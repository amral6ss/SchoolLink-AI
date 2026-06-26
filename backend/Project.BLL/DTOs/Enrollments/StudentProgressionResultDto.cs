namespace Project.BLL.DTOs.Enrollments;

public class StudentProgressionResultDto
{
    public int TotalRequested { get; set; }
    public int SuccessCount { get; set; }
    public int PromotedCount { get; set; }
    public int RetainedCount { get; set; }
    public int GraduatedCount { get; set; }
    public int FailureCount { get; set; }
    public List<StudentProgressionFailureDto> Failures { get; set; } = new();
}

public class StudentProgressionFailureDto
{
    public int EnrollmentId { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
