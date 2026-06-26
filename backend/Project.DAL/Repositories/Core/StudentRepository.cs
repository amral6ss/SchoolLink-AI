using Microsoft.EntityFrameworkCore;
using Project.DAL.Interfaces.Repositories.Core;
using Project.Domain.Entities;
using Project.DAL.Context;
using Project.Domain.Enums;

namespace Project.DAL.Repositories.Core;

public class StudentRepository : Repository<Student>, IStudentRepository
{
    public StudentRepository(AppDbContext context) : base(context) { }


    public async Task<Student?> GetByNationalIdAsync(
        string nationalId,
        CancellationToken ct = default)
        => await _context.Students
            .FirstOrDefaultAsync(s => s.NationalId == nationalId, ct);

    public async Task<Student?> GetByUserIdAsync(
        int userId,
        CancellationToken ct = default)
        => await _context.Students
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public async Task<bool> ExistsByNationalIdAsync(
        string nationalId,
        CancellationToken ct = default)
        => await _context.Students
            .AnyAsync(s => s.NationalId == nationalId, ct);


    public async Task<IReadOnlyList<Student>> SearchByNameAsync(
        string query,
        CancellationToken ct = default)
        => await _context.Students
            .Where(s => s.FullName.Contains(query))
            .OrderBy(s => s.FullName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Student>> GetActiveStudentsAsync(
        CancellationToken ct = default)
        => await _context.Students
            .Where(s => s.IsActive && s.LifecycleStatus == StudentLifecycleStatus.Active)
            .OrderBy(s => s.FullName)
            .ToListAsync(ct);


    public async Task<Student?> GetWithCurrentEnrollmentAsync(
        int studentId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Students
            .Include(s => s.Enrollments
                .Where(e => e.AcademicYearId == academicYearId && e.LeftAt == null))
                .ThenInclude(e => e.Class)
                    .ThenInclude(c => c.GradeLevel)
            .FirstOrDefaultAsync(s => s.Id == studentId, ct);

    public async Task<IReadOnlyList<Student>> GetByClassAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Students
            .Where(s => s.Enrollments.Any(e =>
                e.ClassId == classId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null))
            .OrderBy(s => s.FullName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Student>> GetWithoutUserAccountAsync(
        CancellationToken ct = default)
        => await _context.Students
            .Where(s => s.UserId == null && s.IsActive
                        && s.LifecycleStatus == StudentLifecycleStatus.Active)
            .OrderBy(s => s.FullName)
            .ToListAsync(ct);
}



