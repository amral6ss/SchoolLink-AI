using Common.Results;
using Project.BLL.DTOs.ExamAttempt;

namespace Project.BLL.Interfaces
{
    // Phase 6.2 — DTOs للتصحيح اليدوي للإجابات المقالية
    public record GradeEssayAnswerDto(int AnswerId, decimal PointsEarned, string? Feedback);
    public record GradeEssayAttemptDto(List<GradeEssayAnswerDto> Answers);

    public interface IExamAttemptService
    {
        Task<OperationResult<GetExamAttemptDto>> GetByIdAsync(int id);
        Task<OperationResult<List<ExamAttemptSummaryDto>>> GetByExamIdAsync(int examId, int teacherId);
        Task<OperationResult<GetExamAttemptDto>> StartAttemptAsync(CreateExamAttemptDto dto);
        Task<OperationResult<GetExamAttemptDto>> SubmitAttemptAsync(SubmitExamAttemptDto dto);
        Task<OperationResult> GradeEssayAnswersAsync(int attemptId, GradeEssayAttemptDto dto, int teacherId);
        Task<OperationResult<List<ExamAttemptSummaryDto>>> GetStudentAttemptsAsync(int enrollmentId, int examId);
        Task<OperationResult<AiGradeResponseDto>> AiGradeSuggestionsAsync(int attemptId, int teacherId);

        /// <summary>Legacy — kept for backward compatibility; marked for removal.</summary>
        Task<OperationResult> AutoGradeAsync(int attemptId);
    }
}