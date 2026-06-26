using Common.Results;
using Project.BLL.DTOs.ChildProgress;
using Project.BLL.Interfaces;
using Project.BLL.Utils;
using Project.DAL.Interfaces;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class ChildProgressService : IChildProgressService
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildProgressService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

    public async Task<OperationResult<List<ChildProgressItemDto>>> GetChildProgressAsync(int parentUserId, int? term = null)
    {
        var currentYear = await _unitOfWork.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent && !y.IsDeleted);
        if (currentYear is null)
            return OperationResult<List<ChildProgressItemDto>>.Success(new List<ChildProgressItemDto>(), "لا توجد سنة دراسية حالية");

        // Determine term date range (used to filter assignments/exams by term)
        DateOnly termStartDate, termEndDate;
        if (term.HasValue)
        {
            termStartDate = term.Value == 1
                ? (currentYear.FirstSemesterStartDate ?? currentYear.StartDate)
                : (currentYear.SecondSemesterStartDate ?? currentYear.StartDate);
            termEndDate = term.Value == 1
                ? (currentYear.FirstSemesterEndDate ?? currentYear.EndDate)
                : (currentYear.SecondSemesterEndDate ?? currentYear.EndDate);
        }
        else
        {
            termStartDate = currentYear.StartDate;
            termEndDate = currentYear.EndDate;
        }

        var links = await _unitOfWork.ParentStudents.GetWithStudentDetailsByParentAsync(parentUserId);
        var activeLinks = links.Where(l => !l.IsDeleted && l.Student is { IsDeleted: false }).ToList();

        var results = new List<ChildProgressItemDto>();

        foreach (var link in activeLinks)
        {
            var student = link.Student!;
            var enrollment = student.Enrollments.FirstOrDefault(e => e.LeftAt == null && e.AcademicYearId == currentYear.Id && !e.IsDeleted);
            if (enrollment is null) continue;

            var className = enrollment.Class?.Name ?? "";
            var gradeName = enrollment.Class?.GradeLevel?.Name ?? "";

            var csts = await _unitOfWork.ClassSubjectTeachers
                .FindAsync(c => c.ClassId == enrollment.ClassId && c.AcademicYearId == currentYear.Id && !c.IsDeleted);
            var cstIds = csts.Select(c => c.Id).ToHashSet();

            // Build subject name lookup for assignments
            var cstSubjectNames = new Dictionary<int, string>();
            var subjectIdsFromCst = csts.Select(c => c.SubjectId).Distinct().ToList();
            if (subjectIdsFromCst.Count > 0)
            {
                var subjects = await _unitOfWork.Subjects.FindAsync(s => subjectIdsFromCst.Contains(s.Id));
                var subjectNames = subjects.ToDictionary(s => s.Id, s => s.Name);
                foreach (var cst in csts)
                    cstSubjectNames[cst.Id] = subjectNames.GetValueOrDefault(cst.SubjectId, "");
            }

            // — Assignments —
            var assignments = await _unitOfWork.Assignments
                .FindAsync(a => cstIds.Contains(a.ClassSubjectTeacherId) && !a.IsDeleted && a.IsPublished);
            var submissions = await _unitOfWork.StudentAssignmentSubmissions.GetByEnrollmentIdAsync(enrollment.Id);
            var submissionMap = submissions.ToDictionary(s => s.AssignmentId);

            var assignmentDtos = assignments
                .Where(a => FilterByTerm(a.DueDate, termStartDate, termEndDate))
                .Select(a =>
            {
                var subject = cstSubjectNames.GetValueOrDefault(a.ClassSubjectTeacherId, "");
                var sub = submissionMap.GetValueOrDefault(a.Id);

                string status;
                double? score;

                if (sub is not null)
                {
                    status = sub.IsGraded ? "submitted" : "pending";
                    score = sub.IsGraded && sub.Score.HasValue ? (double)sub.Score.Value : null;
                }
                else if (a.DueDate.HasValue && a.DueDate.Value < DateTime.UtcNow)
                {
                    status = "late";
                    score = null;
                }
                else
                {
                    status = "not-submitted";
                    score = null;
                }

                return new AssignmentProgressDto
                {
                    Id = a.Id,
                    Subject = subject,
                    Title = a.Title,
                    Deadline = FormatCairoTime(a.DueDate, "yyyy-MM-dd"),
                    Status = status,
                    Score = score,
                    MaxScore = (double)a.MaxScore,
                };
            }).ToList();

            // — Exams (same approach as StudentExamService.GetMyExamsAsync) —
            var exams = await _unitOfWork.Exams.GetPublishedForEnrollmentAsync(enrollment.Id);
            var attempts = await _unitOfWork.StudentExamAttempts.GetByEnrollmentIdAsync(enrollment.Id);
            var attemptMap = attempts.ToDictionary(a => a.ExamId);

            var examDtos = exams
                .Where(e => FilterByTerm(e.StartTime, termStartDate, termEndDate))
                .Select(e =>
            {
                // Same logic as StudentExamService.GetSubjectName:
                // exam.Subject?.Name ?? exam.ClassSubjectTeacher?.Subject?.Name
                var subject = e.Subject?.Name ?? e.ClassSubjectTeacher?.Subject?.Name ?? "";

                var att = attemptMap.GetValueOrDefault(e.Id);

                string status;
                double? score;

                if (att is { IsGraded: true } && att.Score.HasValue)
                {
                    status = "done";
                    score = (double)att.Score.Value;
                }
                else if (e.StartTime.HasValue && e.StartTime.Value > DateTime.UtcNow)
                {
                    status = "upcoming";
                    score = null;
                }
                else if (att is not null)
                {
                    status = "pending";
                    score = null;
                }
                else
                {
                    status = "missed";
                    score = null;
                }

                return new ExamProgressDto
                {
                    Id = e.Id,
                    Subject = subject,
                    Title = e.Title,
                    Date = FormatCairoTime(e.StartTime, "yyyy-MM-dd"),
                    Status = status,
                    Score = score,
                    MaxScore = (double)e.TotalScore,
                };
            }).ToList();

            // — Average score —
            var allPcts = new List<double>();
            allPcts.AddRange(submissions
                .Where(s => s.IsGraded && s.Score.HasValue && s.MaxScore > 0)
                .Select(s => (double)(s.Score!.Value / s.MaxScore) * 100));
            allPcts.AddRange(attempts
                .Where(a => a.IsGraded && a.Score.HasValue && a.TotalScore > 0)
                .Select(a => (double)(a.Score!.Value / a.TotalScore) * 100));
            var avgScore = allPcts.Count > 0 ? Math.Round(allPcts.Average(), 1) : 0;

            // — Attendance based on actual school days from term start to today —
            var allDays = await _unitOfWork.DailyAbsences
                .FindAsync(a => a.EnrollmentId == enrollment.Id && !a.IsDeleted);

            // Filter absences by term and count distinct absent dates
            var termDays = allDays
                .Where(a => a.AbsenceDate >= termStartDate && a.AbsenceDate <= termEndDate)
                .ToList();
            var absCount = termDays
                .Where(a => a.IsAbsent)
                .Select(a => a.AbsenceDate)
                .Distinct()
                .Count();

            // Count total school days (Sunday-Thursday) from term start to today
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var effectiveEnd = today < termEndDate ? today : termEndDate;
            var totalSchoolDays = 0;
            for (var d = termStartDate; d <= effectiveEnd; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Friday && d.DayOfWeek != DayOfWeek.Saturday)
                    totalSchoolDays++;
            }

            var attendancePct = totalSchoolDays == 0 ? 100
                : Math.Round((double)(totalSchoolDays - absCount) / totalSchoolDays * 100, 1);

            results.Add(new ChildProgressItemDto
            {
                StudentId = student.Id,
                StudentName = student.FullName,
                ClassName = className,
                GradeLevelName = gradeName,
                AvgScore = avgScore,
                AttendancePercentage = attendancePct,
                Assignments = assignmentDtos,
                Exams = examDtos,
            });
        }

        return OperationResult<List<ChildProgressItemDto>>.Success(results, "تم جلب بيانات متابعة الأبناء بنجاح");
    }

    /// <summary>
    /// Filters a DateTime? value to check if it falls within the given term date range.
    /// If the date is null, we include it (no date = always relevant).
    /// </summary>
    private static bool FilterByTerm(DateTime? date, DateOnly termStart, DateOnly termEnd)
    {
        if (!date.HasValue) return true;
        var d = DateOnly.FromDateTime(date.Value);
        return d >= termStart && d <= termEnd;
    }

    public async Task<OperationResult<ChildExamAttemptResultDto>> GetExamAttemptResultAsync(int parentUserId, int examId)
    {
        var currentYear = await _unitOfWork.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent && !y.IsDeleted);
        if (currentYear is null)
            return OperationResult<ChildExamAttemptResultDto>.Failure("لا توجد سنة دراسية حالية", 400);

        var links = await _unitOfWork.ParentStudents.GetWithStudentDetailsByParentAsync(parentUserId);
        var activeLinks = links.Where(l => !l.IsDeleted && l.Student is { IsDeleted: false }).ToList();

        if (activeLinks.Count == 0)
            return OperationResult<ChildExamAttemptResultDto>.Failure("لا يوجد أبناء مرتبطون", 404);

        // Find the student who has an attempt for this exam
        foreach (var link in activeLinks)
        {
            var student = link.Student!;
            var enrollment = student.Enrollments.FirstOrDefault(e => e.LeftAt == null && e.AcademicYearId == currentYear.Id && !e.IsDeleted);
            if (enrollment is null) continue;

            var attempt = await _unitOfWork.StudentExamAttempts.GetByEnrollmentAndExamAsync(enrollment.Id, examId);
            if (attempt is null) continue;

            // Already found the attempt - now get full details with answers
            var fullAttempt = await _unitOfWork.StudentExamAttempts.GetWithAnswersForEnrollmentAsync(attempt.Id, enrollment.Id);
            if (fullAttempt is null)
                return OperationResult<ChildExamAttemptResultDto>.Failure("لم يتم العثور على تفاصيل المحاولة", 404);

            var isResultPublished = fullAttempt.Exam.IsResultPublished;
            var subject = fullAttempt.Exam.Subject?.Name ?? fullAttempt.Exam.ClassSubjectTeacher?.Subject?.Name ?? "";

            string status;
            if (!fullAttempt.SubmittedAt.HasValue)
                status = "missed";
            else if (!fullAttempt.IsGraded)
                status = "pending";
            else if (!isResultPublished)
                status = "pending";
            else
                status = "done";

            var message = !fullAttempt.SubmittedAt.HasValue
                ? "لم يتم أداء الامتحان"
                : !fullAttempt.IsGraded
                    ? "تم التسليم، في انتظار التصحيح"
                    : !isResultPublished
                        ? "تم التصحيح، لكن لم يتم نشر النتيجة بعد"
                        : "تم إعلان النتيجة";

            var answers = isResultPublished && fullAttempt.IsGraded
                ? fullAttempt.Answers
                    .OrderBy(a => a.Question.DisplayOrder)
                    .Select(a =>
                    {
                        string? finalAnswerText = a.AnswerText;
                        string? correctAnswerText = null;

                        if (a.Question.QuestionType == QuestionType.MultipleChoice)
                        {
                            var selectedOpt = a.Question.Options.FirstOrDefault(o => o.Id == a.SelectedOptionId);
                            if (selectedOpt != null) finalAnswerText = selectedOpt.OptionText;
                            var correctOpt = a.Question.Options.FirstOrDefault(o => o.IsCorrect);
                            if (correctOpt != null) correctAnswerText = correctOpt.OptionText;
                        }
                        else if (a.Question.QuestionType == QuestionType.TrueFalse)
                        {
                            if (a.BooleanAnswer.HasValue)
                                finalAnswerText = a.BooleanAnswer.Value ? "صح" : "خطأ";

                            var normalizedCorrect = BooleanNormalizer.NormalizeBoolean(a.Question.CorrectAnswer);
                            if (normalizedCorrect.HasValue)
                                correctAnswerText = normalizedCorrect.Value ? "صح" : "خطأ";
                            else
                                correctAnswerText = a.Question.CorrectAnswer;
                        }
                        else if (a.Question.QuestionType == QuestionType.FillBlank || a.Question.QuestionType == QuestionType.Essay)
                        {
                            correctAnswerText = a.Question.CorrectAnswer;
                        }

                        return new ChildExamAnswerDto
                        {
                            QuestionId = a.QuestionId,
                            QuestionText = a.Question.QuestionText,
                            AnswerText = finalAnswerText,
                            IsCorrect = a.IsCorrect,
                            CorrectAnswerText = correctAnswerText,
                            PointsEarned = (double)a.PointsEarned,
                            QuestionPoints = (double)a.Question.Points,
                            AIFeedback = a.AIFeedback,
                        };
                    }).ToList()
                : new List<ChildExamAnswerDto>();

            return OperationResult<ChildExamAttemptResultDto>.Success(new ChildExamAttemptResultDto
            {
                ExamId = fullAttempt.ExamId,
                Subject = subject,
                Title = fullAttempt.Exam.Title,
                StudentId = student.Id,
                StudentName = student.FullName,
                Score = (isResultPublished && fullAttempt.Score.HasValue) ? (double)fullAttempt.Score.Value : null,
                MaxScore = (double)fullAttempt.TotalScore,
                Status = status,
                Message = message,
                Answers = answers,
            }, "تم جلب تفاصيل الامتحان بنجاح");
        }

        return OperationResult<ChildExamAttemptResultDto>.Failure("لم يعثر على محاولة لهذا الامتحان", 404);
    }
}
