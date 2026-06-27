using Common.Results;
using Microsoft.EntityFrameworkCore;
using Project.BLL.DTOs.Assignment;
using Project.BLL.DTOs.Common;
using Project.BLL.Interfaces;
using Project.DAL.Context;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class AssignmentManagerService : IAssignmentManagerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDbContext _context;

    public AssignmentManagerService(IUnitOfWork unitOfWork, AppDbContext context)
    {
        _unitOfWork = unitOfWork;
        _context = context;
    }

    private static readonly TimeZoneInfo _cairoZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Egypt Standard Time" : "Africa/Cairo");

    private static string FormatCairoTime(DateTime? utcTime, string format)
    {
        if (!utcTime.HasValue) return "";
        var cairoTime = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcTime.Value, DateTimeKind.Utc), _cairoZone);
        return cairoTime.ToString(format);
    }

    public async Task<OperationResult<List<AssignmentManagerItemDto>>> GetAllAsync(int? classSubjectTeacherId = null)
    {
        var query = _context.Assignments
            .Include(a => a.ClassSubjectTeacher)
                .ThenInclude(c => c.Subject)
            .Include(a => a.ClassSubjectTeacher)
                .ThenInclude(c => c.Class)
                    .ThenInclude(cl => cl.Enrollments)
            .Include(a => a.Submissions)
            .Where(a => !a.IsDeleted)
            .AsQueryable();

        if (classSubjectTeacherId.HasValue)
            query = query.Where(a => a.ClassSubjectTeacherId == classSubjectTeacherId.Value);

        var assignments = await query.ToListAsync();

        var items = assignments.Select(a =>
        {
            var totalStudents = a.ClassSubjectTeacher?.Class?.Enrollments
                .Count(e => !e.IsDeleted && e.AcademicYearId == a.ClassSubjectTeacher.AcademicYearId) ?? 0;
            var submitted = a.Submissions.Count(s => !s.IsDeleted);

            return new AssignmentManagerItemDto
            {
                Id = a.Id,
                Title = a.Title,
                Subject = a.ClassSubjectTeacher?.Subject?.Name ?? "",
                Class = a.ClassSubjectTeacher?.Class?.Name ?? "",
                Deadline = FormatCairoTime(a.DueDate, "yyyy-MM-ddTHH:mm"),
                Submitted = submitted,
                Total = totalStudents,
                Status = GetStatus(a)
            };
        }).ToList();

        return OperationResult<List<AssignmentManagerItemDto>>.Success(items);
    }

    public async Task<OperationResult<PagedResult<AssignmentManagerItemDto>>> GetFilteredAsync(AssignmentFilterDto filter)
    {
        var query = _context.Assignments
            .Include(a => a.ClassSubjectTeacher)
                .ThenInclude(c => c.Subject)
            .Include(a => a.ClassSubjectTeacher)
                .ThenInclude(c => c.Class)
                    .ThenInclude(cl => cl.Enrollments)
            .Include(a => a.Submissions)
            .Include(a => a.Questions)
            .Where(a => !a.IsDeleted)
            .AsQueryable();

        // Filter by teacher (if specified)
        if (filter.TeacherId.HasValue)
        {
            var cstIds = await _context.ClassSubjectTeachers
                .Where(c => c.TeacherId == filter.TeacherId.Value && c.AcademicYearId == filter.AcademicYearId && !c.IsDeleted)
                .Select(c => c.Id)
                .ToListAsync();
            query = query.Where(a => cstIds.Contains(a.ClassSubjectTeacherId));
        }

        // Filter by subject
        if (filter.SubjectId.HasValue)
            query = query.Where(a => a.ClassSubjectTeacher.SubjectId == filter.SubjectId.Value);

        // Filter by search term
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(a => a.Title.ToLower().Contains(term));
        }

        // Apply sorting
        query = (filter.SortBy?.ToLower()) switch
        {
            "oldest" => query.OrderBy(a => a.CreatedAt),
            "name-asc" => query.OrderBy(a => a.Title),
            "name-desc" => query.OrderByDescending(a => a.Title),
            "date-asc" => query.OrderBy(a => a.DueDate ?? a.CreatedAt),
            "date-desc" => query.OrderByDescending(a => a.DueDate ?? a.CreatedAt),
            _ => query.OrderByDescending(a => a.CreatedAt),
        };

        // Fetch all for in-memory status filtering
        var assignments = await query.ToListAsync();

        // Status filter in memory (computed)
        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "all")
        {
            assignments = assignments.Where(a => GetStatus(a) == filter.Status).ToList();
        }

        var totalCount = assignments.Count;
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));
        var paged = assignments
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var dtos = paged.Select(a =>
        {
            var totalStudents = a.ClassSubjectTeacher?.Class?.Enrollments
                .Count(e => !e.IsDeleted && e.AcademicYearId == a.ClassSubjectTeacher.AcademicYearId) ?? 0;
            var submitted = a.Submissions.Count(s => !s.IsDeleted);
            var gradedSubmissions = a.Submissions.Where(s => !s.IsDeleted && s.IsGraded && s.Score.HasValue).ToList();
            var avgScore = gradedSubmissions.Count > 0
                ? Math.Round(gradedSubmissions.Average(s => (double)(s.Score!.Value / (a.MaxScore > 0 ? a.MaxScore : 1) * 100)), 1)
                : (double?)null;

            return new AssignmentManagerItemDto
            {
                Id = a.Id,
                Title = a.Title,
                Subject = a.ClassSubjectTeacher?.Subject?.Name ?? "",
                Class = a.ClassSubjectTeacher?.Class != null
                    ? $"{a.ClassSubjectTeacher.Class.GradeLevel?.Name} - {a.ClassSubjectTeacher.Class.Name}"
                    : "",
                Deadline = FormatCairoTime(a.DueDate, "yyyy-MM-ddTHH:mm"),
                MaxScore = a.MaxScore,
                IsPublished = a.IsPublished,
                IsAIGenerated = a.IsAIGenerated,
                QuestionsCount = a.Questions.Count(q => !q.IsDeleted),
                Submitted = submitted,
                Total = totalStudents,
                AvgScore = avgScore,
                Status = GetStatus(a)
            };
        }).ToList();

        var result = new PagedResult<AssignmentManagerItemDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };

        return OperationResult<PagedResult<AssignmentManagerItemDto>>.Success(result);
    }

    public async Task<OperationResult<AssignmentManagerDetailDto>> GetByIdAsync(int id)
    {
        var assignment = await _context.Assignments
            .Include(a => a.ClassSubjectTeacher)
                .ThenInclude(c => c.Subject)
            .Include(a => a.ClassSubjectTeacher)
                .ThenInclude(c => c.Class)
                    .ThenInclude(cl => cl.Enrollments)
            .Include(a => a.Submissions)
            .Include(a => a.Questions.Where(q => !q.IsDeleted))
                .ThenInclude(q => q.Options.Where(o => !o.IsDeleted))
            .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);

        if (assignment is null)
            return OperationResult<AssignmentManagerDetailDto>.Failure("الواجب غير موجود", 404);

        var totalStudents = assignment.ClassSubjectTeacher?.Class?.Enrollments
            .Count(e => !e.IsDeleted && e.AcademicYearId == assignment.ClassSubjectTeacher.AcademicYearId) ?? 0;
        var submitted = assignment.Submissions.Count(s => !s.IsDeleted);

        var dto = new AssignmentManagerDetailDto
        {
            Id = assignment.Id,
            Title = assignment.Title,
            Subject = assignment.ClassSubjectTeacher?.Subject?.Name ?? "",
            Class = assignment.ClassSubjectTeacher?.Class != null
                ? $"{assignment.ClassSubjectTeacher.Class.GradeLevel?.Name} - {assignment.ClassSubjectTeacher.Class.Name}"
                : "",
            Deadline = assignment.DueDate?.ToString("yyyy-MM-ddTHH:mm") ?? "",
            MaxScore = assignment.MaxScore,
            IsPublished = assignment.IsPublished,
            IsAIGenerated = assignment.IsAIGenerated,
            QuestionsCount = assignment.Questions.Count(q => !q.IsDeleted),
            Submitted = submitted,
            Total = totalStudents,
            Status = GetStatus(assignment),
            Questions = assignment.Questions
                .Where(q => !q.IsDeleted)
                .OrderBy(q => q.DisplayOrder)
                .Select(q => new AssignmentManagerQuestionDto
                {
                    Id = q.Id,
                    Type = MapType(q.QuestionType),
                    Text = q.QuestionText,
                    Options = q.Options
                        .Where(o => !o.IsDeleted)
                        .OrderBy(o => o.DisplayOrder)
                        .Select(o => o.OptionText)
                        .ToList(),
                    CorrectAnswer = q.CorrectAnswer ?? "",
                    Points = q.Points
                }).ToList()
        };

        return OperationResult<AssignmentManagerDetailDto>.Success(dto);
    }

    public async Task<OperationResult<AssignmentManagerItemDto>> CreateAsync(CreateAssignmentManagerDto dto, int teacherId)
    {
        var academicYear = await _unitOfWork.AcademicYears
            .FindAsync(y => y.IsCurrent && !y.IsDeleted);
        var year = academicYear.FirstOrDefault();
        if (year is null)
            return OperationResult<AssignmentManagerItemDto>.Failure("لا يوجد عام دراسي نشط", 400);

        var csts = await _unitOfWork.ClassSubjectTeachers
            .FindAsync(c => c.ClassId == dto.ClassId && c.SubjectId == dto.SubjectId
                && c.AcademicYearId == year.Id && !c.IsDeleted);
        var cst = csts.FirstOrDefault();
        if (cst is null)
            return OperationResult<AssignmentManagerItemDto>.Failure("لم يتم العثور على رابط الفصل-المادة-المدرس", 400);

        DateTime? dueDate = null;
        if (DateTime.TryParse(dto.Deadline, out var parsed))
            dueDate = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), _cairoZone);

        var assignment = new Assignment
        {
            ClassSubjectTeacherId = cst.Id,
            Title = dto.Title,
            DueDate = dueDate,
            MaxScore = dto.Questions.Sum(q => q.Points > 0 ? q.Points : (q.Type == "essay" ? 10 : 5)),
            IsAutoGraded = true,
            IsPublished = false,
            Category = EvaluationCategory.Academic,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Assignments.AddAsync(assignment);
        await _unitOfWork.SaveChangesAsync();

        int displayOrder = 1;
        foreach (var qDto in dto.Questions)
        {
            var question = new AssignmentQuestion
            {
                AssignmentId = assignment.Id,
                QuestionText = qDto.Text,
                QuestionType = MapTypeBack(qDto.Type),
                CorrectAnswer = qDto.CorrectAnswer,
                DisplayOrder = displayOrder++,
                Points = qDto.Points > 0 ? qDto.Points : (qDto.Type == "essay" ? 10 : 5),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.AssignmentQuestions.AddAsync(question);
            await _unitOfWork.SaveChangesAsync();

            if (qDto.Type == "mcq" || qDto.Type == "true-false")
            {
                int optOrder = 1;
                foreach (var optText in qDto.Options)
                {
                    var option = new AssignmentQuestionOption
                    {
                        QuestionId = question.Id,
                        OptionText = optText,
                        IsCorrect = optText == qDto.CorrectAnswer,
                        DisplayOrder = optOrder++,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _unitOfWork.AssignmentQuestionOptions.AddAsync(option);
                }
                await _unitOfWork.SaveChangesAsync();
            }
        }

        return OperationResult<AssignmentManagerItemDto>.Success(new AssignmentManagerItemDto
        {
            Id = assignment.Id,
            Title = assignment.Title,
            Subject = cst.Subject?.Name ?? "",
            Class = cst.Class?.Name ?? "",
            Deadline = FormatCairoTime(dueDate, "yyyy-MM-ddTHH:mm"),
            Submitted = 0,
            Total = cst.Class?.Enrollments
                .Count(e => !e.IsDeleted && e.AcademicYearId == year.Id) ?? 0,
            Status = "draft"
        });
    }

    public async Task<OperationResult> UpdateAsync(int id, UpdateAssignmentManagerDto dto)
    {
        var assignment = await _unitOfWork.Assignments.GetWithQuestionsAsync(id);
        if (assignment is null || assignment.IsDeleted)
            return OperationResult.Failure("الواجب غير موجود", 404);

        var submissionCount = await _context.StudentAssignmentSubmissions.CountAsync(s => s.AssignmentId == id && !s.IsDeleted);
        if (submissionCount > 0)
            return OperationResult.Failure("لا يمكن تعديل الواجب لوجود طلاب قاموا بالتسليم بالفعل", 400);

        DateTime? dueDate = null;
        if (DateTime.TryParse(dto.Deadline, out var parsed))
            dueDate = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), _cairoZone);

        assignment.Title = dto.Title;
        assignment.DueDate = dueDate;
        assignment.MaxScore = dto.Questions.Sum(q => q.Points > 0 ? q.Points : (q.Type == "essay" ? 10 : 5));
        assignment.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Assignments.Update(assignment);

        var existingQuestions = assignment.Questions.Where(q => !q.IsDeleted).ToList();
        foreach (var eq in existingQuestions)
        {
            eq.IsDeleted = true;
            eq.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.AssignmentQuestions.Update(eq);
        }

        int displayOrder = 1;
        foreach (var qDto in dto.Questions)
        {
            var question = new AssignmentQuestion
            {
                AssignmentId = id,
                QuestionText = qDto.Text,
                QuestionType = MapTypeBack(qDto.Type),
                CorrectAnswer = qDto.CorrectAnswer,
                DisplayOrder = displayOrder++,
                Points = qDto.Points > 0 ? qDto.Points : (qDto.Type == "essay" ? 10 : 5),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.AssignmentQuestions.AddAsync(question);
            await _unitOfWork.SaveChangesAsync();

            if (qDto.Type == "mcq" || qDto.Type == "true-false")
            {
                int optOrder = 1;
                foreach (var optText in qDto.Options)
                {
                    var option = new AssignmentQuestionOption
                    {
                        QuestionId = question.Id,
                        OptionText = optText,
                        IsCorrect = optText == qDto.CorrectAnswer,
                        DisplayOrder = optOrder++,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _unitOfWork.AssignmentQuestionOptions.AddAsync(option);
                }
                await _unitOfWork.SaveChangesAsync();
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success("تم تحديث الواجب بنجاح");
    }

    public async Task<OperationResult> DeleteAsync(int id, int userId, string role)
    {
        var assignment = await _unitOfWork.Assignments.GetByIdAsync(id);
        if (assignment is null || assignment.IsDeleted)
            return OperationResult.Failure("الواجب غير موجود", 404);

        // Admin can delete any assignment; Teacher can only delete their own
        if (role == "Teacher")
        {
            var ownsAssignment = await _context.ClassSubjectTeachers
                .AnyAsync(cst => cst.Id == assignment.ClassSubjectTeacherId && cst.TeacherId == userId);
            if (!ownsAssignment)
                return OperationResult.Failure("لا يمكنك حذف هذا الواجب — أنت لست مدرس المادة", 403);
        }

        var submissionCount = await _context.StudentAssignmentSubmissions.CountAsync(s => s.AssignmentId == id && !s.IsDeleted);
        if (submissionCount > 0)
            return OperationResult.Failure("لا يمكن حذف الواجب لوجود طلاب قاموا بالتسليم بالفعل", 400);

        assignment.IsDeleted = true;
        assignment.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Assignments.Update(assignment);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult.Success("تم حذف الواجب بنجاح");
    }

    public async Task<OperationResult<AssignmentManagerStatsDto>> GetStatsAsync(AssignmentFilterDto filter)
    {
        // نعتمد على نفس مصدر بيانات القائمة (GetFilteredAsync) حتى تبقى
        // الإحصائيات متناسقة 100% مع الواجبات المعروضة لنفس المُعلِّم/السنة.
        var filtered = await GetFilteredAsync(filter);
        if (!filtered.IsSuccess || filtered.Data is null)
            return OperationResult<AssignmentManagerStatsDto>.Success(new AssignmentManagerStatsDto());

        var list = filtered.Data.Items.ToList();
        return OperationResult<AssignmentManagerStatsDto>.Success(new AssignmentManagerStatsDto
        {
            Total = filtered.Data.TotalCount,
            Active = list.Count(a => a.Status == "open"),
            AvgDelivery = list.Count > 0
                ? Math.Round(list.Average(a => a.Total > 0 ? (double)a.Submitted / a.Total * 100 : 0), 1)
                : 0,
            Overdue = list.Count(a => a.Status == "open" && a.Submitted < a.Total)
        });
    }

    public async Task<OperationResult<List<AssignmentSubmissionListItemDto>>> GetSubmissionsAsync(int assignmentId)
    {
        var submissions = await _context.StudentAssignmentSubmissions
            .Include(s => s.Enrollment)
                .ThenInclude(e => e.Student)
                    .ThenInclude(st => st.User)
            .Where(s => s.AssignmentId == assignmentId && !s.IsDeleted)
            .ToListAsync();

        var list = submissions.Select(s => new AssignmentSubmissionListItemDto
        {
            SubmissionId = s.Id,
            StudentName = s.Enrollment?.Student?.User?.FullName ?? "غير معروف",
            SubmittedAt = s.SubmittedAt.ToString("yyyy-MM-ddTHH:mm"),
            IsGraded = s.IsGraded,
            Score = s.Score ?? 0m,
            MaxScore = s.MaxScore
        }).ToList();

        return OperationResult<List<AssignmentSubmissionListItemDto>>.Success(list);
    }

    public async Task<OperationResult<AssignmentSubmissionDetailDto>> GetSubmissionDetailAsync(int assignmentId, int submissionId)
    {
        var submission = await _context.StudentAssignmentSubmissions
            .Include(s => s.Enrollment)
                .ThenInclude(e => e.Student)
                    .ThenInclude(st => st.User)
            .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
                    .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.AssignmentId == assignmentId && !s.IsDeleted);

        if (submission == null)
            return OperationResult<AssignmentSubmissionDetailDto>.Failure("التسليم غير موجود", 404);

        var dto = new AssignmentSubmissionDetailDto
        {
            SubmissionId = submission.Id,
            StudentName = submission.Enrollment?.Student?.User?.FullName ?? "غير معروف",
            Score = submission.Score ?? 0m,
            MaxScore = submission.MaxScore,
            IsGraded = submission.IsGraded,
            Answers = submission.Answers.Select(a => new AssignmentSubmissionAnswerDto
            {
                QuestionId = a.QuestionId,
                QuestionText = a.Question?.QuestionText ?? "",
                Type = MapType(a.Question?.QuestionType ?? QuestionType.MultipleChoice),
                StudentAnswer = a.Question?.QuestionType == QuestionType.MultipleChoice 
                    ? a.Question.Options.FirstOrDefault(o => o.Id == a.SelectedOptionId)?.OptionText ?? ""
                    : a.Question?.QuestionType == QuestionType.TrueFalse 
                        ? (a.BooleanAnswer == true ? "صواب" : a.BooleanAnswer == false ? "خطأ" : "")
                        : a.AnswerText ?? "",
                CorrectAnswer = a.Question?.CorrectAnswer ?? "",
                PointsEarned = a.PointsEarned,
                MaxPoints = a.Question?.Points ?? 0,
                IsCorrect = a.IsCorrect
            }).ToList()
        };

        return OperationResult<AssignmentSubmissionDetailDto>.Success(dto);
    }

    public async Task<OperationResult> GradeSubmissionAsync(int assignmentId, int submissionId, GradeAssignmentSubmissionDto dto)
    {
        var submission = await _context.StudentAssignmentSubmissions
            .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.AssignmentId == assignmentId && !s.IsDeleted);

        if (submission == null)
            return OperationResult.Failure("التسليم غير موجود", 404);

        if (submission.IsGraded)
            return OperationResult.Failure("تم تصحيح هذا الواجب مسبقاً", 400);

        foreach (var answer in submission.Answers)
        {
            if (answer.Question?.QuestionType == QuestionType.Essay && dto.ManualGrades.TryGetValue(answer.QuestionId, out var manualPoints))
            {
                answer.PointsEarned = manualPoints;
                answer.IsCorrect = manualPoints > 0; // Or logic based on max points
            }
        }

        submission.Score = submission.Answers.Sum(a => a.PointsEarned);
        submission.IsGraded = true;
        
        _context.StudentAssignmentSubmissions.Update(submission);
        await _context.SaveChangesAsync();

        return OperationResult.Success("تم حفظ التصحيح بنجاح");
    }

    private static string GetStatus(Assignment a)
    {
        if (!a.IsPublished) return "draft";
        // DueDate مخزّن UTC بعد إصلاح التخزين — نقارن UTC vs UTC مباشرة
        if (a.DueDate.HasValue && a.DueDate.Value < DateTime.UtcNow) return "closed";
        return "open";
    }

    private static string MapType(QuestionType type) => type switch
    {
        QuestionType.MultipleChoice => "mcq",
        QuestionType.TrueFalse => "true-false",
        QuestionType.Essay => "essay",
        _ => "mcq"
    };

    private static QuestionType MapTypeBack(string type) => type switch
    {
        "mcq" => QuestionType.MultipleChoice,
        "true-false" => QuestionType.TrueFalse,
        "essay" => QuestionType.Essay,
        _ => QuestionType.MultipleChoice
    };
}
