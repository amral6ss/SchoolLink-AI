using Project.Domain.Entities;
using Project.Domain.Enums;
using System.Linq.Expressions;

namespace Project.DAL.Interfaces.Repositories.Core;

public interface IStudentEnrollmentRepository : IRepository<StudentEnrollment>
{
    Task<StudentEnrollment?>            GetActiveByStudentAndYearAsync(int studentId, int academicYearId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetActiveByStudentsAndYearAsync(IReadOnlyCollection<int> studentIds, int academicYearId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetByClassAndYearAsync(int classId, int academicYearId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetActiveByClassAsync(int classId, int academicYearId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetActiveByGradeLevelAndYearWithDetailsAsync(int gradeLevelId, int academicYearId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetHistoryByStudentAsync(int studentId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetTransfersHistoryAsync(int academicYearId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<bool> IsEnrolledAsync(int studentId, int classId, int academicYearId, CancellationToken ct = default);
    Task<bool> HasActiveEnrollmentAsync(int studentId, int academicYearId, CancellationToken ct = default);

    Task<StudentEnrollment?>               GetWithStudentAsync(int enrollmentId, CancellationToken ct = default);
    Task<StudentEnrollment?>               GetByIdWithDetailsAsync(int enrollmentId, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetByIdsWithDetailsAsync(IEnumerable<int> enrollmentIds, CancellationToken ct = default);
    Task<IReadOnlyList<StudentEnrollment>> GetByClassWithStudentAsync(int classId, int academicYearId, CancellationToken ct = default);

    Task<int> GetActiveCountByClassAsync(int classId, int academicYearId, CancellationToken ct = default);

    Task<IReadOnlyList<StudentEnrollment>> GetActiveEnrollmentsByYearAsync(int academicYearId, CancellationToken ct = default);

    Task<int> GetTransfersCountAsync(int academicYearId, CancellationToken ct = default);

    /// <summary>
    /// يجيب الطلاب الغير مسجلين في أي فصل نشط داخل سنة معينة مع pagination وفلتر وترتيب.
    /// </summary>
    Task<(IReadOnlyList<Project.Domain.Entities.Student> Students, int TotalCount)> GetUnenrolledStudentsAsync(
        int academicYearId,
        string? searchTerm,
        DateOnly? birthDateFrom,
        DateOnly? birthDateTo,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default);
}


