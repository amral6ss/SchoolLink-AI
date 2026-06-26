using Common.Results;
using Microsoft.EntityFrameworkCore;
using Project.BLL.DTOs.Common;
using Project.BLL.DTOs.Exam;
using Project.BLL.DTOs.Notifications;
using Project.BLL.Interfaces;
using Project.BLL.Utils;
using Project.DAL.Context;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class ExamManagerService : IExamManagerService
{
    private readonly AppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public ExamManagerService(AppDbContext context, IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    private static readonly TimeZoneInfo _cairoZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Egypt Standard Time" : "Africa/Cairo"
    );

    private static string FormatCairoTime(DateTime? utcTime, string format)
    {
        if (!utcTime.HasValue) return "";
        var cairoTime = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcTime.Value, DateTimeKind.Utc), _cairoZone
        );
        return cairoTime.ToString(format);
    }

    public async Task<OperationResult<PagedResult<ExamManagerItemDto>>> GetAllAsync(ExamManagerFilterDto filter)
    {
        var now = DateTime.UtcNow;

        var query = _context.Exams
            .Include(e => e.Subject)
            .Include(e => e.GradeLevel)
            .Include(e => e.ClassSubjectTeacher).ThenInclude(cst => cst!.Subject)
            .Include(e => e.ClassSubjectTeacher).ThenInclude(cst => cst!.Class).ThenInclude(c => c.GradeLevel)
            .Include(e => e.ClassSubjectTeacher).ThenInclude(cst => cst!.Class).ThenInclude(c => c.Enrollments)
            .Include(e => e.Attempts)
            .Include(e => e.Questions)
            .Include(e => e.Groups).ThenInclude(g => g.Questions)
            .Where(e => !e.IsDeleted)
            .AsQueryable();

        // فلترة الملكية: امتحانات الفصول المحددة (CST) + امتحانات الصف كله (CST=null) للمواد التي يُدرّسها المعلم
        var hasCst = filter.CstIds is { Count: > 0 };
        var hasSubjects = filter.SubjectIds is { Count: > 0 };

        if (hasCst || hasSubjects)
        {
            var cstIds = filter.CstIds ?? new List<int>();
            var subjectIds = filter.SubjectIds ?? new List<int>();

            query = query.Where(e =>
                (e.ClassSubjectTeacherId.HasValue && cstIds.Contains(e.ClassSubjectTeacherId.Value))
                || (e.ClassSubjectTeacherId == null && e.SubjectId.HasValue && subjectIds.Contains(e.SubjectId.Value)));
        }

        // Filter by subject
        if (filter.SubjectId.HasValue)
            query = query.Where(e => e.SubjectId == filter.SubjectId.Value
                                     || e.ClassSubjectTeacher!.SubjectId == filter.SubjectId.Value);

        // Filter by search term
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(term));
        }

        // Apply sorting before fetching
        query = (filter.SortBy?.ToLower()) switch
        {
            "oldest" => query.OrderBy(e => e.CreatedAt),
            "name-asc" => query.OrderBy(e => e.Title),
            "name-desc" => query.OrderByDescending(e => e.Title),
            "date-asc" => query.OrderBy(e => e.StartTime ?? e.CreatedAt),
            "date-desc" => query.OrderByDescending(e => e.StartTime ?? e.CreatedAt),
            _ => query.OrderByDescending(e => e.CreatedAt), // "newest" or default
        };

        // Fetch all matching exams for in-memory status filtering
        var exams = await query.ToListAsync();

        // Apply status filter in memory (since status is computed at runtime)
        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "all")
        {
            exams = exams.Where(e => GetStatus(e, now) == filter.Status).ToList();
        }

        var totalCount = exams.Count;

        // Apply pagination in memory after status filter
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));
        var pagedExams = exams
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var dtos = pagedExams.Select(e =>
        {
            var status = GetStatus(e, now);
            var questionCount = e.Questions.Count + e.Groups.Sum(g => g.Questions.Count);
            var classEntity = e.ClassSubjectTeacher?.Class;
            var gradeLevelName = classEntity?.GradeLevel?.Name ?? e.GradeLevel?.Name ?? "";
            // لو CST=null (نشر للصف كله) → نعرض الصف + "كل الفصول"، لو CST موجود → "الصف - الفصل"
            var className = classEntity != null
                ? $"{classEntity.GradeLevel.Name} - {classEntity.Name}"
                : (!string.IsNullOrEmpty(gradeLevelName) ? $"{gradeLevelName} - كل الفصول" : "");
            var subjectId = e.SubjectId ?? e.ClassSubjectTeacher?.SubjectId;
            var subjectName = e.Subject?.Name ?? e.ClassSubjectTeacher?.Subject.Name ?? "";
            var scoredAttempts = e.Attempts.Where(a => a.Score.HasValue).ToList();
            var avgScore = scoredAttempts.Count > 0
                ? Math.Round(scoredAttempts.Average(a => (double)(a.Score!.Value / (a.TotalScore > 0 ? a.TotalScore : 1) * 100)), 1)
                : (double?)null;
            var totalStudents = classEntity?.Enrollments.Count(e => !e.IsDeleted) ?? 0;
            var pendingGrading = e.Attempts.Count(a => a.SubmittedAt != null && !a.IsGraded);

            return new ExamManagerItemDto(
                e.Id, e.Title, subjectName, className,
                subjectId, e.ClassSubjectTeacher?.ClassId, e.GradeLevelId, gradeLevelName,
                FormatCairoTime(e.StartTime, "yyyy-MM-dd"),
                FormatCairoTime(e.StartTime, "HH:mm"),
                FormatCairoTime(e.EndTime,   "HH:mm"),
                e.DurationMinutes ?? 0,
                questionCount, status,
                avgScore, e.Attempts.Count, totalStudents > 0 ? totalStudents : null,
                e.IsResultPublished,
                pendingGrading > 0 ? pendingGrading : null,
                e.IsAIGenerated
            );
        }).ToList();

        var paged = new PagedResult<ExamManagerItemDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };

        return OperationResult<PagedResult<ExamManagerItemDto>>.Success(paged);
    }

    public async Task<OperationResult<ExamManagerDetailDto>> GetByIdAsync(int id)
    {
        var exam = await _context.Exams
            .Include(e => e.Subject)
            .Include(e => e.GradeLevel)
            .Include(e => e.ClassSubjectTeacher).ThenInclude(cst => cst!.Subject)
            .Include(e => e.ClassSubjectTeacher).ThenInclude(cst => cst!.Class).ThenInclude(c => c.GradeLevel)
            .Include(e => e.Questions).ThenInclude(q => q.Options)
            .Include(e => e.Groups).ThenInclude(g => g.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        if (exam == null)
            return OperationResult<ExamManagerDetailDto>.Failure("الامتحان غير موجود", 404);

        var now = DateTime.UtcNow;
        var status = GetStatus(exam, now);
        var questionCount = exam.Questions.Count + exam.Groups.Sum(g => g.Questions.Count);
        var classEntity = exam.ClassSubjectTeacher?.Class;
        var gradeLevelName = classEntity?.GradeLevel?.Name ?? exam.GradeLevel?.Name ?? "";
        var className = classEntity != null
            ? $"{classEntity.GradeLevel.Name} - {classEntity.Name}"
            : (!string.IsNullOrEmpty(gradeLevelName) ? $"{gradeLevelName} - كل الفصول" : "");
        var subjectId = exam.SubjectId ?? exam.ClassSubjectTeacher?.SubjectId;
        var subjectName = exam.Subject?.Name ?? exam.ClassSubjectTeacher?.Subject.Name ?? "";

        var questions = new List<ExamManagerQuestionDto>();

        foreach (var q in exam.Questions.OrderBy(q => q.DisplayOrder))
        {
            questions.Add(MapQuestion(q));
        }

        foreach (var group in exam.Groups.OrderBy(g => g.DisplayOrder))
        {
            foreach (var q in group.Questions.OrderBy(q => q.DisplayOrder))
            {
                questions.Add(MapQuestion(q));
            }
        }

        var dto = new ExamManagerDetailDto(
            exam.Id, exam.Title, subjectName, className,
            subjectId, exam.ClassSubjectTeacher?.ClassId, exam.GradeLevelId, gradeLevelName,
            FormatCairoTime(exam.StartTime, "yyyy-MM-dd"),
            FormatCairoTime(exam.StartTime, "HH:mm"),
            FormatCairoTime(exam.EndTime,   "HH:mm"),
            exam.DurationMinutes ?? 0,
            questionCount, status, exam.IsResultPublished,
            exam.TotalScore, questions
        );

        return OperationResult<ExamManagerDetailDto>.Success(dto);
    }

    public async Task<OperationResult<ExamManagerStatsDto>> GetStatsAsync(List<int>? cstIds = null, List<int>? subjectIds = null)
    {
        var query = _context.Exams
            .Include(e => e.Attempts)
            .Where(e => !e.IsDeleted)
            .AsQueryable();

        // نفس منطق GetAll: امتحانات CST + امتحانات CST=null للمواد التي يُدرّسها المعلم
        var hasCst = cstIds is { Count: > 0 };
        var hasSubjects = subjectIds is { Count: > 0 };

        if (hasCst || hasSubjects)
        {
            var cIds = cstIds ?? new List<int>();
            var sIds = subjectIds ?? new List<int>();

            query = query.Where(e =>
                (e.ClassSubjectTeacherId.HasValue && cIds.Contains(e.ClassSubjectTeacherId.Value))
                || (e.ClassSubjectTeacherId == null && e.SubjectId.HasValue && sIds.Contains(e.SubjectId.Value)));
        }

        var exams = await query.ToListAsync();
        var now = DateTime.UtcNow;

        var total = exams.Count;
        var upcoming = exams.Count(e => GetStatus(e, now) == "upcoming");
        var ended = exams.Count(e => GetStatus(e, now) == "ended");
        var avgScore = exams.SelectMany(e => e.Attempts)
            .Where(a => a.Score.HasValue && a.TotalScore > 0)
            .Select(a => (double)(a.Score!.Value / a.TotalScore * 100))
            .DefaultIfEmpty(0)
            .Average();

        var stats = new ExamManagerStatsDto(total, upcoming, ended, Math.Round(avgScore, 1));
        return OperationResult<ExamManagerStatsDto>.Success(stats);
    }

    public async Task<OperationResult<ExamManagerDetailDto>> CreateAsync(CreateExamManagerDto dto, int teacherId)
    {
        // التحقق من المادة + الصف الدراسي (مطلوبان دائماً)
        var subject = await _context.Subjects
            .FirstOrDefaultAsync(s => s.Id == dto.SubjectId && !s.IsDeleted);
        if (subject == null)
            return OperationResult<ExamManagerDetailDto>.Failure("المادة غير موجودة", 404);

        var gradeLevel = await _context.GradeLevels
            .FirstOrDefaultAsync(g => g.Id == dto.GradeLevelId && !g.IsDeleted);
        if (gradeLevel == null)
            return OperationResult<ExamManagerDetailDto>.Failure("الصف الدراسي غير موجود", 404);

        // التحقق إن المعلم يُدرّس هذه المادة (صلاحية)
        bool teachesSubject = await _context.ClassSubjectTeachers
            .AnyAsync(c => c.SubjectId == dto.SubjectId && c.TeacherId == teacherId && !c.IsDeleted);
        if (!teachesSubject)
            return OperationResult<ExamManagerDetailDto>.Failure("غير مصرح لك بإنشاء امتحان في هذه المادة", 403);

        int? cstId = null;
        int gradeLevelId = dto.GradeLevelId;

        if (dto.ClassId.HasValue)
        {
            // نشر لفصل محدد → نحل CST المناسب
            var classEntity = await _context.Classes
                .Include(c => c.GradeLevel)
                .FirstOrDefaultAsync(c => c.Id == dto.ClassId.Value && !c.IsDeleted);

            if (classEntity == null)
                return OperationResult<ExamManagerDetailDto>.Failure("الفصل غير موجود", 404);

            var cst = await _context.ClassSubjectTeachers
                .FirstOrDefaultAsync(c =>
                    c.ClassId == dto.ClassId.Value &&
                    c.SubjectId == dto.SubjectId &&
                    c.TeacherId == teacherId &&
                    c.AcademicYearId == classEntity.AcademicYearId &&
                    !c.IsDeleted);

            if (cst == null)
                return OperationResult<ExamManagerDetailDto>.Failure("لا يوجد تدريس لهذه المادة في هذا الفصل", 400);

            cstId = cst.Id;
            gradeLevelId = cst.Class.GradeLevelId;
        }
        // else: نشر للصف كله → CST يبقى null

        var localStartTime = DateTime.Parse(dto.Date + " " + dto.StartTime);
        var localEndTime   = DateTime.Parse(dto.Date + " " + dto.EndTime);
        var startTime = TimeZoneInfo.ConvertTimeToUtc(localStartTime, _cairoZone);
        var endTime   = TimeZoneInfo.ConvertTimeToUtc(localEndTime,   _cairoZone);

        var exam = new Exam
        {
            ClassSubjectTeacherId = cstId,
            SubjectId = dto.SubjectId,
            GradeLevelId = gradeLevelId,
            Title = dto.Title,
            StartTime = startTime,
            EndTime = endTime,
            DurationMinutes = dto.DurationMinutes,
            TotalScore = dto.TotalScore,
            IsPublished = false,
            Category = EvaluationCategory.Academic,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Exams.AddAsync(exam);
        await _unitOfWork.SaveChangesAsync();

        // حفظ الأسئلة اليدوية لو وُجدت
        if (dto.Questions != null)
        {
            var saveResult = await SaveQuestionsAsync(exam.Id, dto.Questions);
            if (!saveResult.IsSuccess)
                return OperationResult<ExamManagerDetailDto>.Failure(saveResult.Message!, saveResult.StatusCode);
        }

        var detail = await GetByIdAsync(exam.Id);
        return detail;
    }

    public async Task<OperationResult> UpdateAsync(int id, CreateExamManagerDto dto, int teacherId)
    {
        var exam = await _unitOfWork.Exams.GetByIdAsync(id);
        if (exam == null || exam.IsDeleted)
            return OperationResult.Failure("الامتحان غير موجود");

        // التحقق من الصلاحية: المعلم لازم يُدرّس المادة دي
        bool teachesSubject = await _context.ClassSubjectTeachers
            .AnyAsync(c => c.SubjectId == dto.SubjectId && c.TeacherId == teacherId && !c.IsDeleted);
        if (!teachesSubject)
            return OperationResult.Failure("غير مصرح لك بتعديل امتحان في هذه المادة", 403);

        // التحقق من الصف الدراسي
        if (dto.GradeLevelId <= 0)
            return OperationResult.Failure("الصف الدراسي مطلوب", 400);

        int? cstId = null;
        int gradeLevelId = dto.GradeLevelId;

        if (dto.ClassId.HasValue)
        {
            var classEntity = await _context.Classes
                .FirstOrDefaultAsync(c => c.Id == dto.ClassId.Value && !c.IsDeleted);
            if (classEntity == null)
                return OperationResult.Failure("الفصل غير موجود", 404);

            var cst = await _context.ClassSubjectTeachers
                .FirstOrDefaultAsync(c =>
                    c.ClassId == dto.ClassId.Value &&
                    c.SubjectId == dto.SubjectId &&
                    c.TeacherId == teacherId &&
                    c.AcademicYearId == classEntity.AcademicYearId &&
                    !c.IsDeleted);

            if (cst == null)
                return OperationResult.Failure("لا يوجد تدريس لهذه المادة في هذا الفصل", 400);

            cstId = cst.Id;
            gradeLevelId = cst.Class.GradeLevelId;
        }

        var localStartTime = DateTime.Parse(dto.Date + " " + dto.StartTime);
        var localEndTime   = DateTime.Parse(dto.Date + " " + dto.EndTime);
        var startTime = TimeZoneInfo.ConvertTimeToUtc(localStartTime, _cairoZone);
        var endTime   = TimeZoneInfo.ConvertTimeToUtc(localEndTime,   _cairoZone);

        exam.Title = dto.Title;
        exam.SubjectId = dto.SubjectId;
        exam.ClassSubjectTeacherId = cstId;
        exam.GradeLevelId = gradeLevelId;
        exam.StartTime = startTime;
        exam.EndTime = endTime;
        exam.DurationMinutes = dto.DurationMinutes;
        exam.TotalScore = dto.TotalScore;
        exam.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Exams.Update(exam);
        await _unitOfWork.SaveChangesAsync();

        // إعادة بناء الأسئلة لو أُرسلت (soft-replace: احذف القديمة وأضف الجديدة)
        if (dto.Questions != null)
        {
            // حذف أسئلة الامتحان المباشرة فقط (ليس المجموعات)
            var oldQuestions = await _context.ExamQuestions
                .Where(q => q.ExamId == id && !q.IsDeleted)
                .ToListAsync();
            foreach (var q in oldQuestions)
            {
                q.IsDeleted = true;
                q.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            var updateSaveResult = await SaveQuestionsAsync(id, dto.Questions);
            if (!updateSaveResult.IsSuccess)
                return updateSaveResult;
        }

        return OperationResult.Success("تم تحديث الامتحان بنجاح");
    }

    public async Task<OperationResult> DeleteAsync(int id)
    {
        var exam = await _unitOfWork.Exams.GetByIdAsync(id);
        if (exam == null || exam.IsDeleted)
            return OperationResult.Failure("الامتحان غير موجود");

        _unitOfWork.Exams.SoftDelete(exam);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult.Success("تم حذف الامتحان بنجاح");
    }

    public async Task<OperationResult> PublishAsync(int id, int teacherId)
    {
        var exam = await _context.Exams
            .Include(e => e.ClassSubjectTeacher)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        if (exam is null)
            return OperationResult.Failure("الامتحان غير موجود", 404);

        var ownerCheck = await EnsureTeacherCanManageExamAsync(exam, teacherId);
        if (!ownerCheck.IsSuccess)
            return ownerCheck;

        exam.IsPublished = true;
        exam.UpdatedAt = DateTime.UtcNow;

        _context.Exams.Update(exam);
        await _context.SaveChangesAsync();

        // Notify students about published exam
        await NotifyExamStudentsAsync(exam, "امتحان جديد",
            $"تم نشر الامتحان: {exam.Title}", NotificationType.Exam);

        return OperationResult.Success("تم نشر الامتحان بنجاح");
    }

    public async Task<OperationResult> ToggleResultPublishStatusAsync(int id, bool isPublished, int teacherId)
    {
        var exam = await _context.Exams
            .Include(e => e.ClassSubjectTeacher)
            .Include(e => e.Attempts)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        if (exam is null)
            return OperationResult.Failure("الامتحان غير موجود", 404);

        var ownerCheck = await EnsureTeacherCanManageExamAsync(exam, teacherId);
        if (!ownerCheck.IsSuccess)
            return ownerCheck;

        // Phase 6.4: منع النشر لو فيه محاولات لم تُصحح بعد
        if (isPublished)
        {
            bool hasPending = exam.Attempts.Any(a => a.SubmittedAt != null && !a.IsGraded);
            if (hasPending)
                return OperationResult.Failure("لا يمكن نشر النتائج، يوجد إجابات لم تُصحح بعد", 400);
        }

        exam.IsResultPublished = isPublished;
        exam.UpdatedAt = DateTime.UtcNow;

        _context.Exams.Update(exam);
        await _context.SaveChangesAsync();

        // Notify students about published results
        if (isPublished)
        {
            await NotifyExamStudentsAsync(exam, "نتائج الامتحان",
                $"تم نشر نتائج الامتحان: {exam.Title}", NotificationType.ExamResult);
        }

        return OperationResult.Success();
    }

    /// <summary>
    /// التحقق من ملكية/صلاحية إدارة الامتحان:
    ///   - لو CST موجود: المعلم لازم يكون صاحبه (TeacherId).
    ///   - لو CST=null (نشر للصف): المعلم لازم يُدرّس المادة دي (SubjectId).
    /// </summary>
    private async Task<OperationResult> EnsureTeacherCanManageExamAsync(Exam exam, int teacherId)
    {
        // امتحان مربوط بفصل محدد → التحقق المباشر
        if (exam.ClassSubjectTeacher is not null)
        {
            if (exam.ClassSubjectTeacher.TeacherId != teacherId)
                return OperationResult.Failure("غير مصرح لك بإدارة هذا الامتحان", 403);
            return OperationResult.Success();
        }

        // امتحان CST=null (نشر للصف) → المعلم لازم يُدرّس المادة
        if (exam.SubjectId.HasValue)
        {
            bool teachesSubject = await _context.ClassSubjectTeachers
                .AnyAsync(c => c.SubjectId == exam.SubjectId.Value
                            && c.TeacherId == teacherId
                            && !c.IsDeleted);
            if (!teachesSubject)
                return OperationResult.Failure("غير مصرح لك بإدارة هذا الامتحان", 403);
            return OperationResult.Success();
        }

        return OperationResult.Failure("غير مصرح لك بإدارة هذا الامتحان", 403);
    }

    private static string GetStatus(Exam exam, DateTime _ )
    {
        // بعد إصلاح التخزين: الوقت مخزّن UTC، فنقارن UTC بـ UTC مباشرة
        var now = DateTime.UtcNow;

        if (!exam.IsPublished)
            return "draft";
        if (exam.StartTime == null || exam.EndTime == null)
            return "draft";
        if (now < exam.StartTime)
            return "upcoming";
        if (now >= exam.StartTime && now <= exam.EndTime)
            return "active";
        return "ended";
    }

    // Phase 3: حفظ أسئلة يدوية جديدة لامتحان
    private async Task<OperationResult> SaveQuestionsAsync(int examId, List<CreateExamManagerQuestionDto> questions)
    {
        var now = DateTime.UtcNow;
        int order = 1;

        foreach (var q in questions)
        {
            var qType = q.Type switch
            {
                "mcq"         => QuestionType.MultipleChoice,
                "true-false"  => QuestionType.TrueFalse,
                "fill-blank"  => QuestionType.FillBlank,
                _             => QuestionType.Essay,
            };

            // Validation (الطبقة 4): رفض القيم غير الصالحة لأسئلة صح/خطأ
            if (qType == QuestionType.TrueFalse && !BooleanNormalizer.IsValidTrueFalseAnswer(q.CorrectAnswer))
                return OperationResult.Failure($"قيمة الإجابة الصحيحة لسؤال صح/خطأ غير صالحة: \"{q.CorrectAnswer}\"");

            // تطبيع (الطبقة 2): تخزين True/False canonical لأسئلة صح/خطأ
            var canonicalAnswer = BooleanNormalizer.NormalizeCanonicalCorrectAnswer(qType, q.CorrectAnswer) ?? "";

            var question = new ExamQuestion
            {
                ExamId       = examId,
                QuestionText = q.Text,
                QuestionType = qType,
                CorrectAnswer = canonicalAnswer,
                Points       = q.Points,
                DisplayOrder = order++,
                CreatedAt    = now,
                UpdatedAt    = now,
            };
            _context.ExamQuestions.Add(question);
            await _context.SaveChangesAsync();

            if ((qType == QuestionType.MultipleChoice || qType == QuestionType.TrueFalse)
                && q.Options?.Count > 0)
            {
                int optOrder = 1;
                foreach (var opt in q.Options)
                {
                    _context.ExamQuestionOptions.Add(new ExamQuestionOption
                    {
                        QuestionId   = question.Id,
                        OptionText   = opt,
                        IsCorrect    = opt == q.CorrectAnswer,
                        DisplayOrder = optOrder++,
                        CreatedAt    = now,
                        UpdatedAt    = now,
                    });
                }
                await _context.SaveChangesAsync();
            }
        }

        return OperationResult.Success();
    }

    private static ExamManagerQuestionDto MapQuestion(ExamQuestion q)
    {
        var type = q.QuestionType switch
        {
            QuestionType.MultipleChoice => "mcq",
            QuestionType.TrueFalse => "true-false",
            QuestionType.FillBlank => "fill-blank",
            QuestionType.Essay => "essay",
            _ => "mcq"
        };

        List<string>? options = null;
        string correctAnswer = "";

        if (q.QuestionType == QuestionType.MultipleChoice || q.QuestionType == QuestionType.TrueFalse)
        {
            options = q.Options.OrderBy(o => o.DisplayOrder).Select(o => o.OptionText).ToList();
            var correct = q.Options.FirstOrDefault(o => o.IsCorrect);
            correctAnswer = correct?.OptionText ?? "";
        }
        else if (q.QuestionType == QuestionType.FillBlank || q.QuestionType == QuestionType.Essay)
        {
            correctAnswer = q.CorrectAnswer ?? "";
        }

        return new ExamManagerQuestionDto(q.Id, type, q.QuestionText, options, correctAnswer, q.Points);
    }

    /// <summary>
    /// إرسال إشعار إلى جميع الطلاب المستهدفين (و أولياء أمورهم) بعد نشر الامتحان أو نتيجته
    /// </summary>
    private async Task NotifyExamStudentsAsync(Exam exam, string title, string body, NotificationType type)
    {
        var studentIds = await GetExamStudentUserIdsAsync(exam);
        if (studentIds.Count == 0) return;

        await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
        {
            UserIds = studentIds,
            Title = title,
            Body = body,
            Type = type
        });

        // Notify parents too
        var parentIds = new List<int>();
        var enrollments = await _context.StudentEnrollments
            .Where(e => studentIds.Contains(e.Student.UserId!.Value))
            .ToListAsync();
        foreach (var enrollment in enrollments.DistinctBy(e => e.StudentId))
        {
            var parentLinks = await _context.ParentStudents
                .Where(ps => ps.StudentId == enrollment.StudentId)
                .ToListAsync();
            parentIds.AddRange(parentLinks.Select(ps => ps.ParentId));
        }
        parentIds = parentIds.Distinct().ToList();
        if (parentIds.Count == 0) return;

        await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
        {
            UserIds = parentIds,
            Title = title,
            Body = body,
            Type = type
        });
    }

    /// <summary>
    /// يجلب UserIds جميع الطلاب المستهدفين في امتحان (حسب الفصل أو الصف)
    /// </summary>
    private async Task<List<int>> GetExamStudentUserIdsAsync(Exam exam)
    {
        if (exam.ClassSubjectTeacherId.HasValue)
        {
            var cst = await _context.ClassSubjectTeachers
                .FirstOrDefaultAsync(c => c.Id == exam.ClassSubjectTeacherId.Value);
            if (cst == null) return new List<int>();

            return await _context.StudentEnrollments
                .Where(e => e.ClassId == cst.ClassId
                         && e.AcademicYearId == cst.AcademicYearId
                         && e.LeftAt == null
                         && !e.IsDeleted
                         && e.Student != null && e.Student.UserId != null && e.Student.IsActive
                         && e.Student.LifecycleStatus == StudentLifecycleStatus.Active)
                .Select(e => e.Student.UserId!.Value)
                .Distinct()
                .ToListAsync();
        }

        // Grade-level exam (no specific CST)
        return await _context.StudentEnrollments
            .Where(e => e.Class.GradeLevelId == exam.GradeLevelId
                     && e.LeftAt == null
                     && !e.IsDeleted
                     && e.Student != null && e.Student.UserId != null && e.Student.IsActive
                     && e.Student.LifecycleStatus == StudentLifecycleStatus.Active)
            .Select(e => e.Student.UserId!.Value)
            .Distinct()
            .ToListAsync();
    }
}
