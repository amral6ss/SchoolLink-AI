using Common.Results;
using Project.BLL.DTOs.HistoricalData;
using Project.Domain.Enums;

namespace Project.BLL.Interfaces;

public interface IHistoricalDataService
{
    Task<OperationResult<IReadOnlyList<HistoricalYearDto>>> GetAccessibleYearsAsync(int userId, UserRole role);
    Task<OperationResult<IReadOnlyList<HistoricalClassDto>>> GetClassesByYearAsync(int userId, UserRole role, int academicYearId);
    Task<OperationResult<IReadOnlyList<HistoricalStudentDto>>> GetStudentsByClassAsync(int userId, UserRole role, int classId);
    Task<OperationResult<HistoricalDataOverviewDto>> GetClassOverviewAsync(int userId, UserRole role, int classId, AcademicTerm? term = null);
    Task<OperationResult<IReadOnlyList<HistoricalFinalGradeDto>>> GetFinalGradesAsync(int userId, UserRole role, int classId, AcademicTerm? term = null, int? subjectId = null);
    Task<OperationResult<IReadOnlyList<HistoricalEvaluationDto>>> GetEvaluationsAsync(int userId, UserRole role, int classId, int? periodId = null);
    Task<OperationResult<IReadOnlyList<HistoricalPeriodicAssessmentDto>>> GetAssessmentsAsync(int userId, UserRole role, int classId, int? subjectId = null, AcademicTerm? term = null);
    Task<OperationResult<IReadOnlyList<HistoricalExamDto>>> GetExamsAsync(int userId, UserRole role, int enrollmentId);
    Task<OperationResult<IReadOnlyList<HistoricalAssignmentDto>>> GetAssignmentsAsync(int userId, UserRole role, int enrollmentId);
    Task<OperationResult<IReadOnlyList<HistoricalAbsenceDto>>> GetAbsencesAsync(int userId, UserRole role, int classId, int? subjectId = null);
    Task<OperationResult<HistoricalStudentSummaryDto>> GetStudentSummaryAsync(int userId, UserRole role, int studentId, int academicYearId);
    Task<OperationResult<IReadOnlyList<HistoricalStudentDto>>> GetStudentsByYearAsync(int userId, UserRole role, int academicYearId);
}
