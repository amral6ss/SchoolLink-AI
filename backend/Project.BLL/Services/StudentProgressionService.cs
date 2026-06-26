using Common.Results;
using Project.BLL.DTOs.Enrollments;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class StudentProgressionService : IStudentProgressionService
{
    private readonly IUnitOfWork _unitOfWork;

    public StudentProgressionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OperationResult<IEnumerable<StudentProgressionCandidateDto>>> GetCandidatesAsync(
        int gradeLevelId,
        int academicYearId,
        ProgressionTermScope termScope = ProgressionTermScope.BothSemesters,
        decimal passingThreshold = 50m,
        CancellationToken ct = default)
    {
        if (passingThreshold < 0m || passingThreshold > 100m)
            return OperationResult<IEnumerable<StudentProgressionCandidateDto>>.Failure("نسبة النجاح يجب أن تكون بين 0 و 100");

        var gradeLevel = await _unitOfWork.GradeLevels.GetByIdAsync(gradeLevelId, ct);
        if (gradeLevel is null || gradeLevel.IsDeleted)
            return OperationResult<IEnumerable<StudentProgressionCandidateDto>>.Failure("الصف الدراسي غير موجود");

        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(academicYearId, ct);
        if (academicYear is null || academicYear.IsDeleted)
            return OperationResult<IEnumerable<StudentProgressionCandidateDto>>.Failure("السنة الدراسية غير موجودة");

        var enrollments = await _unitOfWork.StudentEnrollments
            .GetActiveByGradeLevelAndYearWithDetailsAsync(gradeLevelId, academicYearId, ct);

        if (enrollments.Count == 0)
            return OperationResult<IEnumerable<StudentProgressionCandidateDto>>.Success(
                Array.Empty<StudentProgressionCandidateDto>(),
                "لا يوجد طلاب نشطون في الصف والسنة المحددين");

        var enrollmentIds = enrollments.Select(e => e.Id).ToList();

        // Pick the terms we care about based on the chosen scope.
        var termsToLoad = GetTermsForScope(termScope);

        var finalGrades = await _unitOfWork.FinalGrades.FindAsync(
            fg => !fg.IsDeleted
                  && enrollmentIds.Contains(fg.EnrollmentId)
                  && termsToLoad.Contains(fg.Term),
            ct);

        // Subject names are not loaded on FinalGrade by default — fetch them once.
        var subjectIds = finalGrades
            .Where(fg => fg.SubjectId.HasValue)
            .Select(fg => fg.SubjectId!.Value)
            .Distinct()
            .ToList();

        var subjects = subjectIds.Count != 0
            ? await _unitOfWork.Subjects.FindAsync(s => subjectIds.Contains(s.Id) && !s.IsDeleted, ct)
            : new List<Subject>();
        var subjectNamesById = subjects.ToDictionary(s => s.Id, s => s.Name);

        // FinalGrades are stored per (Enrollment + Subject + Term). Group by EnrollmentId.
        var finalGradesByEnrollmentId = finalGrades
            .GroupBy(fg => fg.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var candidates = enrollments
            .OrderBy(e => e.Student.FullName)
            .Select(enrollment => BuildCandidate(
                enrollment,
                academicYear,
                finalGradesByEnrollmentId,
                subjectNamesById,
                termsToLoad,
                passingThreshold))
            .ToList();

        return OperationResult<IEnumerable<StudentProgressionCandidateDto>>.Success(candidates);
    }

    private static List<AcademicTerm> GetTermsForScope(ProgressionTermScope scope)
        => scope switch
        {
            ProgressionTermScope.FirstSemester => new List<AcademicTerm> { AcademicTerm.FirstSemester },
            ProgressionTermScope.SecondSemester => new List<AcademicTerm> { AcademicTerm.SecondSemester },
            // BothSemesters (and any unexpected value) → consider the whole year.
            _ => new List<AcademicTerm> { AcademicTerm.FirstSemester, AcademicTerm.SecondSemester }
        };

    private static StudentProgressionCandidateDto BuildCandidate(
        StudentEnrollment enrollment,
        AcademicYear academicYear,
        IReadOnlyDictionary<int, List<FinalGrade>> finalGradesByEnrollmentId,
        IReadOnlyDictionary<int, string> subjectNamesById,
        IReadOnlyCollection<AcademicTerm> requiredTerms,
        decimal passingThreshold)
    {
        finalGradesByEnrollmentId.TryGetValue(enrollment.Id, out var grades);
        grades ??= new List<FinalGrade>();

        var hasAnyFinalGrade = grades.Count != 0;
        var hasPublishedFinalGrade = grades.Any(g => g.IsPublished);
        var hasIncompleteOrUnpublishedGrade = grades.Any(g => !g.IsPublished || !g.IsComplete || g.MaxTotal <= 0);

        // Aggregate per subject across the chosen term scope using score totals.
        var subjectGroups = grades
            .GroupBy(g => g.SubjectId)
            .ToList();

        var subjectGrades = new List<SubjectGradeDto>();
        var passed = 0;
        var failed = 0;

        foreach (var subjectGroup in subjectGroups)
        {
            var subjectId = subjectGroup.Key;
            var subjectGradeRows = subjectGroup.ToList();
            var termsPresent = subjectGradeRows.Select(g => g.Term).Distinct().ToHashSet();
            var hasAllRequiredTerms = requiredTerms.All(termsPresent.Contains);
            var isReadyForDecision = hasAllRequiredTerms &&
                subjectGradeRows.All(g => g.IsPublished && g.IsComplete && g.MaxTotal > 0);

            if (!isReadyForDecision)
                hasIncompleteOrUnpublishedGrade = true;

            var totalScore = subjectGradeRows
                .Where(g => g.MaxTotal > 0)
                .Sum(g => g.Total);
            var maxScore = subjectGradeRows
                .Where(g => g.MaxTotal > 0)
                .Sum(g => g.MaxTotal);

            var percentage = maxScore > 0 ? totalScore / maxScore * 100m : 0m;
            percentage = Math.Round(percentage, 1);
            var isPassed = isReadyForDecision && percentage >= passingThreshold;
            if (isReadyForDecision)
            {
                if (isPassed) passed++; else failed++;
            }

            // Representative term for display (first available within the scope).
            var representativeTerm = subjectGradeRows
                .Select(g => g.Term)
                .OrderBy(t => t)
                .FirstOrDefault();

            subjectGrades.Add(new SubjectGradeDto
            {
                SubjectId = subjectId,
                SubjectName = subjectId.HasValue && subjectNamesById.TryGetValue(subjectId.Value, out var name)
                    ? name
                    : "غير محدد",
                Percentage = percentage,
                IsPublished = isReadyForDecision,
                IsPassed = isPassed,
                Term = requiredTerms.Count == 1 ? MapTerm(representativeTerm) : null
            });
        }

        // Decide the overall academic status using the "must pass EVERY subject" rule.
        AcademicStatus status;
        if (!hasAnyFinalGrade)
            status = AcademicStatus.NoGrades;
        else if (!hasPublishedFinalGrade || hasIncompleteOrUnpublishedGrade)
            status = AcademicStatus.Unpublished;
        else if (failed == 0 && passed > 0)
            status = AcademicStatus.Passed;
        else
            status = AcademicStatus.Failed;

        // A single overall percentage to display (average of published subjects).
        var finalTotal = subjectGrades.Count != 0
            ? Math.Round(subjectGrades.Average(sg => sg.Percentage), 1)
            : (decimal?)null;

        return new StudentProgressionCandidateDto
        {
            EnrollmentId = enrollment.Id,
            StudentId = enrollment.StudentId,
            StudentName = enrollment.Student.FullName,
            CurrentClassId = enrollment.ClassId,
            CurrentClassName = enrollment.Class.Name,
            CurrentGradeLevelId = enrollment.Class.GradeLevelId,
            CurrentGradeLevelName = enrollment.Class.GradeLevel.Name,
            AcademicYearId = enrollment.AcademicYearId,
            AcademicYearName = academicYear.Name,
            StudentIsActive = enrollment.Student.IsActive,
            StudentLifecycleStatus = enrollment.Student.LifecycleStatus,
            StudentLifecycleStatusName = enrollment.Student.LifecycleStatus.ToString(),
            HasStudentAccount = enrollment.Student.UserId.HasValue,
            HasFinalGrade = hasAnyFinalGrade,
            HasPublishedFinalGrade = hasPublishedFinalGrade,
            FinalTotal = finalTotal,
            AcademicStatus = status,
            PassedSubjectsCount = passed,
            FailedSubjectsCount = failed,
            SubjectGrades = subjectGrades
                .OrderByDescending(sg => sg.Percentage)
                .ToList()
        };
    }

    private static AcademicTermLabel? MapTerm(AcademicTerm term)
        => term switch
        {
            AcademicTerm.FirstSemester => AcademicTermLabel.FirstSemester,
            AcademicTerm.SecondSemester => AcademicTermLabel.SecondSemester,
            _ => null
        };

    private async Task<AcademicStatus> CalculateAnnualAcademicStatusAsync(
        int enrollmentId,
        decimal passingThreshold,
        CancellationToken ct)
    {
        var requiredTerms = GetTermsForScope(ProgressionTermScope.BothSemesters);
        var grades = await _unitOfWork.FinalGrades.FindAsync(
            fg => !fg.IsDeleted &&
                  fg.EnrollmentId == enrollmentId &&
                  requiredTerms.Contains(fg.Term),
            ct);

        if (grades.Count == 0)
            return AcademicStatus.NoGrades;

        if (!grades.Any(g => g.IsPublished))
            return AcademicStatus.Unpublished;

        var subjectGroups = grades.GroupBy(g => g.SubjectId).ToList();
        var hasFailedSubject = false;

        foreach (var subjectGroup in subjectGroups)
        {
            var subjectGrades = subjectGroup.ToList();
            var termsPresent = subjectGrades.Select(g => g.Term).Distinct().ToHashSet();
            var hasAllRequiredTerms = requiredTerms.All(termsPresent.Contains);
            var isReadyForDecision = hasAllRequiredTerms &&
                subjectGrades.All(g => g.IsPublished && g.IsComplete && g.MaxTotal > 0);

            if (!isReadyForDecision)
                return AcademicStatus.Unpublished;

            var totalScore = subjectGrades.Sum(g => g.Total);
            var maxScore = subjectGrades.Sum(g => g.MaxTotal);
            var percentage = maxScore > 0 ? totalScore / maxScore * 100m : 0m;

            if (percentage < passingThreshold)
                hasFailedSubject = true;
        }

        return hasFailedSubject ? AcademicStatus.Failed : AcademicStatus.Passed;
    }

    private static string? ValidateTargetClass(
        SchoolClass? targetClass,
        int targetAcademicYearId,
        int expectedGradeLevelId)
    {
        if (targetClass is null || targetClass.IsDeleted)
            return "الفصل الهدف غير موجود";

        if (targetClass.AcademicYearId != targetAcademicYearId)
            return "الفصل الهدف لا ينتمي إلى السنة الدراسية الهدف";

        if (targetClass.GradeLevelId != expectedGradeLevelId)
            return "الفصل الهدف لا ينتمي إلى الصف الدراسي المطلوب";

        if (targetClass.Status != ClassStatus.Active)
            return "الفصل الهدف غير نشط";

        return null;
    }

    public async Task<OperationResult<StudentProgressionResultDto>> ExecuteAsync(
        StudentProgressionRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            return OperationResult<StudentProgressionResultDto>.Failure("بيانات الطلب غير صالحة");

        var enrollmentIds = request.EnrollmentIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (enrollmentIds.Count == 0)
            return OperationResult<StudentProgressionResultDto>.Failure("يجب اختيار طالب واحد على الأقل");

        if (!Enum.IsDefined(request.Action))
            return OperationResult<StudentProgressionResultDto>.Failure("نوع العملية غير صالح");

        if (request.PassingThreshold < 0m || request.PassingThreshold > 100m)
            return OperationResult<StudentProgressionResultDto>.Failure("نسبة النجاح يجب أن تكون بين 0 و 100");

        var selectedEnrollments = await _unitOfWork.StudentEnrollments.GetByIdsWithDetailsAsync(enrollmentIds, ct);
        if (selectedEnrollments.Count != enrollmentIds.Count)
            return OperationResult<StudentProgressionResultDto>.Failure("بعض القيود الدراسية المحددة غير موجودة");

        if (selectedEnrollments.Any(e => e.IsDeleted))
            return OperationResult<StudentProgressionResultDto>.Failure("الطلب يحتوي على قيد دراسي محذوف");

        if (selectedEnrollments.Any(e => e.LeftAt is not null))
            return OperationResult<StudentProgressionResultDto>.Failure("الطلب يحتوي على قيد دراسي مغلق بالفعل");

        if (selectedEnrollments.Any(e =>
                e.Student is null ||
                !e.Student.IsActive ||
                e.Student.LifecycleStatus != StudentLifecycleStatus.Active))
            return OperationResult<StudentProgressionResultDto>.Failure("لا يمكن تنفيذ الترقية إلا على طلاب نشطين أكاديميا");

        if (selectedEnrollments.Any(e => e.Class is null || e.Class.IsDeleted || e.Class.GradeLevel is null))
            return OperationResult<StudentProgressionResultDto>.Failure("بعض القيود الدراسية تفتقد بيانات الصف الحالي");

        if (selectedEnrollments.Any(e => e.AcademicYear is null || e.AcademicYear.IsDeleted))
            return OperationResult<StudentProgressionResultDto>.Failure("بعض القيود الدراسية تفتقد بيانات السنة الدراسية");

        if (selectedEnrollments.Any(e => request.EffectiveDate < e.EnrolledAt))
            return OperationResult<StudentProgressionResultDto>.Failure("تاريخ التنفيذ لا يمكن أن يسبق تاريخ القيد الحالي");

        var sourceGradeLevelIds = selectedEnrollments
            .Select(e => e.Class.GradeLevelId)
            .Distinct()
            .ToList();
        if (sourceGradeLevelIds.Count != 1)
            return OperationResult<StudentProgressionResultDto>.Failure("يجب أن تنتمي القيود المختارة إلى صف دراسي مصدر واحد فقط");

        var sourceAcademicYearIds = selectedEnrollments
            .Select(e => e.AcademicYearId)
            .Distinct()
            .ToList();
        if (sourceAcademicYearIds.Count != 1)
            return OperationResult<StudentProgressionResultDto>.Failure("يجب أن تنتمي القيود المختارة إلى سنة دراسية مصدر واحدة فقط");

        var sourceGradeLevel = selectedEnrollments[0].Class.GradeLevel;
        var sourceAcademicYear = selectedEnrollments[0].AcademicYear;
        var nextGradeLevel = await _unitOfWork.GradeLevels.GetByLevelOrderAsync(sourceGradeLevel.LevelOrder + 1, ct);
        if (nextGradeLevel?.IsDeleted == true)
            nextGradeLevel = null;

        AcademicYear? targetAcademicYear = null;
        SchoolClass? targetClass = null;
        var targetClassesBySourceClassId = new Dictionary<int, SchoolClass>();
        var hasClassMappings = request.ClassMappings.Any(m => m.SourceClassId > 0 || m.TargetClassId > 0);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        switch (request.Action)
        {
            case StudentProgressionActionType.Promote:
                if (nextGradeLevel is null)
                    return OperationResult<StudentProgressionResultDto>.Failure(
                        "لا يوجد صف تالٍ لهذا الصف. استخدم التخرج إذا كان هذا آخر صف في المدرسة");

                if (!request.TargetAcademicYearId.HasValue)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف مطلوبة للترقية");

                if (!request.TargetClassId.HasValue && !hasClassMappings)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف مطلوب للترقية");

                targetAcademicYear = await _unitOfWork.AcademicYears.GetByIdAsync(request.TargetAcademicYearId.Value, ct);
                if (targetAcademicYear is null || targetAcademicYear.IsDeleted)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف غير موجودة");

                if (targetAcademicYear.Id == sourceAcademicYear.Id)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف يجب أن تختلف عن السنة المصدر");

                if (targetAcademicYear.StartDate <= sourceAcademicYear.StartDate)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف يجب أن تبدأ بعد السنة المصدر");

                if (!hasClassMappings)
                {
                    targetClass = await _unitOfWork.Classes.GetByIdWithIncludesAsync(request.TargetClassId.GetValueOrDefault(), ct);
                if (targetClass is null || targetClass.IsDeleted)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف غير موجود");

                if (targetClass.AcademicYearId != targetAcademicYear.Id)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف لا ينتمي إلى السنة الدراسية الهدف");

                if (targetClass.GradeLevelId != nextGradeLevel.Id)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف يجب أن ينتمي إلى الصف التالي مباشرة");

                if (targetClass.Status != ClassStatus.Active)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف غير نشط");

                }

                if (request.EffectiveDate < targetAcademicYear.StartDate ||
                    request.EffectiveDate > targetAcademicYear.EndDate)
                    return OperationResult<StudentProgressionResultDto>.Failure("تاريخ التنفيذ يجب أن يقع داخل السنة الدراسية الهدف");
                break;

            case StudentProgressionActionType.Retain:
                if (!request.TargetAcademicYearId.HasValue)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف مطلوبة للإبقاء");

                if (!request.TargetClassId.HasValue && !hasClassMappings)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف مطلوب للإبقاء");

                targetAcademicYear = await _unitOfWork.AcademicYears.GetByIdAsync(request.TargetAcademicYearId.Value, ct);
                if (targetAcademicYear is null || targetAcademicYear.IsDeleted)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف غير موجودة");

                if (targetAcademicYear.Id == sourceAcademicYear.Id)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف يجب أن تختلف عن السنة المصدر");

                if (targetAcademicYear.StartDate <= sourceAcademicYear.StartDate)
                    return OperationResult<StudentProgressionResultDto>.Failure("السنة الدراسية الهدف يجب أن تبدأ بعد السنة المصدر");

                if (!hasClassMappings)
                {
                    targetClass = await _unitOfWork.Classes.GetByIdWithIncludesAsync(request.TargetClassId.GetValueOrDefault(), ct);
                if (targetClass is null || targetClass.IsDeleted)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف غير موجود");

                if (targetClass.AcademicYearId != targetAcademicYear.Id)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف لا ينتمي إلى السنة الدراسية الهدف");

                if (targetClass.GradeLevelId != sourceGradeLevel.Id)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف يجب أن ينتمي إلى نفس الصف الدراسي المصدر");

                if (targetClass.Status != ClassStatus.Active)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف غير نشط");

                }
                if (request.EffectiveDate < targetAcademicYear.StartDate ||
                    request.EffectiveDate > targetAcademicYear.EndDate)
                    return OperationResult<StudentProgressionResultDto>.Failure("تاريخ التنفيذ يجب أن يقع داخل السنة الدراسية الهدف");
                break;

            case StudentProgressionActionType.Graduate:
                if (nextGradeLevel is not null)
                    return OperationResult<StudentProgressionResultDto>.Failure(
                        "هذا الصف ليس الأخير في المدرسة، لذا لا يمكن تنفيذ التخرج عليه");

                if (request.TargetClassId.HasValue || request.TargetAcademicYearId.HasValue)
                    return OperationResult<StudentProgressionResultDto>.Failure(
                        "لا يجب إرسال سنة دراسية أو فصل هدف عند تنفيذ التخرج");
                break;
        }

        if (request.Action is StudentProgressionActionType.Promote or StudentProgressionActionType.Retain)
        {
            if (targetAcademicYear is null)
                return OperationResult<StudentProgressionResultDto>.Failure("بيانات الوجهة غير مكتملة");

            var expectedTargetGradeLevelId = request.Action == StudentProgressionActionType.Promote
                ? nextGradeLevel!.Id
                : sourceGradeLevel.Id;

            var selectedSourceClassIds = selectedEnrollments
                .Select(e => e.ClassId)
                .Distinct()
                .ToList();

            if (hasClassMappings)
            {
                var mappings = request.ClassMappings
                    .Where(m => m.SourceClassId > 0 && m.TargetClassId > 0)
                    .GroupBy(m => m.SourceClassId)
                    .ToDictionary(g => g.Key, g => g.First().TargetClassId);

                if (selectedSourceClassIds.Any(sourceClassId => !mappings.ContainsKey(sourceClassId)))
                    return OperationResult<StudentProgressionResultDto>.Failure("يجب تحديد فصل هدف لكل فصل مصدر في الطلاب المحددين");

                foreach (var sourceClassId in selectedSourceClassIds)
                {
                    var mappedTargetClass = await _unitOfWork.Classes.GetByIdWithIncludesAsync(mappings[sourceClassId], ct);
                    var validationError = ValidateTargetClass(mappedTargetClass, targetAcademicYear.Id, expectedTargetGradeLevelId);
                    if (validationError is not null)
                        return OperationResult<StudentProgressionResultDto>.Failure(validationError);

                    targetClassesBySourceClassId[sourceClassId] = mappedTargetClass!;
                }
            }
            else
            {
                if (targetClass is null)
                    return OperationResult<StudentProgressionResultDto>.Failure("الفصل الهدف غير موجود");

                foreach (var sourceClassId in selectedSourceClassIds)
                    targetClassesBySourceClassId[sourceClassId] = targetClass;
            }
        }

        var result = new StudentProgressionResultDto
        {
            TotalRequested = enrollmentIds.Count
        };

        var orderedSummaries = selectedEnrollments
            .OrderBy(e => e.Student.FullName)
            .ToList();

        foreach (var summary in orderedSummaries)
        {
            try
            {
                await _unitOfWork.BeginTransactionAsync(ct);

                var currentEnrollment = await _unitOfWork.StudentEnrollments.GetByIdWithDetailsAsync(summary.Id, ct);
                if (currentEnrollment is null || currentEnrollment.IsDeleted)
                {
                    await AddFailureAndRollbackAsync(
                        result,
                        summary.Id,
                        summary.StudentId,
                        summary.Student.FullName,
                        "القيد الدراسي لم يعد متاحًا",
                        ct);
                    continue;
                }

                if (currentEnrollment.LeftAt is not null)
                {
                    await AddFailureAndRollbackAsync(
                        result,
                        currentEnrollment.Id,
                        currentEnrollment.StudentId,
                        currentEnrollment.Student.FullName,
                        "القيد الدراسي مغلق بالفعل",
                        ct);
                    continue;
                }

                if (request.EffectiveDate < currentEnrollment.EnrolledAt)
                {
                    await AddFailureAndRollbackAsync(
                        result,
                        currentEnrollment.Id,
                        currentEnrollment.StudentId,
                        currentEnrollment.Student.FullName,
                        "تاريخ التنفيذ لا يمكن أن يسبق تاريخ القيد الحالي",
                        ct);
                    continue;
                }

                if (currentEnrollment.Class.GradeLevelId != sourceGradeLevel.Id ||
                    currentEnrollment.AcademicYearId != sourceAcademicYear.Id)
                {
                    await AddFailureAndRollbackAsync(
                        result,
                        currentEnrollment.Id,
                        currentEnrollment.StudentId,
                        currentEnrollment.Student.FullName,
                        "القيد الدراسي تغيّر أثناء التنفيذ ولم يعد مطابقًا للدفعة الحالية",
                        ct);
                    continue;
                }

                var academicStatus = await CalculateAnnualAcademicStatusAsync(
                    currentEnrollment.Id,
                    request.PassingThreshold,
                    ct);

                if (request.Action is StudentProgressionActionType.Promote or StudentProgressionActionType.Graduate &&
                    academicStatus != AcademicStatus.Passed)
                {
                    await AddFailureAndRollbackAsync(
                        result,
                        currentEnrollment.Id,
                        currentEnrollment.StudentId,
                        currentEnrollment.Student.FullName,
                        "لا يمكن ترقية أو تخريج الطالب قبل نجاحه في نتيجة السنة كاملة",
                        ct);
                    continue;
                }

                if (request.Action == StudentProgressionActionType.Retain &&
                    academicStatus != AcademicStatus.Failed)
                {
                    await AddFailureAndRollbackAsync(
                        result,
                        currentEnrollment.Id,
                        currentEnrollment.StudentId,
                        currentEnrollment.Student.FullName,
                        "لا يمكن إبقاء الطالب إلا إذا كان راسبا في نتيجة السنة كاملة",
                        ct);
                    continue;
                }

                SchoolClass? resolvedTargetClassForStudent = null;

                if (request.Action is StudentProgressionActionType.Promote or StudentProgressionActionType.Retain)
                {
                    if (targetAcademicYear is null ||
                        !targetClassesBySourceClassId.TryGetValue(currentEnrollment.ClassId, out var resolvedTargetClass))
                    {
                        await AddFailureAndRollbackAsync(
                            result,
                            currentEnrollment.Id,
                            currentEnrollment.StudentId,
                            currentEnrollment.Student.FullName,
                            "بيانات الوجهة غير مكتملة",
                            ct);
                        continue;
                    }

                    resolvedTargetClassForStudent = resolvedTargetClass;

                    var hasActiveEnrollment = await _unitOfWork.StudentEnrollments
                        .HasActiveEnrollmentAsync(currentEnrollment.StudentId, targetAcademicYear.Id, ct);

                    if (hasActiveEnrollment)
                    {
                        await AddFailureAndRollbackAsync(
                            result,
                            currentEnrollment.Id,
                            currentEnrollment.StudentId,
                            currentEnrollment.Student.FullName,
                            "الطالب لديه بالفعل قيد نشط في السنة الدراسية الهدف",
                            ct);
                        continue;
                    }

                    if (resolvedTargetClass.Capacity.HasValue)
                    {
                        var activeCount = await _unitOfWork.StudentEnrollments.GetActiveCountByClassAsync(
                            resolvedTargetClass.Id,
                            targetAcademicYear.Id,
                            ct);

                        if (activeCount >= resolvedTargetClass.Capacity.Value)
                        {
                            await AddFailureAndRollbackAsync(
                                result,
                                currentEnrollment.Id,
                                currentEnrollment.StudentId,
                                currentEnrollment.Student.FullName,
                                "الفصل الهدف وصل إلى السعة القصوى",
                                ct);
                            continue;
                        }
                    }
                }

                currentEnrollment.LeftAt = request.EffectiveDate;
                currentEnrollment.TransferReason = note;
                currentEnrollment.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.StudentEnrollments.Update(currentEnrollment);

                switch (request.Action)
                {
                    case StudentProgressionActionType.Promote:
                    case StudentProgressionActionType.Retain:
                        await _unitOfWork.StudentEnrollments.AddAsync(new StudentEnrollment
                        {
                            StudentId = currentEnrollment.StudentId,
                            ClassId = resolvedTargetClassForStudent!.Id,
                            AcademicYearId = targetAcademicYear!.Id,
                            EnrolledAt = request.EffectiveDate
                        }, ct);

                        result.SuccessCount++;
                        if (request.Action == StudentProgressionActionType.Promote)
                            result.PromotedCount++;
                        else
                            result.RetainedCount++;
                        break;

                    case StudentProgressionActionType.Graduate:
                        currentEnrollment.Student.LifecycleStatus = StudentLifecycleStatus.Graduated;
                        currentEnrollment.Student.UpdatedAt = DateTime.UtcNow;
                        _unitOfWork.Students.Update(currentEnrollment.Student);

                        result.SuccessCount++;
                        result.GraduatedCount++;
                        break;
                }

                await _unitOfWork.SaveChangesAsync(ct);
                await _unitOfWork.CommitTransactionAsync(ct);
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                AddFailure(
                    result,
                    summary.Id,
                    summary.StudentId,
                    summary.Student.FullName,
                    "حدث خطأ غير متوقع أثناء معالجة الطالب");
            }
        }

        result.FailureCount = result.Failures.Count;

        return OperationResult<StudentProgressionResultDto>.Success(
            result,
            BuildResultMessage(result));
    }

    private async Task AddFailureAndRollbackAsync(
        StudentProgressionResultDto result,
        int enrollmentId,
        int studentId,
        string studentName,
        string reason,
        CancellationToken ct)
    {
        await _unitOfWork.RollbackTransactionAsync(ct);
        AddFailure(result, enrollmentId, studentId, studentName, reason);
    }

    private static void AddFailure(
        StudentProgressionResultDto result,
        int enrollmentId,
        int studentId,
        string studentName,
        string reason)
    {
        result.Failures.Add(new StudentProgressionFailureDto
        {
            EnrollmentId = enrollmentId,
            StudentId = studentId,
            StudentName = studentName,
            Reason = reason
        });
    }

    private static string BuildResultMessage(StudentProgressionResultDto result)
    {
        if (result.SuccessCount == 0)
            return "لم تنجح أي عملية على الطلاب المحددين";

        if (result.FailureCount == 0)
            return $"تم تنفيذ العملية بنجاح على {result.SuccessCount} طالب";

        return $"تم تنفيذ العملية على {result.SuccessCount} طالب، وتعذر معالجة {result.FailureCount} طالب";
    }
}
