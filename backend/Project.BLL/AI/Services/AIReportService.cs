using Common.Results;
using Microsoft.Extensions.Logging;
using Project.BLL.AI.Interfaces;
using Project.BLL.DTOs.Reports;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.AI.Services;

public class AIReportService : IAIReportService
{
    private readonly ILLMRouter _router;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStudentEvaluationService _evalService;
    private readonly ILogger<AIReportService> _logger;

    private const string SystemPrompt =
        "أنت محلل أكاديمي خبير في كتابة تقارير تقييم الطلاب.\n\n" +
        "مهمتك: توليد تقرير أكاديمي احترافي باللغة العربية بناءً على بيانات الطالب.\n\n" +
        "التنسيق المطلوب (التزم به بدقة):\n" +
        "1. استخدم **عنوان القسم** (بين نجمتين مزدوجتين) لعناوين الأقسام الرئيسية.\n" +
        "2. استخدم - **مسمى:** (شرطة ونجمة مزدوجة ونقطتين) لتسمية الحقول.\n" +
        "3. استخدم | لجداول البيانات.\n" +
        "4. استخدم --- للفصل بين الأقسام.\n" +
        "5. اكتب الدرجات بصيغة 35.0 / 40.0 والنسب المئوية بصيغة 87.5%.";

    public AIReportService(
        ILLMRouter router,
        IUnitOfWork unitOfWork,
        IStudentEvaluationService evalService,
        ILogger<AIReportService> logger)
    {
        _router = router;
        _unitOfWork = unitOfWork;
        _evalService = evalService;
        _logger = logger;
    }

    /// <summary>
    /// Helper to compute subject grades from evaluations for a given enrollment and period.
    /// For monthly periods, aggregates across all weekly periods within that month.
    /// </summary>
    private async Task<List<SubjectGradeDto>> ComputeSubjectGradesAsync(
        StudentEnrollment? enrollment, int periodId, CancellationToken ct)
    {
        var subjectGrades = new List<SubjectGradeDto>();
        if (enrollment == null) return subjectGrades;

        var period = await _unitOfWork.EvaluationPeriods.GetByIdAsync(periodId);
        if (period == null) return subjectGrades;

        IReadOnlyList<StudentEvaluation> evaluations;

        // For monthly periods, aggregate evaluations from all weekly periods in that month
        if (period.PeriodType == PeriodType.Monthly && !string.IsNullOrWhiteSpace(period.MonthName))
        {
            var weeklyPeriods = await _unitOfWork.EvaluationPeriods
                .GetByMonthNameAsync(period.AcademicYearId, period.MonthName, ct);

            var weeklyIds = weeklyPeriods
                .Where(p => p.PeriodType == PeriodType.Weekly)
                .Select(p => p.Id)
                .ToList();

            if (weeklyIds.Count == 0)
                evaluations = await _unitOfWork.StudentEvaluations
                    .GetByEnrollmentAndPeriodAsync(enrollment.Id, periodId, ct);
            else
            {
                evaluations = await _unitOfWork.StudentEvaluations
                    .GetByPeriodsAndEnrollmentsAsync(weeklyIds, [enrollment.Id], ct);

                var weekCount = weeklyIds.Count;

                if (evaluations.Count > 0 && weekCount > 0)
                {
                    // Aggregate by subject — sum across weeks then divide to get average
                    var (groups, _, _) = await AggregateEvaluationsBySubjectAsync(evaluations, ct);

                    subjectGrades = groups.Select(sg => new SubjectGradeDto
                    {
                        SubjectName = sg.Name,
                        Score = Math.Round(sg.Score / weekCount, 1),
                        MaxScore = Math.Round(sg.Max / weekCount, 1)
                    }).ToList();

                    return subjectGrades;
                }
            }
        }
        else
        {
            evaluations = await _unitOfWork.StudentEvaluations
                .GetByEnrollmentAndPeriodAsync(enrollment.Id, periodId, ct);
        }

        if (evaluations.Count == 0) return subjectGrades;

        var (resultGroups, _, _) = await AggregateEvaluationsBySubjectAsync(evaluations, ct);

        subjectGrades = resultGroups.Select(sg => new SubjectGradeDto
        {
            SubjectName = sg.Name,
            Score = sg.Score,
            MaxScore = sg.Max
        }).ToList();

        return subjectGrades;
    }

    /// <summary>
    /// Aggregates a list of evaluations by subject, returning summed scores and max scores.
    /// </summary>
    private async Task<(List<(string Name, decimal Score, decimal Max)> Groups, Dictionary<int, string> SubjectDict, Dictionary<int, decimal> ItemMaxScores)> AggregateEvaluationsBySubjectAsync(
        IReadOnlyList<StudentEvaluation> evaluations, CancellationToken ct)
    {
        var itemIds = evaluations.Select(e => e.EvaluationItemId).Distinct().ToList();
        var items = (await _unitOfWork.EvaluationItems.FindAsync(i => itemIds.Contains(i.Id))).ToList();
        var templateIds = items.Select(i => i.TemplateId).Distinct().ToList();
        var templates = (await _unitOfWork.EvaluationTemplates.FindAsync(t => templateIds.Contains(t.Id))).ToList();
        var subjectIds = templates.Select(t => t.SubjectId).Distinct().ToList();
        var subjects = (await _unitOfWork.Subjects.FindAsync(s => subjectIds.Contains(s.Id))).ToList();

        var subjectDict = subjects.ToDictionary(s => s.Id, s => s.Name);
        var templateDict = templates.ToDictionary(t => t.Id, t => t.SubjectId);
        var itemMaxDict = items.ToDictionary(i => i.Id, i => (decimal)i.MaxScore);
        var itemTemplateDict = items.ToDictionary(i => i.Id, i => i.TemplateId);

        var groups = new Dictionary<int, (decimal Score, decimal Max, string Name)>();

        foreach (var eval in evaluations)
        {
            if (!itemTemplateDict.TryGetValue(eval.EvaluationItemId, out var tmplId)) continue;
            if (!templateDict.TryGetValue(tmplId, out var subjId)) continue;
            if (!subjectDict.TryGetValue(subjId, out var subjName)) continue;

            var score = eval.Score ?? 0;
            var maxScore = itemMaxDict.GetValueOrDefault(eval.EvaluationItemId, 0);

            if (!groups.ContainsKey(subjId))
                groups[subjId] = (0, 0, subjName);
            var cur = groups[subjId];
            groups[subjId] = (cur.Score + score, cur.Max + maxScore, subjName);
        }

        var result = groups.Select(g => (Name: g.Value.Name, Score: g.Value.Score, Max: g.Value.Max)).ToList();
        return (result, subjectDict, itemMaxDict);
    }

