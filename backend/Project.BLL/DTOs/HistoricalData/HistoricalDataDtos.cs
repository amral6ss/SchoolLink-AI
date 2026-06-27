using Project.Domain.Enums;

namespace Project.BLL.DTOs.HistoricalData;

public class HistoricalYearDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

public class HistoricalClassDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GradeLevelName { get; set; }
    public int StudentCount { get; set; }
    public int? TeacherSubjectId { get; set; }
}

public class HistoricalStudentDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int EnrollmentId { get; set; }
}

public class HistoricalFinalGradeDto
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int StudentId { get; set; }
    public string? StudentName { get; set; }
    public string? ClassName { get; set; }
    public int AcademicTerm { get; set; }
    public decimal PeriodAvgScore { get; set; }
    public decimal Assessment1Score { get; set; }
    public decimal Assessment2Score { get; set; }
    public decimal WrittenTotal { get; set; }
    public decimal FinalExamScore { get; set; }
    public decimal Total { get; set; }
    public decimal MaxTotal { get; set; }
    public bool IsPublished { get; set; }
    public bool IsComplete { get; set; }
    public decimal Percentage => MaxTotal > 0 ? Math.Round(Total / MaxTotal * 100, 1) : 0;
}

public class HistoricalEvaluationDto
{
    public int EnrollmentId { get; set; }
    public string? StudentName { get; set; }
    public string? ItemName { get; set; }
    public string? PeriodName { get; set; }
    public decimal? Score { get; set; }
    public decimal MaxScore { get; set; }
}

public class HistoricalPeriodicAssessmentDto
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public string? StudentName { get; set; }
    public string? SubjectName { get; set; }
    public string AssessmentType { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public int? Term { get; set; }
}

public class HistoricalExamDto
{
    public int ExamId { get; set; }
    public string? ExamTitle { get; set; }
    public string? SubjectName { get; set; }
    public int? Score { get; set; }
    public int? TotalScore { get; set; }
    public double? Percentage { get; set; }
    public bool IsCompleted { get; set; }
}

public class HistoricalAssignmentDto
{
    public int AssignmentId { get; set; }
    public string? AssignmentTitle { get; set; }
    public string? SubjectName { get; set; }
    public decimal? Score { get; set; }
    public decimal? MaxScore { get; set; }
    public double? Percentage { get; set; }
    public bool IsGraded { get; set; }
}

public class HistoricalAbsenceDto
{
    public int EnrollmentId { get; set; }
    public string? StudentName { get; set; }
    public string? SubjectName { get; set; }
    public DateOnly AbsenceDate { get; set; }
    public bool IsAbsent { get; set; }
    public string? Reason { get; set; }
}

public class HistoricalStudentSummaryDto
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public string? GradeLevelName { get; set; }
    public List<HistoricalFinalGradeDto> FinalGrades { get; set; } = new();
    public List<HistoricalEvaluationDto> Evaluations { get; set; } = new();
    public List<HistoricalPeriodicAssessmentDto> Assessments { get; set; } = new();
    public List<HistoricalExamDto> Exams { get; set; } = new();
    public List<HistoricalAssignmentDto> Assignments { get; set; } = new();
    public List<HistoricalAbsenceDto> Absences { get; set; } = new();
}

public class HistoricalDataOverviewDto
{
    public int TotalStudents { get; set; }
    public int TotalClasses { get; set; }
    public int TotalFinalGrades { get; set; }
    public decimal? ClassAverage { get; set; }
}
