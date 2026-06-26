namespace Project.BLL.DTOs.ExamAttempt;

public class AiGradeSuggestionDto
{
    public int AnswerId { get; set; }
    public decimal SuggestedPoints { get; set; }
    public string? Justification { get; set; }
}

public class AiGradeResponseDto
{
    public int AttemptId { get; set; }
    public List<AiGradeSuggestionDto> Suggestions { get; set; } = new();
}
