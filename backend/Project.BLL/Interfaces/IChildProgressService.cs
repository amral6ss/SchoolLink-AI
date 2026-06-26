using Common.Results;
using Project.BLL.DTOs.ChildProgress;

namespace Project.BLL.Interfaces;

public interface IChildProgressService
{
    Task<OperationResult<List<ChildProgressItemDto>>> GetChildProgressAsync(int parentUserId, int? term = null);
    Task<OperationResult<ChildExamAttemptResultDto>> GetExamAttemptResultAsync(int parentUserId, int examId);
}