    /// <summary>
    /// Compute attendance rate for a given enrollment within a date range.
    /// Excludes weekends (Friday/Saturday) from the total school days count.
    /// </summary>
    private async Task<double> ComputeAttendanceRateAsync(int enrollmentId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var allAbsences = await _unitOfWork.DailyAbsences
            .FindAsync(a => a.EnrollmentId == enrollmentId && !a.IsDeleted);

        var periodAbsences = allAbsences
            .Where(a => a.AbsenceDate >= from && a.AbsenceDate <= to)
            .Count();

        var schoolDays = CountWeekdays(from, to);
        if (schoolDays <= 0) return 100;

        return Math.Max(0, 100 - periodAbsences / (double)schoolDays * 100);
    }

    /// <summary>
    /// Count weekdays (Sunday–Thursday) between two dates (inclusive).
    /// </summary>
    private static int CountWeekdays(DateOnly from, DateOnly to)
    {
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Friday && d.DayOfWeek != DayOfWeek.Saturday)
                count++;
        }
        return count;
    }

    public async Task<OperationResult<AIReport>> GenerateStudentReportAsync(int studentId, int periodId, AcademicTerm? term = null, CancellationToken ct = default)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(studentId);
        if (student == null || student.IsDeleted)
            return OperationResult<AIReport>.Failure("الطالب غير موجود", 404);

        var period = await _unitOfWork.EvaluationPeriods.GetByIdAsync(periodId);
        if (period == null || period.IsDeleted)
            return OperationResult<AIReport>.Failure("فترة التقييم غير موجودة", 404);

        var termInfo = term.HasValue ? $"\nالفصل الدراسي: {GetTermArabicName(term.Value)}" : "";
        var prompt = $"ولّد تقريراً أكاديمياً للطالب {student.FullName} عن فترة التقييم {period.Name}" +
                     $"\nتاريخ البداية: {period.StartDate}\nتاريخ النهاية: {period.EndDate}{termInfo}" +
                     $"\nملاحظة: السنة الحالية هي {DateTime.UtcNow.Year}، استخدمها في كتابة التاريخ أسفل التقرير.";

        var content = await _router.GenerateAsync(SystemPrompt, prompt, ct: ct);

        var report = new AIReport
        {
            StudentId = studentId,
            PeriodId = periodId,
            Term = term,
            ReportType = "Student",
            Content = content,
            IsPublished = false
        };

        await _unitOfWork.AIReports.AddAsync(report);
        await _unitOfWork.SaveChangesAsync(ct);

        return OperationResult<AIReport>.Success(report, "تم إنشاء التقرير بنجاح");
    }

    public async Task<OperationResult<AIReport>> GenerateClassReportAsync(int classId, int periodId, AcademicTerm? term = null, CancellationToken ct = default)
    {
        var period = await _unitOfWork.EvaluationPeriods.GetByIdAsync(periodId);
        var periodName = period?.Name ?? "الفترة الحالية";
        var termInfo = term.HasValue ? $" في الفصل الدراسي {GetTermArabicName(term.Value)}" : "";
        var prompt = $"ولّد تقريراً عن أداء الفصل في فترة التقييم {periodName}{termInfo}";
        var content = await _router.GenerateAsync(SystemPrompt, prompt, ct: ct);

        var report = new AIReport
        {
            ClassId = classId,
            PeriodId = periodId,
            Term = term,
            ReportType = "Class",
            Content = content,
            IsPublished = false
        };

        await _unitOfWork.AIReports.AddAsync(report);
        await _unitOfWork.SaveChangesAsync(ct);

        return OperationResult<AIReport>.Success(report, "تم إنشاء تقرير الفصل بنجاح");
    }

    public async Task<OperationResult<AIReport>> GenerateRecommendationsAsync(int studentId, AcademicTerm? term = null, CancellationToken ct = default)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(studentId);
        var studentName = student?.FullName ?? "الطالب";
        var termInfo = term.HasValue ? $" في الفصل الدراسي {GetTermArabicName(term.Value)}" : "";
        var prompt = $"قدّم توصيات أكاديمية لتحسين أداء الطالب {studentName}{termInfo}";
        var content = await _router.GenerateAsync(SystemPrompt, prompt, ct: ct);

        var report = new AIReport
        {
            StudentId = studentId,
            Term = term,
            ReportType = "Recommendations",
            Content = content,
            IsPublished = false
        };

        await _unitOfWork.AIReports.AddAsync(report);
        await _unitOfWork.SaveChangesAsync(ct);

        return OperationResult<AIReport>.Success(report, "تم إنشاء التوصيات بنجاح");
    }

    public async Task<OperationResult<IEnumerable<AIReport>>> GetStudentReportsAsync(int studentId, int? periodId = null)
    {
        var reports = await _unitOfWork.AIReports
            .FindAsync(r => r.StudentId == studentId && !r.IsDeleted);

        if (periodId.HasValue)
            reports = reports.Where(r => r.PeriodId == periodId).ToList();

        return OperationResult<IEnumerable<AIReport>>.Success(
            reports.OrderByDescending(r => r.CreatedAt));
    }

    public async Task<OperationResult<AIReport>> GetReportByIdAsync(int reportId, int userId, UserRole role)
    {
        var report = await _unitOfWork.AIReports.GetByIdAsync(reportId);
        if (report == null || report.IsDeleted)
            return OperationResult<AIReport>.Failure("التقرير غير موجود", 404);

        if (!role.IsAdminLike())
        {
            if (role == UserRole.Teacher && !report.ClassId.HasValue)
                return OperationResult<AIReport>.Failure("لا يمكنك الوصول إلى هذا التقرير");

            if (role == UserRole.Parent)
            {
                var parentLinks = await _unitOfWork.ParentStudents
                    .FindAsync(ps => ps.ParentId == userId && ps.StudentId == report.StudentId);
                if (!parentLinks.Any())
                    return OperationResult<AIReport>.Failure("لا يمكنك الوصول إلى هذا التقرير");
            }
            else if (role == UserRole.Student && report.StudentId != userId)
            {
                return OperationResult<AIReport>.Failure("لا يمكنك الوصول إلى هذا التقرير");
            }
        }

        return OperationResult<AIReport>.Success(report);
    }

    public async Task<OperationResult<StudentReportDto>> GetStructuredStudentReportAsync(
        int studentId, int periodId, AcademicTerm? term = null, CancellationToken ct = default)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(studentId);
        if (student == null || student.IsDeleted)
            return OperationResult<StudentReportDto>.Failure("الطالب غير موجود", 404);

        // If periodId is 0, resolve to the latest period for this student
        if (periodId == 0)
        {
            periodId = await ResolveLatestPeriodAsync(studentId);
            if (periodId == 0)
                return OperationResult<StudentReportDto>.Failure("لا توجد فترة تقييم متاحة للطالب", 404);
        }

        var period = await _unitOfWork.EvaluationPeriods.GetByIdAsync(periodId);
        if (period == null || period.IsDeleted)
            return OperationResult<StudentReportDto>.Failure("فترة التقييم غير موجودة", 404);

        var enrollment = (await _unitOfWork.StudentEnrollments
            .FindAsync(e => e.StudentId == studentId && e.LeftAt == null))
            .FirstOrDefault();

        // -- Overall score priority: use subject grades (evaluations) when available for consistency --
        double overallScore = 0;
        double overallMax = 100;
        double finalGradeAvg = 0;
        List<FinalGrade> allFinalGrades = new();

        if (enrollment != null && term.HasValue)
        {
            // Fetch ALL FinalGrades for this enrollment
            allFinalGrades = (await _unitOfWork.FinalGrades
                .FindAsync(fg => fg.EnrollmentId == enrollment.Id && fg.Term == term.Value && !fg.IsDeleted))
                .Where(fg => fg.Total > 0)
                .ToList();
        }

        // Subject-level grades via evaluations (current period)
        var subjectGrades = await ComputeSubjectGradesAsync(enrollment, periodId, ct);

        // Calculate overall from subject grades (evaluations) — consistent with what's displayed
        if (subjectGrades.Count > 0)
        {
            var sumScore = (double)subjectGrades.Sum(sg => sg.Score);
            var sumMax = (double)subjectGrades.Sum(sg => sg.MaxScore);
            if (sumMax > 0)
            {
                overallScore = Math.Round(sumScore / sumMax * 100, 1);
                overallMax = 100;
            }
        }

        // Also compute FinalGrade average (from 100 each)
        if (allFinalGrades.Count > 0)
        {
            finalGradeAvg = Math.Round(allFinalGrades.Average(fg => (double)fg.Total), 1);
        }

        // Trend detection
        string overallTrend = "stable";
        double overallChange = 0;

        var previousReports = await _unitOfWork.AIReports
            .FindAsync(r => r.StudentId == studentId && r.PeriodId == periodId && r.ReportType == "Student" && !r.IsDeleted);
        var previousReport = previousReports.OrderByDescending(r => r.CreatedAt).Skip(1).FirstOrDefault();

        if (previousReport?.Summary != null)
        {
            try
            {
                var prevData = System.Text.Json.JsonSerializer.Deserialize<StudentReportDto>(previousReport.Summary);
                if (prevData != null)
                {
                    overallChange = overallScore - prevData.OverallScore;
                    overallTrend = overallChange switch
                    {
                        > 2 => "up",
                        < -2 => "down",
                        _ => "stable"
                    };
                }
            }
            catch { /* skip */ }
        }

        // Metrics
        var metrics = new List<MetricDto>
        {
            new() { Label = "التقييمات", Value = overallScore, Max = overallMax }
        };

        if (enrollment != null && period.StartDate.HasValue && period.EndDate.HasValue)
        {
            try
            {
                var attendanceRate = await ComputeAttendanceRateAsync(enrollment.Id, period.StartDate.Value, period.EndDate.Value, ct);
                metrics.Add(new MetricDto { Label = "نسبة الحضور", Value = Math.Round(attendanceRate, 1), Max = 100 });
            }
            catch { /* skip */ }
        }

        // Metrics: only overall metrics — subjects are shown in their own card below

        // ── Previous month comparison ──
        PeriodComparisonDto? previousMonthData = null;
        if (enrollment != null)
        {
            try
            {
                var allEvals = await _unitOfWork.StudentEvaluations
                    .GetByEnrollmentIdAsync(enrollment.Id, ct);

                // Distinct periods sorted by OrderNum within the SAME semester
                var periodOrderMap = allEvals
                    .GroupBy(e => e.PeriodId)
                    .Select(g => new {
                        PeriodId = g.Key,
                        OrderNum = g.First().Period?.OrderNum ?? 0,
                        SemesterNumber = g.First().Period?.SemesterNumber,
                        PeriodType = g.First().Period?.PeriodType
                    })
                    .ToList();

                // Filter to same semester and same period type to avoid mixing
                // e.g. comparing a monthly period to a weekly period
                var sameSemester = period.SemesterNumber.HasValue
                    ? periodOrderMap.Where(p => p.SemesterNumber == period.SemesterNumber).ToList()
                    : periodOrderMap;

                // Only compare with periods of the same type (monthly vs monthly, etc.)
                if (period.PeriodType == PeriodType.Monthly)
                {
                    sameSemester = sameSemester.Where(p => p.PeriodType == PeriodType.Monthly).ToList();
                }

                sameSemester = sameSemester.OrderBy(p => p.OrderNum).ToList();

                var currentIdx = sameSemester.FindIndex(p => p.PeriodId == periodId);
                if (currentIdx > 0)
                {
                    var prevMonthId = sameSemester[currentIdx - 1].PeriodId;
                    var prevMonth = await _unitOfWork.EvaluationPeriods.GetByIdAsync(prevMonthId);

                    if (prevMonth is { IsDeleted: false })
                    {
                        var prevSubjectGrades = await ComputeSubjectGradesAsync(enrollment, prevMonthId, ct);

                        double prevOverall = 0;
                        if (prevSubjectGrades.Count > 0)
                        {
                            var prevSumScore = (double)prevSubjectGrades.Sum(sg => sg.Score);
                            var prevSumMax = (double)prevSubjectGrades.Sum(sg => sg.MaxScore);
                            if (prevSumMax > 0)
                                prevOverall = Math.Round(prevSumScore / prevSumMax * 100, 1);
                        }

                        // ── Previous month metrics (include attendance if dates available) ──
                        var prevMetrics = new List<MetricDto>
                        {
                            new() { Label = "التقييمات", Value = prevOverall, Max = 100 }
                        };
                        if (prevMonth.StartDate.HasValue && prevMonth.EndDate.HasValue)
                        {
                            try
                            {
                                var prevAttendance = await ComputeAttendanceRateAsync(
                                    enrollment.Id, prevMonth.StartDate.Value, prevMonth.EndDate.Value, ct);
                                prevMetrics.Add(new MetricDto
                                {
                                    Label = "نسبة الحضور",
                                    Value = Math.Round(prevAttendance, 1),
                                    Max = 100
                                });
                            }
                            catch { /* skip */ }
                        }

                        previousMonthData = new PeriodComparisonDto
                        {
                            PeriodId = prevMonthId,
                            PeriodName = prevMonth.Name,
                            OverallScore = prevOverall,
                            OverallMax = 100,
                            SubjectGrades = prevSubjectGrades,
                            Metrics = prevMetrics
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load previous month data for student {StudentId}", studentId);
            }
        }

        // Generate AI-enhanced report text with real scores
        var currentYear = DateTime.UtcNow.Year;
        // Determine the actual month name from the evaluation period's dates
        var periodMonthName = GetMonthNameFromPeriod(period);
        var periodDateLabel = $"{periodMonthName} {currentYear} م";
        var scoresSummary = string.Join("\n", subjectGrades.Select(sg => $"- {sg.SubjectName}: {sg.Score:F1}/{sg.MaxScore:F1}"));

        // Build rich comparison text for the prompt
        var comparisonText = "";
        if (previousMonthData != null)
        {
            var changeSign = overallChange > 0 ? "+" : "";
            comparisonText =
                $"\nبيانات الشهر السابق ({previousMonthData.PeriodName}):\n" +
                $"- **الدرجة الكلية:** {previousMonthData.OverallScore:F1}%\n" +
                $"- **المواد:\n";
            foreach (var sg in previousMonthData.SubjectGrades)
            {
                var pct = sg.MaxScore > 0 ? sg.Score / sg.MaxScore * 100 : 0;
                comparisonText += $"    {sg.SubjectName}: {sg.Score:F1} / {sg.MaxScore:F1} ({pct:F1}%)\n";
            }
            comparisonText += $"\nالتغيير الإجمالي: {changeSign}{overallChange:F1}%\n";
        }

        var promptBuilder = new System.Text.StringBuilder();
        promptBuilder.AppendLine("-- بيانات الطالب --");
        promptBuilder.AppendLine($"الاسم: {student.FullName}");
        promptBuilder.AppendLine($"فترة التقييم: {period.Name}");
        if (term.HasValue) promptBuilder.AppendLine($"الفصل الدراسي: {GetTermArabicName(term.Value)}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("-- الدرجات --");
        promptBuilder.AppendLine($"الدرجة الكلية: {overallScore:F1} من {overallMax:F1}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("المواد:");
        promptBuilder.AppendLine(scoresSummary);
        if (overallChange != 0)
            promptBuilder.AppendLine($"التغيير عن التقرير السابق: {overallChange:+#;-#;0}%");
        promptBuilder.Append(comparisonText);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("-- التعليمات --");
        promptBuilder.Append($"اكتب تقريراً أكاديمياً باللغة العربية للطالب {student.FullName} عن فترة {period.Name} ");
        promptBuilder.AppendLine("باتباع الهيكل التالي بالضبط:");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("**البيانات الأساسية**");
        promptBuilder.AppendLine("| العنصر | التفاصيل |");
        promptBuilder.AppendLine("|---|---|");
        promptBuilder.AppendLine($"| اسم الطالب | {student.FullName} |");
        promptBuilder.AppendLine($"| فترة التقييم | {period.Name} |");
        if (term.HasValue) promptBuilder.AppendLine($"| الفصل الدراسي | {GetTermArabicName(term.Value)} |");
        promptBuilder.AppendLine($"| الدرجة الكلية | {overallScore:F1}% |");
        if (previousMonthData != null)
            promptBuilder.AppendLine($"| درجة الشهر السابق ({previousMonthData.PeriodName}) | {previousMonthData.OverallScore:F1}% |");
        if (overallChange != 0)
            promptBuilder.AppendLine($"| التغير عن التقرير السابق | {overallChange:+#;-#;0}% |");
        promptBuilder.AppendLine($"| التاريخ | {periodDateLabel} |");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("---");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("**الأداء العام**");
        promptBuilder.AppendLine("اكتب فقرة موجزة تصف الأداء العام للطالب. اذكر الدرجة الكلية والتقييم العام (ممتاز / جيد جداً / جيد / مقبول / ضعيف).");
        promptBuilder.AppendLine("استخدم - **مسمى:** للتفاصيل. اذكر نقاط القوة والضعف بشكل عام.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("---");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("**تحليل المواد الدراسية**");
        promptBuilder.AppendLine("قسم المواد إلى:");
        promptBuilder.AppendLine("1. مواد التميز (أعلى من 90%): اذكرها مع تحليل سبب التميز.");
        promptBuilder.AppendLine("2. مواد جيدة جدا (80% - 90%): اذكرها مع ملاحظات.");
        promptBuilder.AppendLine("3. مواد تحتاج تحسين (أقل من 80%): اذكرها مع تحليل المشكلة.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("لكل مادة اكتب:");
        promptBuilder.AppendLine("- **اسم المادة:** الدرجة (مثلاً 35.0 / 40.0 - 87.5%) مع تحليل الأداء.");
        promptBuilder.AppendLine("- إذا توفرت مقارنة مع الشهر السابق، اذكر التغير (+ أو -).");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("---");
        promptBuilder.AppendLine();
        if (previousMonthData != null)
        {
            promptBuilder.AppendLine($"**مقارنة مع {previousMonthData.PeriodName}**");
            promptBuilder.AppendLine("حلل التغيرات بين الشهرين. اذكر المواد التي تحسنت والتي تراجعت. اذكر الأسباب المحتملة.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("---");
            promptBuilder.AppendLine();
        }
        promptBuilder.AppendLine("**التوصيات**");
        promptBuilder.AppendLine("قدم 3-5 توصيات محددة وقابلة للتنفيذ. اكتب كل توصية في سطر منفصل يبدأ بـ -.");
        promptBuilder.AppendLine("- ركز على المواد التي تحتاج تحسين.");
        promptBuilder.AppendLine("- قدم نصائح عملية (مثل: حل تمارين إضافية، مراجعة مع المعلم، تنظيم وقت المذاكرة).");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("---");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("**خلاصة**");
        promptBuilder.AppendLine("فقرة ختامية تلخص أداء الطالب وتوجهه للمستقبل.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("---");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"*التاريخ: {periodDateLabel}*");
        promptBuilder.AppendLine("*المحلل الأكاديمي*");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("ملاحظات مهمة:");
        promptBuilder.AppendLine("- استخدم | لكتابة الجداول.");
        promptBuilder.AppendLine("- استخدم **نص** للعناوين الرئيسية.");
        promptBuilder.AppendLine("- استخدم - **مسمى:** للحقول المسمّاة.");
        promptBuilder.AppendLine("- استخدم --- للفصل بين الأقسام.");
        promptBuilder.AppendLine("- اكتب النسب المئوية بصيغة 87.5%.");
        promptBuilder.AppendLine("- اكتب الكسور بصيغة 35.0 / 40.0.");
        promptBuilder.AppendLine("- لا تستخدم رموزا تعبيرية في التقرير.");

        var aiPrompt = promptBuilder.ToString();

        // Try AI generation; fallback to template-based report if AI is unavailable
        string aiContent;
        try
        {
            aiContent = await _router.GenerateAsync(
                SystemPrompt,
                aiPrompt, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI generation failed for student {StudentId}, using template fallback", studentId);
            aiContent = GenerateFallbackReportText(student.FullName, period.Name, overallScore, overallMax,
                finalGradeAvg, subjectGrades, overallTrend, overallChange, term);
        }

        if (string.IsNullOrWhiteSpace(aiContent))
        {
            aiContent = GenerateFallbackReportText(student.FullName, period.Name, overallScore, overallMax,
                finalGradeAvg, subjectGrades, overallTrend, overallChange, term);
        }

        // Build DTO
        var reportDto = new StudentReportDto
        {
            StudentId = studentId,
            StudentName = student.FullName,
            PeriodId = periodId,
            PeriodName = period.Name,
            Term = term,
            OverallScore = overallScore,
            OverallMax = overallMax,
            OverallTrend = overallTrend,
            OverallChange = overallChange,
            FinalGradeAverage = finalGradeAvg,
            FinalGradeMax = 100,
            SubjectGrades = subjectGrades,
            Metrics = metrics,
            ReportText = aiContent,
            PreviousMonth = previousMonthData
        };

        // Save to AIReports table for history
        var summaryJson = System.Text.Json.JsonSerializer.Serialize(reportDto);
        var report = new AIReport
        {
            StudentId = studentId,
            PeriodId = periodId,
            Term = term,
            ReportType = "Student",
            Content = aiContent,
            Summary = summaryJson,
            IsPublished = true
        };

        await _unitOfWork.AIReports.AddAsync(report);
        await _unitOfWork.SaveChangesAsync(ct);

        return OperationResult<StudentReportDto>.Success(reportDto, "تم إنشاء التقرير بنجاح");
    }

    public async Task<OperationResult<RecommendationsDto>> GetStructuredRecommendationsAsync(
        int studentId, AcademicTerm? term = null, CancellationToken ct = default)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(studentId);
        if (student == null || student.IsDeleted)
            return OperationResult<RecommendationsDto>.Failure("الطالب غير موجود", 404);

        var termInfo = term.HasValue ? $" في الفصل الدراسي {GetTermArabicName(term.Value)}" : "";
        var enrollment = (await _unitOfWork.StudentEnrollments
            .FindAsync(e => e.StudentId == studentId && e.LeftAt == null))
            .FirstOrDefault();

        var weakAreas = new List<string>();
        if (enrollment != null)
        {
            var evaluations = await _unitOfWork.StudentEvaluations
                .GetByEnrollmentIdAsync(enrollment.Id, ct);

            if (evaluations.Count > 0)
            {
                var itemIds = evaluations.Select(e => e.EvaluationItemId).Distinct().ToList();
                var items = (await _unitOfWork.EvaluationItems.FindAsync(i => itemIds.Contains(i.Id))).ToList();
                var templateIds = items.Select(i => i.TemplateId).Distinct().ToList();
                var templates = (await _unitOfWork.EvaluationTemplates.FindAsync(t => templateIds.Contains(t.Id))).ToList();
                var subjectIds = templates.Select(t => t.SubjectId).Distinct().ToList();
                var subjects = (await _unitOfWork.Subjects.FindAsync(s => subjectIds.Contains(s.Id))).ToList();

                var subjectDict = subjects.ToDictionary(s => s.Id, s => s.Name);
                var templateDict = templates.ToDictionary(t => t.Id, t => t.SubjectId);
                var itemDict = items.ToDictionary(i => i.Id, i => new { i.TemplateId, i.MaxScore, i.Name });

                var subjectScores = new Dictionary<int, (decimal Score, decimal Max, string Name)>();
                foreach (var eval in evaluations)
                {
                    if (!itemDict.TryGetValue(eval.EvaluationItemId, out var itemInfo)) continue;
                    if (!templateDict.TryGetValue(itemInfo.TemplateId, out var subjId)) continue;
                    if (!subjectDict.TryGetValue(subjId, out var subjName)) continue;

                    var score = eval.Score ?? 0;
                    if (!subjectScores.ContainsKey(subjId))
                        subjectScores[subjId] = (0, 0, subjName);
                    var cur = subjectScores[subjId];
                    subjectScores[subjId] = (cur.Score + score, cur.Max + itemInfo.MaxScore, subjName);
                }

                weakAreas = subjectScores
                    .Where(s => s.Value.Max > 0 && (double)(s.Value.Score / s.Value.Max) < 0.6)
                    .Select(s => s.Value.Name)
                    .ToList();
            }
        }

        var currentYear = DateTime.UtcNow.Year;
        var scoresHint = weakAreas.Count > 0
            ? $" الطالب يحتاج تحسيناً في المواد التالية: {string.Join("، ", weakAreas)}."
            : "";

        var prompt = $"قدّم توصيات أكاديمية مخصصة لتحسين أداء الطالب {student.FullName}{termInfo}.{scoresHint}\n\n" +
                     "قسّم التوصيات إلى أقسام واضحة. كل قسم يبدأ بعنوان بين علامتي ** ** على سطر منفصل.\n" +
                     "تحت كل عنوان، اكتب التوصيات كقائمة نقطية تبدأ كل منها بـ '- '.\n" +
                     "مثال:\n" +
                     "**تحسين المواد الدراسية**\n" +
                     "- توصية 1\n" +
                     "- توصية 2\n\n" +
                     "**تنمية المهارات**\n" +
                     "- توصية 3\n" +
                     "- توصية 4\n\n" +
                     "يجب أن تكون التوصيات محددة وقابلة للتطبيق." +
                     $"\nملاحظة: السنة الحالية هي {currentYear}.";

        string content;
        try
        {
            content = await _router.GenerateAsync(
                "أنت مستشار أكاديمي خبير في تقديم توصيات تعليمية مخصصة باللغة العربية.",
                prompt, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI recommendations generation failed for student {StudentId}, using template fallback", studentId);
            content = GenerateFallbackRecommendations(student.FullName, weakAreas, term);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = GenerateFallbackRecommendations(student.FullName, weakAreas, term);
        }

        // Parse sections: **Title** blocks followed by - items
        var sections = new List<RecommendationSection>();
        var lines = content.Split('\n', StringSplitOptions.None);

        RecommendationSection? currentSection = null;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            // Section header: ** something **
            if (line.StartsWith("**") && line.EndsWith("**"))
            {
                currentSection = new RecommendationSection
                {
                    Title = line.Trim('*', ' ').Trim()
                };
                sections.Add(currentSection);
                continue;
            }

            // Also check for bold markdown **text** where text may have more content after **
            if (line.StartsWith("**") && line.Contains("**", StringComparison.Ordinal) && line.LastIndexOf("**") > 2)
            {
                var endIdx = line.LastIndexOf("**");
                var title = line[2..endIdx].Trim();
                if (title.Length > 0)
                {
                    currentSection = new RecommendationSection { Title = title };
                    sections.Add(currentSection);
                    // remaining text after the ** could be content
                    var rest = line[(endIdx + 2)..].Trim();
                    if (rest.Length > 0 && !rest.StartsWith('-') && !rest.StartsWith('•'))
                    {
                        currentSection.Items.Add(rest);
                    }
                    continue;
                }
            }

            // Bullet item
            if (line.StartsWith('-') || line.StartsWith('•') || line.StartsWith('*'))
            {
                var item = line.TrimStart('-', '•', '*', ' ').Trim();
                if (item.Length > 0)
                {
                    if (currentSection != null)
                        currentSection.Items.Add(item);
                    else
                    {
                        // Items before any section header → create a default section
                        currentSection = new RecommendationSection { Title = "توصيات عامة" };
                        sections.Add(currentSection);
                        currentSection.Items.Add(item);
                    }
                }
                continue;
            }

            // Plain text line → append to current section's last item or skip
            if (currentSection != null && currentSection.Items.Count > 0)
            {
                currentSection.Items[^1] += " " + line;
            }
        }

        // Fallback: flat list from old parsing
        var recItems = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.TrimStart().StartsWith('-') || l.TrimStart().StartsWith('•') || l.TrimStart().StartsWith('*'))
            .Select(l => l.TrimStart('-', '•', '*', ' ').Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (recItems.Count == 0)
            recItems.Add(content.Trim());

        var report = new AIReport
        {
            StudentId = studentId,
            Term = term,
            ReportType = "Recommendations",
            Content = content,
            IsPublished = true
        };

        await _unitOfWork.AIReports.AddAsync(report);
        await _unitOfWork.SaveChangesAsync(ct);

        var dto = new RecommendationsDto
        {
            StudentId = studentId,
            RecommendationsText = content,
            RecommendationItems = recItems,
            Sections = sections
        };

        return OperationResult<RecommendationsDto>.Success(dto, "تم إنشاء التوصيات بنجاح");
    }

    public async Task<OperationResult<object>> DeleteReportAsync(int reportId, int userId, UserRole role)
    {
        var report = await _unitOfWork.AIReports.GetByIdAsync(reportId);
        if (report == null || report.IsDeleted)
            return OperationResult<object>.Failure("التقرير غير موجود", 404);

        // Role-based access
        if (!role.IsAdminLike())
        {
            if (role == UserRole.Teacher && !report.ClassId.HasValue)
                return OperationResult<object>.Failure("لا يمكنك حذف هذا التقرير");

            if (role == UserRole.Parent)
            {
                var parentLinks = await _unitOfWork.ParentStudents
                    .FindAsync(ps => ps.ParentId == userId && ps.StudentId == report.StudentId);
                if (!parentLinks.Any())
                    return OperationResult<object>.Failure("لا يمكنك حذف هذا التقرير");
            }
            else if (role == UserRole.Student && report.StudentId != userId)
            {
                return OperationResult<object>.Failure("لا يمكنك حذف هذا التقرير");
            }
        }

        _unitOfWork.AIReports.SoftDelete(report);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult<object>.Success(new { }, "تم حذف التقرير بنجاح");
    }

    /// <summary>
    /// Translates AcademicTerm enum to Arabic.
    /// </summary>
    private static string GetTermArabicName(AcademicTerm term) => term switch
    {
        AcademicTerm.FirstSemester => "الفصل الدراسي الأول",
        AcademicTerm.SecondSemester => "الفصل الدراسي الثاني",
        AcademicTerm.Final => "الفصل الدراسي الثالث",
        _ => $"الفصل {term}"
    };

    private static readonly string[] ArabicMonthNames =
        ["يناير", "فبراير", "مارس", "إبريل", "مايو", "يونيو",
         "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    /// <summary>
    /// Extracts the Arabic month name from an EvaluationPeriod.
    /// Falls back to period.MonthName, then EndDate, then StartDate, then "يناير".
    /// </summary>
    private static string GetMonthNameFromPeriod(EvaluationPeriod period)
    {
        if (!string.IsNullOrWhiteSpace(period.MonthName))
            return period.MonthName;

        var date = period.EndDate ?? period.StartDate;
        if (date.HasValue)
        {
            var idx = date.Value.Month - 1;
            if (idx >= 0 && idx < ArabicMonthNames.Length)
                return ArabicMonthNames[idx];
        }

        return "يناير";
    }

    /// <summary>
    /// Generates a template-based report text when AI generation is unavailable.
    /// </summary>
    private static string GenerateFallbackReportText(
        string studentName, string periodName,
        double overallScore, double overallMax,
        double finalGradeAvg, List<SubjectGradeDto> subjectGrades,
        string overallTrend, double overallChange,
        AcademicTerm? term)
    {
        var lines = new List<string>();

        var overallStatus = overallScore >= 90 ? "ممتاز" : overallScore >= 80 ? "جيد جداً" : overallScore >= 70 ? "جيد" : overallScore >= 50 ? "مقبول" : "ضعيف";

        lines.Add("**البيانات الأساسية**");
        lines.Add("| العنصر | التفاصيل |");
        lines.Add("|---|---|");
        lines.Add($"| اسم الطالب | {studentName} |");
        lines.Add($"| فترة التقييم | {periodName} |");
        if (term.HasValue)
            lines.Add($"| الفصل الدراسي | {GetTermArabicName(term.Value)} |");
        lines.Add($"| الدرجة الكلية | {overallScore:F1}% |");
        lines.Add($"| التقييم العام | {overallStatus} |");
        if (overallChange != 0)
        {
            var changeWord = overallChange > 0 ? "تحسن" : "تراجع";
            lines.Add($"| التغير عن التقرير السابق | {overallChange:+#;-#;0}% ({changeWord}) |");
        }
        lines.Add($"| التاريخ | {DateTime.Now:yyyy/MM/dd} |");

        lines.Add(string.Empty);
        lines.Add("---");
        lines.Add(string.Empty);

        lines.Add("**الأداء العام**");
        lines.Add($"- **التقييم العام:** {overallStatus}");
        lines.Add($"- **الدرجة الكلية:** {overallScore:F1}%");
        if (overallChange > 0)
            lines.Add($"- **الاتجاه:** تحسن بنسبة {overallChange:F1}% عن التقرير السابق.");
        else if (overallChange < 0)
            lines.Add($"- **الاتجاه:** تراجع بنسبة {Math.Abs(overallChange):F1}% عن التقرير السابق.");
        else if (overallChange != 0)
            lines.Add($"- **الاتجاه:** مستقر مع تغير طفيف.");
        else
            lines.Add($"- **الاتجاه:** مستقر.");
        lines.Add($"حصل الطالب على نسبة {overallScore:F1}% في التقييمات العامة، وهو مستوى {overallStatus}.");

        if (finalGradeAvg > 0)
        {
            lines.Add($"- **الدرجات النهائية:** {finalGradeAvg:F1}%.");
        }

        lines.Add(string.Empty);
        lines.Add("---");
        lines.Add(string.Empty);

        lines.Add("**تحليل المواد الدراسية**");

        var sortedGrades = subjectGrades.OrderByDescending(s => s.Percentage).ToList();
        var excellent = sortedGrades.Where(s => s.Percentage >= 90).ToList();
        var good = sortedGrades.Where(s => s.Percentage >= 80 && s.Percentage < 90).ToList();
        var needsWork = sortedGrades.Where(s => s.Percentage < 80).ToList();

        if (excellent.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("مواد التميز:");
            foreach (var sg in excellent)
                lines.Add($"- **{sg.SubjectName}:** {sg.Score:F1} / {sg.MaxScore:F1} ({sg.Percentage:F1}%) - أداء متميز.");
        }

        if (good.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("مواد جيدة جداً:");
            foreach (var sg in good)
                lines.Add($"- **{sg.SubjectName}:** {sg.Score:F1} / {sg.MaxScore:F1} ({sg.Percentage:F1}%) - أداء جيد جداً.");
        }

        if (needsWork.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("مواد تحتاج تحسين:");
            foreach (var sg in needsWork)
                lines.Add($"- **{sg.SubjectName}:** {sg.Score:F1} / {sg.MaxScore:F1} ({sg.Percentage:F1}%) - يحتاج إلى مزيد من الاهتمام.");
        }

        lines.Add(string.Empty);
        lines.Add("---");
        lines.Add(string.Empty);

        lines.Add("**التوصيات**");
        if (needsWork.Count > 0)
        {
            foreach (var sg in needsWork)
                lines.Add($"- التركيز على مادة {sg.SubjectName} من خلال حل تمارين إضافية ومراجعة الدروس بانتظام.");
        }
        if (excellent.Count > 0)
        {
            lines.Add($"- الاستمرار في التفوق في المواد المتميزة ({string.Join("، ", excellent.Select(s => s.SubjectName))}) مع تحديات إضافية.");
        }
        lines.Add("- تنظيم وقت المذاكرة اليومي وتخصيص مراجعة أسبوعية.");
        if (finalGradeAvg > 0)
            lines.Add($"- متابعة الاستعداد لامتحانات نهاية الفصل (متوسط الدرجات النهائية: {finalGradeAvg:F1}%).");

        lines.Add(string.Empty);
        lines.Add("---");
        lines.Add(string.Empty);

        lines.Add("**خلاصة**");
        lines.Add($"المستوى العام للطالب {overallStatus} بنسبة {overallScore:F1}%. " +
                   (needsWork.Count > 0
                       ? $"يحتاج للتركيز على تحسين المواد التالية: {string.Join("، ", needsWork.Select(s => s.SubjectName))}."
                       : "أداء ممتاز في جميع المواد. نوصي بالاستمرار على هذا المستوى."));
        lines.Add(string.Empty);
        lines.Add($"*تم إنشاء هذا التقرير تلقائياً في {DateTime.Now:yyyy/MM/dd HH:mm}*");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Generates template-based recommendations when AI generation is unavailable.
    /// </summary>
    private static string GenerateFallbackRecommendations(string studentName, List<string> weakAreas, AcademicTerm? term)
    {
        var sections = new List<string>();
        var termInfo = term.HasValue ? $" في الفصل الدراسي {GetTermArabicName(term.Value)}" : "";
        
        sections.Add($"**توصيات لتحسين أداء الطالب {studentName}{termInfo}**");
        sections.Add(string.Empty);

        sections.Add("**تحسين المواد الدراسية**");
        if (weakAreas.Count > 0)
        {
            sections.Add($"- يحتاج الطالب إلى التركيز على المواد التالية: {string.Join("، ", weakAreas)}.");
            foreach (var weak in weakAreas)
            {
                sections.Add($"- يُنصح بمراجعة دروس {weak} بشكل يومي وحل التمارين الإضافية.");
            }
        }
        else
        {
            sections.Add("- الأداء العام جيد. يُنصح بالاستمرار على نفس المستوى.");
        }
        sections.Add(string.Empty);

        sections.Add("**تنظيم الوقت والدراسة**");
        sections.Add("- وضع جدول دراسة يومي منتظم مع تخصيص وقت لكل مادة.");
        sections.Add("- تخصيص وقت كافٍ للمراجعة قبل الامتحانات.");
        sections.Add("- أخذ فترات راحة قصيرة بين جلسات الدراسة لتحسين التركيز.");
        sections.Add(string.Empty);

        sections.Add("**التفاعل مع المعلمين**");
        sections.Add("- تشجيع الطالب على المشاركة في الحصص الدراسية وطرح الأسئلة.");
        sections.Add("- التواصل مع معلمي المواد التي يحتاج فيها الطالب دعماً إضافياً.");
        sections.Add(string.Empty);

        sections.Add("**متابعة أولياء الأمور**");
        sections.Add("- متابعة أداء الطالب بشكل أسبوعي من خلال التقارير المتاحة.");
        sections.Add("- توفير بيئة مناسبة للدراسة في المنزل.");
        sections.Add("- تحفيز الطالب وتقديم الدعم المعنوي المستمر.");
        sections.Add(string.Empty);

        sections.Add($"*تم إنشاء هذه التوصيات تلقائياً في {DateTime.Now:yyyy/MM/dd HH:mm}*");

        return string.Join("\n", sections);
    }

    /// <summary>
    /// Finds the current active evaluation period for the student based on today's date.
    /// Falls back to the most recent completed period, then to the highest period ID.
    /// </summary>
    private async Task<int> ResolveLatestPeriodAsync(int studentId)
    {
        var enrollment = (await _unitOfWork.StudentEnrollments
            .FindAsync(e => e.StudentId == studentId && e.LeftAt == null))
            .FirstOrDefault();
        if (enrollment == null) return 0;

        // Get all periods with evaluations for this student
        var evaluations = await _unitOfWork.StudentEvaluations
            .GetByEnrollmentIdAsync(enrollment.Id, default);
        var periodIds = evaluations.Select(e => e.PeriodId).Distinct().ToList();
        if (periodIds.Count == 0) return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Try to find a period whose date range includes today (active period)
        foreach (var pid in periodIds.OrderByDescending(p => p))
        {
            var period = await _unitOfWork.EvaluationPeriods.GetByIdAsync(pid);
            if (period?.StartDate <= today && period?.EndDate >= today)
                return pid;
        }

        // 2. No active period found — pick the most recent completed period (EndDate <= today)
        foreach (var pid in periodIds.OrderByDescending(p => p))
        {
            var period = await _unitOfWork.EvaluationPeriods.GetByIdAsync(pid);
            if (period?.EndDate <= today)
                return pid;
        }

        // 3. Final fallback: highest period ID
        return periodIds.Max();
    }
}
