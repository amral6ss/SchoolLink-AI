using Project.Domain.Entities;
using Project.Domain.Enums;
using System.Linq.Expressions;

namespace Project.DAL.Interfaces.Repositories.Core;

public interface ISchoolClassRepository : IRepository<SchoolClass>
{
    Task<IReadOnlyList<SchoolClass>> GetByGradeLevelAndYearAsync(int gradeLevelId, int academicYearId, CancellationToken ct = default);
    Task<IReadOnlyList<SchoolClass>> GetByAcademicYearAsync(int academicYearId, CancellationToken ct = default);

    Task<SchoolClass?> GetByNameGradeLevelAndYearAsync(string name, int gradeLevelId, int academicYearId, CancellationToken ct = default);
    Task<bool>         ExistsByNameGradeLevelAndYearAsync(string name, int gradeLevelId, int academicYearId, CancellationToken ct = default);

    Task<int>                        GetEnrollmentCountAsync(int classId, int academicYearId, CancellationToken ct = default);
    Task<IReadOnlyList<SchoolClass>> GetWithEnrollmentCountAsync(int academicYearId, CancellationToken ct = default);

    /// <summary>
    /// يجيب SchoolClass بـ GradeLevel + AcademicYear محملين.
    /// يُستخدم في ClassService بعد Create/Update وفي GetById.
    /// </summary>
    Task<SchoolClass?> GetByIdWithIncludesAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// يجيب كل الـ classes مع GradeLevel + AcademicYear محملين.
    /// يدعم فلترة اختيارية بالـ academicYearId و/أو gradeLevelId.
    /// مُرتَّب تلقائياً بـ LevelOrder ثم Name.
    /// يُستخدم في ClassService.GetAllClassesAsync.
    /// </summary>
    Task<IReadOnlyList<SchoolClass>> GetFilteredWithIncludesAsync(
        int? academicYearId,
        int? gradeLevelId,
        int? status,
        CancellationToken ct = default);
}



