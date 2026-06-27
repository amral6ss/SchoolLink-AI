using Common.Results;
using Project.BLL.DTOs.Assignment;
using Project.BLL.DTOs.Common;

namespace Project.BLL.Interfaces;

public class AssignmentManagerItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Deadline { get; set; } = string.Empty;
    public decimal MaxScore { get; set; }
    public bool IsPublished { get; set; }
    public bool IsAIGenerated { get; set; }
    public int QuestionsCount { get; set; }
    public int Submitted { get; set; }
    public int Total { get; set; }
    public double? AvgScore { get; set; }
    public string Status { get; set; } = "draft";
}

public class AssignmentManagerQuestionDto
{
    public int Id { get; set; }
    public string Type { get; set; } = "mcq";
    public string Text { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public string CorrectAnswer { get; set; } = string.Empty;
    public decimal Points { get; set; } = 5;
}

public class AssignmentManagerDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Deadline { get; set; } = string.Empty;
    public decimal MaxScore { get; set; }
    public bool IsPublished { get; set; }
    public bool IsAIGenerated { get; set; }
    public int QuestionsCount { get; set; }
    public int Submitted { get; set; }
    public int Total { get; set; }
    public string Status { get; set; } = "draft";
    public List<AssignmentManagerQuestionDto> Questions { get; set; } = new();
}

public class AssignmentManagerStatsDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public double AvgDelivery { get; set; }
    public int Overdue { get; set; }
}

public class CreateAssignmentManagerDto
{
    public string Title { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public int ClassId { get; set; }
    public string? Deadline { get; set; }
    public List<CreateManagerQuestionDto> Questions { get; set; } = new();
}

public class CreateManagerQuestionDto
{
    public string Type { get; set; } = "mcq";
    public string Text { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public string CorrectAnswer { get; set; } = string.Empty;
    public decimal Points { get; set; } = 5;
}

public class UpdateAssignmentManagerDto
{
    public string Title { get; set; } = string.Empty;
    public string? Deadline { get; set; }
    public List<CreateManagerQuestionDto> Questions { get; set; } = new();
}

public class AssignmentSubmissionListItemDto
{
    public int SubmissionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string SubmittedAt { get; set; } = string.Empty;
    public bool IsGraded { get; set; }
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
}

public class AssignmentSubmissionDetailDto
{
    public int SubmissionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public bool IsGraded { get; set; }
    public List<AssignmentSubmissionAnswerDto> Answers { get; set; } = new();
}

public class AssignmentSubmissionAnswerDto
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string Type { get; set; } = "mcq";
    public string StudentAnswer { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty;
    public decimal PointsEarned { get; set; }
    public decimal MaxPoints { get; set; }
    public bool? IsCorrect { get; set; }
}

public class GradeAssignmentSubmissionDto
{
    public Dictionary<int, decimal> ManualGrades { get; set; } = new();
}

public interface IAssignmentManagerService
{
    Task<OperationResult<List<AssignmentManagerItemDto>>> GetAllAsync(int? classSubjectTeacherId = null);
    Task<OperationResult<PagedResult<AssignmentManagerItemDto>>> GetFilteredAsync(AssignmentFilterDto filter);
    Task<OperationResult<AssignmentManagerDetailDto>> GetByIdAsync(int id);
    Task<OperationResult<AssignmentManagerItemDto>> CreateAsync(CreateAssignmentManagerDto dto, int teacherId);
    Task<OperationResult> UpdateAsync(int id, UpdateAssignmentManagerDto dto);
    Task<OperationResult> DeleteAsync(int id, int userId, string role);
    Task<OperationResult<AssignmentManagerStatsDto>> GetStatsAsync(AssignmentFilterDto filter);
    
    Task<OperationResult<List<AssignmentSubmissionListItemDto>>> GetSubmissionsAsync(int assignmentId);
    Task<OperationResult<AssignmentSubmissionDetailDto>> GetSubmissionDetailAsync(int assignmentId, int submissionId);
    Task<OperationResult> GradeSubmissionAsync(int assignmentId, int submissionId, GradeAssignmentSubmissionDto dto);
}
