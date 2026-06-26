using Microsoft.EntityFrameworkCore;
using Project.DAL.Interfaces.Repositories.Core;
using Project.Domain.Entities;
using Project.DAL.Context;
using Project.Domain.Enums;

namespace Project.DAL.Repositories.Core;

public class StudentEnrollmentRepository : Repository<StudentEnrollment>, IStudentEnrollmentRepository
{
    public StudentEnrollmentRepository(AppDbContext context) : base(context) { }


    public async Task<StudentEnrollment?> GetActiveByStudentAndYearAsync(
        int studentId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .FirstOrDefaultAsync(e =>
                e.StudentId == studentId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StudentEnrollment>> GetActiveByStudentsAndYearAsync(
        IReadOnlyCollection<int> studentIds,
        int academicYearId,
        CancellationToken ct = default)
    {
        if (studentIds.Count == 0)
            return Array.Empty<StudentEnrollment>();

        // استعلام واحد لكل التسجيلات النشطة لقائمة طلاب في سنة معينة (إصلاح N+1 في جداول الأبناء).
        return await _context.StudentEnrollments
            .AsNoTracking()
            .Where(e =>
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null &&
                studentIds.Contains(e.StudentId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StudentEnrollment>> GetByClassAndYearAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Where(e => e.ClassId == classId && e.AcademicYearId == academicYearId)
            .Include(e => e.Student)
            .OrderBy(e => e.Student.FullName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StudentEnrollment>> GetActiveByClassAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Where(e =>
                e.ClassId == classId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null)
            .Include(e => e.Student)
            .OrderBy(e => e.Student.FullName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StudentEnrollment>> GetActiveByGradeLevelAndYearWithDetailsAsync(
        int gradeLevelId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Where(e =>
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null &&
                !e.IsDeleted &&
                e.Student != null &&
                !e.Student.IsDeleted &&
                e.Student.IsActive &&
                e.Student.LifecycleStatus == StudentLifecycleStatus.Active &&
                e.Class.GradeLevelId == gradeLevelId &&
                !e.Class.IsDeleted)
            .Include(e => e.Student)
            .Include(e => e.Class)
                .ThenInclude(c => c.GradeLevel)
            .Include(e => e.AcademicYear)
            .OrderBy(e => e.Student.FullName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StudentEnrollment>> GetHistoryByStudentAsync(
        int studentId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Where(e => e.StudentId == studentId)
            .Include(e => e.Class)
                .ThenInclude(c => c.GradeLevel)
            .Include(e => e.AcademicYear)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StudentEnrollment>> GetTransfersHistoryAsync(
        int academicYearId,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Where(e => e.AcademicYearId == academicYearId && e.LeftAt != null)
            .Include(e => e.Student)
            .Include(e => e.Class)
            .OrderByDescending(e => e.LeftAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);


    public async Task<bool> IsEnrolledAsync(
        int studentId,
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .AnyAsync(e =>
                e.StudentId == studentId &&
                e.ClassId == classId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null, ct);

    public async Task<bool> HasActiveEnrollmentAsync(
        int studentId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .AnyAsync(e =>
                e.StudentId == studentId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null, ct);


    public async Task<StudentEnrollment?> GetWithStudentAsync(
        int enrollmentId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);

    public async Task<StudentEnrollment?> GetByIdWithDetailsAsync(
        int enrollmentId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Include(e => e.Student)
            .Include(e => e.Class)
                .ThenInclude(c => c.GradeLevel)
            .Include(e => e.AcademicYear)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);

    public async Task<IReadOnlyList<StudentEnrollment>> GetByIdsWithDetailsAsync(
        IEnumerable<int> enrollmentIds,
        CancellationToken ct = default)
    {
        var ids = enrollmentIds.Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<StudentEnrollment>();

        return await _context.StudentEnrollments
            .Where(e => ids.Contains(e.Id))
            .Include(e => e.Student)
            .Include(e => e.Class)
                .ThenInclude(c => c.GradeLevel)
            .Include(e => e.AcademicYear)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StudentEnrollment>> GetByClassWithStudentAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Where(e =>
                e.ClassId == classId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null)
            .Include(e => e.Student)
            .OrderBy(e => e.Student.FullName)
            .ToListAsync(ct);


    public async Task<int> GetActiveCountByClassAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .CountAsync(e =>
                e.ClassId == classId &&
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null, ct);

    public async Task<IReadOnlyList<StudentEnrollment>> GetActiveEnrollmentsByYearAsync(
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .Where(e =>
                e.AcademicYearId == academicYearId &&
                e.LeftAt == null &&
                !e.IsDeleted &&
                e.Student != null &&
                !e.Student.IsDeleted)
            .Include(e => e.Student)
            .Include(e => e.Class)
            .OrderBy(e => e.Student.FullName)
            .ToListAsync(ct);

    public async Task<int> GetTransfersCountAsync(
        int academicYearId,
        CancellationToken ct = default)
        => await _context.StudentEnrollments
            .CountAsync(e =>
                e.AcademicYearId == academicYearId &&
                e.LeftAt != null, ct);

    public async Task<(IReadOnlyList<Domain.Entities.Student> Students, int TotalCount)> GetUnenrolledStudentsAsync(
        int academicYearId,
        string? searchTerm,
        DateOnly? birthDateFrom,
        DateOnly? birthDateTo,
        string? sortBy,
        bool sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.Students
            .Where(s =>
                !s.IsDeleted &&
                s.IsActive &&
                s.LifecycleStatus == StudentLifecycleStatus.Active &&
                !_context.StudentEnrollments.Any(e =>
                    e.StudentId == s.Id &&
                    e.AcademicYearId == academicYearId &&
                    e.LeftAt == null &&
                    !e.IsDeleted))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
            query = query.Where(s => s.FullName.Contains(searchTerm));

        if (birthDateFrom.HasValue)
            query = query.Where(s => s.BirthDate.HasValue && s.BirthDate.Value >= birthDateFrom.Value);

        if (birthDateTo.HasValue)
            query = query.Where(s => s.BirthDate.HasValue && s.BirthDate.Value <= birthDateTo.Value);

        var totalCount = await query.CountAsync(ct);

        query = sortBy?.ToLower() switch
        {
            "birthdate" => sortDescending
                ? query.OrderByDescending(s => s.BirthDate)
                : query.OrderBy(s => s.BirthDate),
            _ => sortDescending
                ? query.OrderByDescending(s => s.FullName)
                : query.OrderBy(s => s.FullName)
        };

        var students = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (students, totalCount);
    }
}
