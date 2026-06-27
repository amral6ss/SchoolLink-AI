using Microsoft.EntityFrameworkCore;
using Common.Results;
using Project.BLL.DTOs.HistoricalData;
using Project.BLL.Interfaces;
using Project.DAL.Context;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class HistoricalDataService : IHistoricalDataService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDbContext _context;

    public HistoricalDataService(IUnitOfWork unitOfWork, AppDbContext context)
    {
        _unitOfWork = unitOfWork;
        _context = context;
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalYearDto>>> GetAccessibleYearsAsync(int userId, UserRole role)
    {
        if (role == UserRole.Admin)
        {
            var years = await _unitOfWork.AcademicYears.GetAllOrderedByStartDateAsync();
            var dtos = years.Select(y => new HistoricalYearDto
            {
                Id = y.Id, Name = y.Name, StartDate = y.StartDate,
                EndDate = y.EndDate, IsCurrent = y.IsCurrent
            }).ToList();
            return OperationResult<IReadOnlyList<HistoricalYearDto>>.Success(dtos);
        }

        if (role == UserRole.Teacher)
        {
            var yearIds = await _context.ClassSubjectTeachers
                .Where(cst => cst.TeacherId == userId && !cst.IsDeleted)
                .Select(cst => cst.AcademicYearId)
                .Distinct()
                .ToListAsync();

            var years = await _unitOfWork.AcademicYears
                .FindAsync(y => yearIds.Contains(y.Id) && !y.IsDeleted);

            var dtos = years.OrderByDescending(y => y.StartDate)
                .Select(y => new HistoricalYearDto
                {
                    Id = y.Id, Name = y.Name, StartDate = y.StartDate,
                    EndDate = y.EndDate, IsCurrent = y.IsCurrent
                }).ToList();
            return OperationResult<IReadOnlyList<HistoricalYearDto>>.Success(dtos);
        }

        if (role == UserRole.Student)
        {
            var yearIds = await _context.StudentEnrollments
                .Where(se => se.Student.UserId == userId && !se.IsDeleted)
                .Select(se => se.AcademicYearId)
                .Distinct()
                .ToListAsync();

            var years = await _unitOfWork.AcademicYears
                .FindAsync(y => yearIds.Contains(y.Id) && !y.IsDeleted);

            var dtos = years.OrderByDescending(y => y.StartDate)
                .Select(y => new HistoricalYearDto
                {
                    Id = y.Id, Name = y.Name, StartDate = y.StartDate,
                    EndDate = y.EndDate, IsCurrent = y.IsCurrent
                }).ToList();
            return OperationResult<IReadOnlyList<HistoricalYearDto>>.Success(dtos);
        }

        if (role == UserRole.Parent)
        {
            var studentIds = await _context.ParentStudents
                .Where(ps => ps.ParentId == userId && !ps.IsDeleted)
                .Select(ps => ps.StudentId)
                .ToListAsync();

            var yearIds = await _context.StudentEnrollments
                .Where(se => studentIds.Contains(se.StudentId) && !se.IsDeleted)
                .Select(se => se.AcademicYearId)
                .Distinct()
                .ToListAsync();

            var years = await _unitOfWork.AcademicYears
                .FindAsync(y => yearIds.Contains(y.Id) && !y.IsDeleted);

            var dtos = years.OrderByDescending(y => y.StartDate)
                .Select(y => new HistoricalYearDto
                {
                    Id = y.Id, Name = y.Name, StartDate = y.StartDate,
                    EndDate = y.EndDate, IsCurrent = y.IsCurrent
                }).ToList();
            return OperationResult<IReadOnlyList<HistoricalYearDto>>.Success(dtos);
        }

        return OperationResult<IReadOnlyList<HistoricalYearDto>>.Success(new List<HistoricalYearDto>());
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalClassDto>>> GetClassesByYearAsync(int userId, UserRole role, int academicYearId)
    {
        if (role == UserRole.Admin)
        {
            var classes = await _context.Classes
                .Where(c => c.AcademicYearId == academicYearId && !c.IsDeleted && c.Status == ClassStatus.Active)
                .Include(c => c.GradeLevel)
                .Include(c => c.Enrollments)
                .ToListAsync();

            var dtos = classes.Select(c => new HistoricalClassDto
            {
                Id = c.Id, Name = c.Name,
                GradeLevelName = c.GradeLevel?.Name,
                StudentCount = c.Enrollments.Count(e => !e.IsDeleted)
            }).ToList();
            return OperationResult<IReadOnlyList<HistoricalClassDto>>.Success(dtos);
        }

        if (role == UserRole.Teacher)
        {
            var assignments = await _context.ClassSubjectTeachers
                .Where(cst => cst.TeacherId == userId
                           && cst.AcademicYearId == academicYearId
                           && !cst.IsDeleted)
                .Include(cst => cst.Class)
                    .ThenInclude(c => c.GradeLevel)
                .Include(cst => cst.Class)
                    .ThenInclude(c => c.Enrollments)
                .ToListAsync();

            var classes = assignments
                .GroupBy(a => a.ClassId)
                .Select(g => g.First().Class)
                .ToList();

            var dtos = classes.Select(c => new HistoricalClassDto
            {
                Id = c.Id, Name = c.Name,
                GradeLevelName = c.GradeLevel?.Name,
                StudentCount = c.Enrollments.Count(e => !e.IsDeleted)
            }).ToList();
            return OperationResult<IReadOnlyList<HistoricalClassDto>>.Success(dtos);
        }

        if (role == UserRole.Student)
        {
            var enrollment = await _context.StudentEnrollments
                .Where(se => se.Student.UserId == userId
                          && se.AcademicYearId == academicYearId
                          && !se.IsDeleted)
                .Include(se => se.Class)
                    .ThenInclude(c => c.GradeLevel)
                .FirstOrDefaultAsync();

            if (enrollment == null)
                return OperationResult<IReadOnlyList<HistoricalClassDto>>.Success(new List<HistoricalClassDto>());

            var dto = new HistoricalClassDto
            {
                Id = enrollment.Class.Id,
                Name = enrollment.Class.Name,
                GradeLevelName = enrollment.Class.GradeLevel?.Name,
                StudentCount = 1
            };
            return OperationResult<IReadOnlyList<HistoricalClassDto>>.Success(new List<HistoricalClassDto> { dto });
        }

        if (role == UserRole.Parent)
        {
            var studentIds = await _context.ParentStudents
                .Where(ps => ps.ParentId == userId && !ps.IsDeleted)
                .Select(ps => ps.StudentId)
                .ToListAsync();

            var enrollments = await _context.StudentEnrollments
                .Where(se => studentIds.Contains(se.StudentId)
                          && se.AcademicYearId == academicYearId
                          && !se.IsDeleted)
                .Include(se => se.Class)
                    .ThenInclude(c => c.GradeLevel)
                .ToListAsync();

            var classes = enrollments
                .GroupBy(e => e.ClassId)
                .Select(g => g.First().Class)
                .ToList();

            var dtos = classes.Select(c => new HistoricalClassDto
            {
                Id = c.Id, Name = c.Name,
                GradeLevelName = c.GradeLevel?.Name,
                StudentCount = c.Enrollments.Count(e => !e.IsDeleted)
            }).ToList();
            return OperationResult<IReadOnlyList<HistoricalClassDto>>.Success(dtos);
        }

        return OperationResult<IReadOnlyList<HistoricalClassDto>>.Success(new List<HistoricalClassDto>());
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalStudentDto>>> GetStudentsByClassAsync(int userId, UserRole role, int classId)
    {
        if (role == UserRole.Admin)
        {
            var enrollments = await _context.StudentEnrollments
                .Where(e => e.ClassId == classId && !e.IsDeleted)
                .Include(e => e.Student)
                .ToListAsync();

            var dtos = enrollments.Select(e => new HistoricalStudentDto
            {
                Id = e.Student.Id,
                FullName = e.Student.FullName,
                EnrollmentId = e.Id
            }).ToList();
            return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Success(dtos);
        }

        if (role == UserRole.Teacher)
        {
            var hasAccess = await _context.ClassSubjectTeachers
                .AnyAsync(cst => cst.ClassId == classId
                              && cst.TeacherId == userId
                              && !cst.IsDeleted);

            if (!hasAccess)
                return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Failure("ليس لديك صلاحية لعرض هذا الفصل");

            var enrollments = await _context.StudentEnrollments
                .Where(e => e.ClassId == classId && !e.IsDeleted)
                .Include(e => e.Student)
                .ToListAsync();

            var dtos = enrollments.Select(e => new HistoricalStudentDto
            {
                Id = e.Student.Id,
                FullName = e.Student.FullName,
                EnrollmentId = e.Id
            }).ToList();
            return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Success(dtos);
        }

        return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Failure("ليس لديك صلاحية");
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalStudentDto>>> GetStudentsByYearAsync(int userId, UserRole role, int academicYearId)
    {
        if (role == UserRole.Admin)
        {
            var enrollments = await _context.StudentEnrollments
                .Where(e => e.AcademicYearId == academicYearId && !e.IsDeleted)
                .Include(e => e.Student)
                .ToListAsync();

            var dtos = enrollments.Select(e => new HistoricalStudentDto
            {
                Id = e.Student.Id,
                FullName = e.Student.FullName,
                EnrollmentId = e.Id
            }).DistinctBy(s => s.Id).ToList();
            return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Success(dtos);
        }

        if (role == UserRole.Teacher)
        {
            var classIds = await _context.ClassSubjectTeachers
                .Where(cst => cst.TeacherId == userId
                           && cst.AcademicYearId == academicYearId
                           && !cst.IsDeleted)
                .Select(cst => cst.ClassId)
                .Distinct()
                .ToListAsync();

            var enrollments = await _context.StudentEnrollments
                .Where(e => classIds.Contains(e.ClassId) && !e.IsDeleted)
                .Include(e => e.Student)
                .ToListAsync();

            var dtos = enrollments.Select(e => new HistoricalStudentDto
            {
                Id = e.Student.Id,
                FullName = e.Student.FullName,
                EnrollmentId = e.Id
            }).DistinctBy(s => s.Id).ToList();
            return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Success(dtos);
        }

        if (role == UserRole.Student)
        {
            var enrollments = await _context.StudentEnrollments
                .Where(e => e.Student.UserId == userId && e.AcademicYearId == academicYearId && !e.IsDeleted)
                .Include(e => e.Student)
                .ToListAsync();

            var dtos = enrollments.Select(e => new HistoricalStudentDto
            {
                Id = e.Student.Id,
                FullName = e.Student.FullName,
                EnrollmentId = e.Id
            }).DistinctBy(s => s.Id).ToList();
            return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Success(dtos);
        }

        if (role == UserRole.Parent)
        {
            var studentIds = await _context.ParentStudents
                .Where(ps => ps.ParentId == userId && !ps.IsDeleted)
                .Select(ps => ps.StudentId)
                .ToListAsync();

            var enrollments = await _context.StudentEnrollments
                .Where(e => studentIds.Contains(e.StudentId) && e.AcademicYearId == academicYearId && !e.IsDeleted)
                .Include(e => e.Student)
                .ToListAsync();

            var dtos = enrollments.Select(e => new HistoricalStudentDto
            {
                Id = e.Student.Id,
                FullName = e.Student.FullName,
                EnrollmentId = e.Id
            }).DistinctBy(s => s.Id).ToList();
            return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Success(dtos);
        }

        return OperationResult<IReadOnlyList<HistoricalStudentDto>>.Failure("ليس لديك صلاحية");
    }

    public async Task<OperationResult<HistoricalDataOverviewDto>> GetClassOverviewAsync(int userId, UserRole role, int classId, AcademicTerm? term = null)
    {
        var accessCheck = await EnsureClassAccessAsync(userId, role, classId);
        if (!accessCheck.IsSuccess)
            return OperationResult<HistoricalDataOverviewDto>.Failure(accessCheck.Message!);

        var grades = await _context.FinalGrades
            .Where(fg => fg.Enrollment.ClassId == classId && !fg.IsDeleted)
            .Include(fg => fg.Enrollment)
            .ToListAsync();

        if (term.HasValue)
            grades = grades.Where(fg => fg.Term == term.Value).ToList();

        var overview = new HistoricalDataOverviewDto
        {
            TotalStudents = grades.Select(g => g.EnrollmentId).Distinct().Count(),
            TotalFinalGrades = grades.Count,
            ClassAverage = grades.Any()
                ? Math.Round(grades.Average(g => g.MaxTotal > 0 ? (decimal)g.Total / g.MaxTotal * 100 : 0), 1)
                : null
        };

        var enrollmentsCount = await _context.StudentEnrollments
            .CountAsync(e => e.ClassId == classId && !e.IsDeleted);
        if (enrollmentsCount > overview.TotalStudents)
            overview.TotalStudents = enrollmentsCount;

        return OperationResult<HistoricalDataOverviewDto>.Success(overview);
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalFinalGradeDto>>> GetFinalGradesAsync(int userId, UserRole role, int classId, AcademicTerm? term = null, int? subjectId = null)
    {
        var accessCheck = await EnsureClassAccessAsync(userId, role, classId);
        if (!accessCheck.IsSuccess)
            return OperationResult<IReadOnlyList<HistoricalFinalGradeDto>>.Failure(accessCheck.Message!);

        var query = _context.FinalGrades
            .Where(fg => fg.Enrollment.ClassId == classId && !fg.IsDeleted)
            .Include(fg => fg.Enrollment).ThenInclude(e => e.Student)
            .Include(fg => fg.Subject)
            .AsQueryable();

        if (term.HasValue)
            query = query.Where(fg => fg.Term == term.Value);
        if (subjectId.HasValue)
            query = query.Where(fg => fg.SubjectId == subjectId.Value);

        var grades = await query.OrderByDescending(fg => fg.Total).ToListAsync();

        var dtos = grades.Select(g => new HistoricalFinalGradeDto
        {
            Id = g.Id,
            EnrollmentId = g.EnrollmentId,
            SubjectId = g.SubjectId,
            SubjectName = g.Subject?.Name,
            StudentId = g.Enrollment.Student.Id,
            StudentName = g.Enrollment.Student.FullName,
            AcademicTerm = (int)g.Term,
            PeriodAvgScore = g.PeriodAvgScore,
            Assessment1Score = g.Assessment1Score,
            Assessment2Score = g.Assessment2Score,
            WrittenTotal = g.WrittenTotal,
            FinalExamScore = g.FinalExamScore,
            Total = g.Total,
            MaxTotal = g.MaxTotal,
            IsPublished = g.IsPublished,
            IsComplete = g.IsComplete
        }).ToList();

        return OperationResult<IReadOnlyList<HistoricalFinalGradeDto>>.Success(dtos);
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalEvaluationDto>>> GetEvaluationsAsync(int userId, UserRole role, int classId, int? periodId = null)
    {
        var accessCheck = await EnsureClassAccessAsync(userId, role, classId);
        if (!accessCheck.IsSuccess)
            return OperationResult<IReadOnlyList<HistoricalEvaluationDto>>.Failure(accessCheck.Message!);

        var enrollments = await _context.StudentEnrollments
            .Where(e => e.ClassId == classId && !e.IsDeleted)
            .Select(e => e.Id)
            .ToListAsync();

        var query = _context.StudentEvaluations
            .Where(se => enrollments.Contains(se.EnrollmentId) && !se.IsDeleted)
            .Include(se => se.Enrollment).ThenInclude(e => e.Student)
            .Include(se => se.EvaluationItem)
            .Include(se => se.Period)
            .AsQueryable();

        if (periodId.HasValue)
            query = query.Where(se => se.PeriodId == periodId.Value);

        var evaluations = await query.ToListAsync();

        var dtos = evaluations.Select(e => new HistoricalEvaluationDto
        {
            EnrollmentId = e.EnrollmentId,
            StudentName = e.Enrollment?.Student?.FullName,
            ItemName = e.EvaluationItem?.Name,
            PeriodName = e.Period?.Name,
            Score = e.Score,
            MaxScore = e.EvaluationItem?.MaxScore ?? 0
        }).ToList();

        return OperationResult<IReadOnlyList<HistoricalEvaluationDto>>.Success(dtos);
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalPeriodicAssessmentDto>>> GetAssessmentsAsync(int userId, UserRole role, int classId, int? subjectId = null, AcademicTerm? term = null)
    {
        var accessCheck = await EnsureClassAccessAsync(userId, role, classId);
        if (!accessCheck.IsSuccess)
            return OperationResult<IReadOnlyList<HistoricalPeriodicAssessmentDto>>.Failure(accessCheck.Message!);

        var enrollments = await _context.StudentEnrollments
            .Where(e => e.ClassId == classId && !e.IsDeleted)
            .Select(e => e.Id)
            .ToListAsync();

        var query = _context.PeriodicAssessments
            .Where(pa => enrollments.Contains(pa.EnrollmentId) && !pa.IsDeleted)
            .Include(pa => pa.Enrollment).ThenInclude(e => e.Student)
            .Include(pa => pa.Subject)
            .AsQueryable();

        if (subjectId.HasValue)
            query = query.Where(pa => pa.SubjectId == subjectId.Value);
        if (term.HasValue)
            query = query.Where(pa => pa.Term == term.Value);

        var assessments = await query.ToListAsync();

        var dtos = assessments.Select(a => new HistoricalPeriodicAssessmentDto
        {
            Id = a.Id,
            EnrollmentId = a.EnrollmentId,
            StudentName = a.Enrollment?.Student?.FullName,
            SubjectName = a.Subject?.Name,
            AssessmentType = a.AssessmentType.ToString(),
            Score = a.Score,
            MaxScore = a.MaxScore,
            Term = (int?)a.Term
        }).ToList();

        return OperationResult<IReadOnlyList<HistoricalPeriodicAssessmentDto>>.Success(dtos);
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalExamDto>>> GetExamsAsync(int userId, UserRole role, int enrollmentId)
    {
        var hasAccess = await EnsureEnrollmentAccessAsync(userId, role, enrollmentId);
        if (!hasAccess)
            return OperationResult<IReadOnlyList<HistoricalExamDto>>.Failure("ليس لديك صلاحية");

        var attempts = await _context.StudentExamAttempts
            .Where(a => a.EnrollmentId == enrollmentId && !a.IsDeleted && a.SubmittedAt != null)
            .Include(a => a.Exam).ThenInclude(e => e.Subject)
            .ToListAsync();

        var dtos = attempts.Select(a => new HistoricalExamDto
        {
            ExamId = a.ExamId,
            ExamTitle = a.Exam?.Title,
            SubjectName = a.Exam?.Subject?.Name,
            Score = (int?)(a.Score ?? 0),
            TotalScore = (int?)a.TotalScore,
            Percentage = a.TotalScore > 0 ? Math.Round((double)(a.Score ?? 0) / (double)a.TotalScore * 100, 1) : null,
            IsCompleted = a.SubmittedAt != null
        }).ToList();

        return OperationResult<IReadOnlyList<HistoricalExamDto>>.Success(dtos);
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalAssignmentDto>>> GetAssignmentsAsync(int userId, UserRole role, int enrollmentId)
    {
        var hasAccess = await EnsureEnrollmentAccessAsync(userId, role, enrollmentId);
        if (!hasAccess)
            return OperationResult<IReadOnlyList<HistoricalAssignmentDto>>.Failure("ليس لديك صلاحية");

        var submissions = await _context.StudentAssignmentSubmissions
            .Where(s => s.EnrollmentId == enrollmentId && !s.IsDeleted && s.Score.HasValue)
            .Include(s => s.Assignment).ThenInclude(a => a.ClassSubjectTeacher).ThenInclude(cst => cst.Subject)
            .ToListAsync();

        var dtos = submissions.Select(s => new HistoricalAssignmentDto
        {
            AssignmentId = s.AssignmentId,
            AssignmentTitle = s.Assignment?.Title,
            SubjectName = s.Assignment?.ClassSubjectTeacher?.Subject?.Name,
            Score = s.Score,
            MaxScore = s.MaxScore,
            Percentage = s.MaxScore > 0 ? (double?)((double)(s.Score ?? 0) / (double)s.MaxScore * 100) : null,
            IsGraded = s.Score.HasValue
        }).ToList();

        return OperationResult<IReadOnlyList<HistoricalAssignmentDto>>.Success(dtos);
    }

    public async Task<OperationResult<IReadOnlyList<HistoricalAbsenceDto>>> GetAbsencesAsync(int userId, UserRole role, int classId, int? subjectId = null)
    {
        var accessCheck = await EnsureClassAccessAsync(userId, role, classId);
        if (!accessCheck.IsSuccess)
            return OperationResult<IReadOnlyList<HistoricalAbsenceDto>>.Failure(accessCheck.Message!);

        var enrollments = await _context.StudentEnrollments
            .Where(e => e.ClassId == classId && !e.IsDeleted)
            .Select(e => e.Id)
            .ToListAsync();

        var query = _context.DailyAbsences
            .Where(da => enrollments.Contains(da.EnrollmentId) && !da.IsDeleted)
            .Include(da => da.Enrollment).ThenInclude(e => e.Student)
            .Include(da => da.ClassSubjectTeacher).ThenInclude(cst => cst.Subject)
            .AsQueryable();

        if (subjectId.HasValue)
            query = query.Where(da => da.ClassSubjectTeacher != null && da.ClassSubjectTeacher.SubjectId == subjectId.Value);

        var absences = await query.ToListAsync();

        var dtos = absences.Select(a => new HistoricalAbsenceDto
        {
            EnrollmentId = a.EnrollmentId,
            StudentName = a.Enrollment?.Student?.FullName,
            SubjectName = a.ClassSubjectTeacher?.Subject?.Name,
            AbsenceDate = a.AbsenceDate,
            IsAbsent = a.IsAbsent,
            Reason = a.Reason
        }).ToList();

        return OperationResult<IReadOnlyList<HistoricalAbsenceDto>>.Success(dtos);
    }

    public async Task<OperationResult<HistoricalStudentSummaryDto>> GetStudentSummaryAsync(int userId, UserRole role, int studentId, int academicYearId)
    {
        if (role == UserRole.Student)
        {
            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.Id == studentId && s.UserId == userId && !s.IsDeleted);
            if (student == null)
                return OperationResult<HistoricalStudentSummaryDto>.Failure("ليس لديك صلاحية");
        }
        else if (role == UserRole.Parent)
        {
            var hasChild = await _context.ParentStudents
                .AnyAsync(ps => ps.ParentId == userId && ps.StudentId == studentId && !ps.IsDeleted);
            if (!hasChild)
                return OperationResult<HistoricalStudentSummaryDto>.Failure("ليس لديك صلاحية");
        }

        var enrollment = await _context.StudentEnrollments
            .Where(e => e.StudentId == studentId && e.AcademicYearId == academicYearId && !e.IsDeleted)
            .Include(e => e.Class).ThenInclude(c => c.GradeLevel)
            .FirstOrDefaultAsync();

        if (enrollment == null)
            return OperationResult<HistoricalStudentSummaryDto>.Failure("الطالب غير مسجل في هذه السنة");

        var studentEntity = await _unitOfWork.Students.GetByIdAsync(studentId);

        var summary = new HistoricalStudentSummaryDto
        {
            StudentId = studentId,
            StudentName = studentEntity?.FullName ?? "",
            ClassName = enrollment.Class?.Name,
            GradeLevelName = enrollment.Class?.GradeLevel?.Name
        };

        var finalGrades = await _context.FinalGrades
            .Where(fg => fg.EnrollmentId == enrollment.Id && !fg.IsDeleted)
            .Include(fg => fg.Subject)
            .ToListAsync();

        summary.FinalGrades = finalGrades.Select(g => new HistoricalFinalGradeDto
        {
            Id = g.Id,
            EnrollmentId = g.EnrollmentId,
            SubjectId = g.SubjectId,
            SubjectName = g.Subject?.Name,
            StudentId = studentId,
            StudentName = studentEntity?.FullName,
            AcademicTerm = (int)g.Term,
            PeriodAvgScore = g.PeriodAvgScore,
            Assessment1Score = g.Assessment1Score,
            Assessment2Score = g.Assessment2Score,
            WrittenTotal = g.WrittenTotal,
            FinalExamScore = g.FinalExamScore,
            Total = g.Total,
            MaxTotal = g.MaxTotal,
            IsPublished = g.IsPublished,
            IsComplete = g.IsComplete
        }).ToList();

        var evals = await _context.StudentEvaluations
            .Where(se => se.EnrollmentId == enrollment.Id && !se.IsDeleted)
            .Include(se => se.EvaluationItem)
            .Include(se => se.Period)
            .ToListAsync();

        summary.Evaluations = evals.Select(e => new HistoricalEvaluationDto
        {
            EnrollmentId = e.EnrollmentId,
            ItemName = e.EvaluationItem?.Name,
            PeriodName = e.Period?.Name,
            Score = e.Score,
            MaxScore = e.EvaluationItem?.MaxScore ?? 0
        }).ToList();

        var assessments = await _context.PeriodicAssessments
            .Where(pa => pa.EnrollmentId == enrollment.Id && !pa.IsDeleted)
            .Include(pa => pa.Subject)
            .ToListAsync();

        summary.Assessments = assessments.Select(a => new HistoricalPeriodicAssessmentDto
        {
            Id = a.Id,
            EnrollmentId = a.EnrollmentId,
            SubjectName = a.Subject?.Name,
            AssessmentType = a.AssessmentType.ToString(),
            Score = a.Score,
            MaxScore = a.MaxScore,
            Term = (int?)a.Term
        }).ToList();

        var exams = await _context.StudentExamAttempts
            .Where(a => a.EnrollmentId == enrollment.Id && !a.IsDeleted && a.SubmittedAt != null)
            .Include(a => a.Exam).ThenInclude(e => e.Subject)
            .ToListAsync();

        summary.Exams = exams.Select(a => new HistoricalExamDto
        {
            ExamId = a.ExamId,
            ExamTitle = a.Exam?.Title,
            SubjectName = a.Exam?.Subject?.Name,
            Score = (int?)(a.Score ?? 0),
            TotalScore = (int?)a.TotalScore,
            Percentage = a.TotalScore > 0 ? Math.Round((double)(a.Score ?? 0) / (double)a.TotalScore * 100, 1) : null,
            IsCompleted = a.SubmittedAt != null
        }).ToList();

        var assignments = await _context.StudentAssignmentSubmissions
            .Where(s => s.EnrollmentId == enrollment.Id && !s.IsDeleted && s.Score.HasValue)
            .Include(s => s.Assignment).ThenInclude(a => a.ClassSubjectTeacher).ThenInclude(cst => cst.Subject)
            .ToListAsync();

        summary.Assignments = assignments.Select(s => new HistoricalAssignmentDto
        {
            AssignmentId = s.AssignmentId,
            AssignmentTitle = s.Assignment?.Title,
            SubjectName = s.Assignment?.ClassSubjectTeacher?.Subject?.Name,
            Score = s.Score,
            MaxScore = s.MaxScore,
            Percentage = s.MaxScore > 0 ? (double?)((double)(s.Score ?? 0) / (double)s.MaxScore * 100) : null,
            IsGraded = s.Score.HasValue
        }).ToList();

        var absences = await _context.DailyAbsences
            .Where(da => da.EnrollmentId == enrollment.Id && !da.IsDeleted)
            .Include(da => da.ClassSubjectTeacher).ThenInclude(cst => cst.Subject)
            .ToListAsync();

        summary.Absences = absences.Select(a => new HistoricalAbsenceDto
        {
            EnrollmentId = a.EnrollmentId,
            SubjectName = a.ClassSubjectTeacher?.Subject?.Name,
            AbsenceDate = a.AbsenceDate,
            IsAbsent = a.IsAbsent,
            Reason = a.Reason
        }).ToList();

        return OperationResult<HistoricalStudentSummaryDto>.Success(summary);
    }

    private async Task<OperationResult> EnsureClassAccessAsync(int userId, UserRole role, int classId)
    {
        if (role == UserRole.Admin)
            return OperationResult.Success();

        if (role == UserRole.Teacher)
        {
            var hasAccess = await _context.ClassSubjectTeachers
                .AnyAsync(cst => cst.ClassId == classId
                              && cst.TeacherId == userId
                              && !cst.IsDeleted);
            return hasAccess
                ? OperationResult.Success()
                : OperationResult.Failure("ليس لديك صلاحية لعرض هذا الفصل");
        }

        return OperationResult.Failure("ليس لديك صلاحية");
    }

    private async Task<bool> EnsureEnrollmentAccessAsync(int userId, UserRole role, int enrollmentId)
    {
        if (role == UserRole.Admin)
            return true;

        if (role == UserRole.Teacher)
        {
            var classId = await _context.StudentEnrollments
                .Where(e => e.Id == enrollmentId && !e.IsDeleted)
                .Select(e => e.ClassId)
                .FirstOrDefaultAsync();

            return await _context.ClassSubjectTeachers
                .AnyAsync(cst => cst.ClassId == classId
                              && cst.TeacherId == userId
                              && !cst.IsDeleted);
        }

        if (role == UserRole.Student)
        {
            return await _context.StudentEnrollments
                .AnyAsync(e => e.Id == enrollmentId
                            && e.Student.UserId == userId
                            && !e.IsDeleted);
        }

        if (role == UserRole.Parent)
        {
            var studentId = await _context.StudentEnrollments
                .Where(e => e.Id == enrollmentId && !e.IsDeleted)
                .Select(e => e.StudentId)
                .FirstOrDefaultAsync();

            return await _context.ParentStudents
                .AnyAsync(ps => ps.ParentId == userId
                             && ps.StudentId == studentId
                             && !ps.IsDeleted);
        }

        return false;
    }
}
