using Microsoft.EntityFrameworkCore;
using Project.DAL.Interfaces.Repositories.Core;
using Project.Domain.Entities;
using Project.DAL.Context;

namespace Project.DAL.Repositories.Core;

public class SchoolClassRepository : Repository<SchoolClass>, ISchoolClassRepository
{
    public SchoolClassRepository(AppDbContext context) : base(context) { }


    public async Task<IReadOnlyList<SchoolClass>> GetByGradeLevelAndYearAsync(
        int gradeLevelId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Classes
            .Where(c => c.GradeLevelId == gradeLevelId && c.AcademicYearId == academicYearId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SchoolClass>> GetByAcademicYearAsync(
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Classes
            .Where(c => c.AcademicYearId == academicYearId)
            .Include(c => c.GradeLevel)
            .OrderBy(c => c.GradeLevel.LevelOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);


    public async Task<SchoolClass?> GetByNameGradeLevelAndYearAsync(
        string name,
        int gradeLevelId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Classes
            .FirstOrDefaultAsync(c =>
                c.Name == name &&
                c.GradeLevelId == gradeLevelId &&
                c.AcademicYearId == academicYearId, ct);

    public async Task<bool> ExistsByNameGradeLevelAndYearAsync(
        string name,
        int gradeLevelId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Classes
            .AnyAsync(c =>
                c.Name == name &&
                c.GradeLevelId == gradeLevelId &&
                c.AcademicYearId == academicYearId, ct);


    public async Task<int> GetEnrollmentCountAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .CountAsync(e =>
                e.ClassId == classId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null, ct);

    public async Task<IReadOnlyList<SchoolClass>> GetWithEnrollmentCountAsync(
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Classes
            .Where(c => c.AcademicYearId == academicYearId)
            .Include(c => c.GradeLevel)
            .Include(c => c.Enrollments
                .Where(e => e.LeftAt == null))
            .OrderBy(c => c.GradeLevel.LevelOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);


    public async Task<SchoolClass?> GetByIdWithIncludesAsync(
        int id,
        CancellationToken ct = default)
        => await _context.Classes
            .Include(c => c.GradeLevel)
            .Include(c => c.AcademicYear)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<SchoolClass>> GetFilteredWithIncludesAsync(
        int? academicYearId,
        int? gradeLevelId,
        int? status,
        CancellationToken ct = default)
    {
        var query = _context.Classes
            .Where(c => !c.IsDeleted)
            .Include(c => c.GradeLevel)
            .Include(c => c.AcademicYear)
            .AsQueryable();

        if (academicYearId.HasValue)
            query = query.Where(c => c.AcademicYearId == academicYearId.Value);

        if (gradeLevelId.HasValue)
            query = query.Where(c => c.GradeLevelId == gradeLevelId.Value);

        if (status.HasValue)
            query = query.Where(c => (int)c.Status == status.Value);

        return await query
            .OrderBy(c => c.GradeLevel.LevelOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);
    }
}



