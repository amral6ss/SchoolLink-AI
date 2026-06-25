using Microsoft.EntityFrameworkCore;
using Project.DAL.Context;
using Project.DAL.Interfaces.Repositories.Timetable;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.DAL.Repositories.Timetable;

public class TimetableRepository : Repository<Project.Domain.Entities.Timetable>, ITimetableRepository
{
    public TimetableRepository(AppDbContext context) : base(context) { }

    public async Task<Project.Domain.Entities.Timetable?> GetActiveByClassAndYearAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Timetables
            .AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.ClassId        == classId        &&
                t.AcademicYearId == academicYearId &&
                t.Status         == TimetableStatus.Active, ct);

    public async Task<bool> HasActiveTimetableAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Timetables
            .AsNoTracking()
            .AnyAsync(t =>
                t.ClassId        == classId        &&
                t.AcademicYearId == academicYearId &&
                t.Status         == TimetableStatus.Active, ct);

    public async Task<IReadOnlyList<Project.Domain.Entities.Timetable>> GetByClassAndYearAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Timetables
            .AsNoTracking()
            .Where(t =>
                t.ClassId        == classId        &&
                t.AcademicYearId == academicYearId)
            .OrderBy(t => t.Status == TimetableStatus.Draft ? 0 :
                          t.Status == TimetableStatus.Active ? 1 : 2)
            .ThenByDescending(t => t.VersionNumber)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Project.Domain.Entities.Timetable>> GetByClassAndYearWithDetailsAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
        => await _context.Timetables
            .AsNoTracking()
            .Where(t =>
                t.ClassId        == classId        &&
                t.AcademicYearId == academicYearId)
            .Include(t => t.Class)
            .Include(t => t.Slots.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.ClassSubjectTeacher)
                    .ThenInclude(cst => cst!.Subject)
            .Include(t => t.Slots.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.ClassSubjectTeacher)
                    .ThenInclude(cst => cst!.Teacher)
            .Include(t => t.Slots.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.Room)
            .OrderBy(t => t.Status == TimetableStatus.Draft ? 0 :
                          t.Status == TimetableStatus.Active ? 1 : 2)
            .ThenByDescending(t => t.VersionNumber)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<Project.Domain.Entities.Timetable?> GetWithSlotsAsync(
        int timetableId,
        CancellationToken ct = default)
        => await _context.Timetables
            .AsNoTracking()
            .Include(t => t.Slots.Where(s => !s.IsBreak))
                .ThenInclude(s => s.ClassSubjectTeacher)
                    .ThenInclude(cst => cst!.Subject)
            .Include(t => t.Slots.Where(s => !s.IsBreak))
                .ThenInclude(s => s.ClassSubjectTeacher)
                    .ThenInclude(cst => cst!.Teacher)
            .FirstOrDefaultAsync(t => t.Id == timetableId, ct);

    public async Task DeactivateByClassAndYearAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _context.Timetables
            .Where(t =>
                t.ClassId        == classId        &&
                t.AcademicYearId == academicYearId &&
                t.Status         == TimetableStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TimetableStatus.Archived)
                .SetProperty(t => t.ArchivedAt, now)
                .SetProperty(t => t.UpdatedAt, now), ct);
    }

    public async Task SoftDeleteDraftsByClassAndYearAsync(
        int classId,
        int academicYearId,
        CancellationToken ct = default)
    {
        var draftIds = await _context.Timetables
            .Where(t =>
                t.ClassId        == classId        &&
                t.AcademicYearId == academicYearId &&
                t.Status         == TimetableStatus.Draft &&
                !t.IsDeleted)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (draftIds.Count == 0) return;

        var now = DateTime.UtcNow;

        await _context.TimetableSlots
            .Where(s => draftIds.Contains(s.TimetableId) && !s.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.UpdatedAt, now), ct);

        await _context.Timetables
            .Where(t => draftIds.Contains(t.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.UpdatedAt, now), ct);
    }

    public async Task<Project.Domain.Entities.Timetable?> GetWithClassAndAllSlotsAsync(
        int timetableId,
        CancellationToken ct = default)
        => await _context.Timetables
            .Include(t => t.Class)
            .Include(t => t.Slots.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.ClassSubjectTeacher)
                    .ThenInclude(cst => cst!.Subject)
            .Include(t => t.Slots.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.ClassSubjectTeacher)
                    .ThenInclude(cst => cst!.Teacher)
            .Include(t => t.Slots.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.Room)
            .FirstOrDefaultAsync(t => t.Id == timetableId, ct);
}
