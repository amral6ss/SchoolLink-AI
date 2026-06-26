using Common.Results;
using Project.BLL.DTOs;

namespace Project.BLL.Interfaces;

public interface IClassService
{
    Task<OperationResult<ClassDto>>              CreateClassAsync(CreateClassRequest request);
    Task<OperationResult<ClassDto>>              UpdateClassAsync(UpdateClassRequest request);
    Task<OperationResult>                        DeleteClassAsync(int id);
    Task<OperationResult<IEnumerable<ClassDto>>> GetAllClassesAsync(GetClassesFilter filter);
    Task<OperationResult<ClassDto>>              GetClassByIdAsync(int id);
    Task<OperationResult<IEnumerable<ClassDto>>> GetClassesByGradeLevelAsync(int gradeLevelId);
    Task<OperationResult<ClassDto>>              GetClassWithStudentsAsync(int classId);
    Task<OperationResult<IEnumerable<ClassDto>>> GetClassesByTeacherAsync(int teacherId, int academicYearId);
    Task<OperationResult<ClassDto>>              CreateClassWithStudentsAsync(CreateClassWithStudentsRequest request);
    Task<OperationResult<CopyClassesFromYearResultDto>> PreviewCopyClassesFromYearAsync(CopyClassesFromYearRequest request);
    Task<OperationResult<CopyClassesFromYearResultDto>> CopyClassesFromYearAsync(CopyClassesFromYearRequest request);
    Task<OperationResult<int>>                   GetClassCountAsync(int? academicYearId = null);
    Task<OperationResult<object>>                GetClassStatsAsync(int? academicYearId = null);
}
