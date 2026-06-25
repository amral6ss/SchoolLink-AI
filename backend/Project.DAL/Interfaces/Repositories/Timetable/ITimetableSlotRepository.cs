using Project.Domain.Entities;
using Project.Domain.Enums;
using System.Linq.Expressions;

namespace Project.DAL.Interfaces.Repositories.Timetable;

/// <summary>
/// معلومات تعارض قاعة (تُرجع من GetRoomConflictAcrossAllAsync) لبناء رسالة خطأ محددة.
/// </summary>
public sealed class RoomConflictInfo
{
    public string ClassName      { get; set; } = string.Empty;
    public string? SubjectName   { get; set; }
    public string? TeacherName   { get; set; }
    public bool   IsOtherDraft   { get; set; }
    public TimetableStatus Status { get; set; }
}

public interface ITimetableSlotRepository : IRepository<TimetableSlot>
{
    Task<IReadOnlyList<TimetableSlot>> GetByTimetableIdAsync(int timetableId, CancellationToken ct = default);
    Task<IReadOnlyList<TimetableSlot>> GetNonBreakByTimetableAsync(int timetableId, CancellationToken ct = default);
    Task<IReadOnlyList<TimetableSlot>> GetBreaksByTimetableAsync(int timetableId, CancellationToken ct = default);

    Task<IReadOnlyList<TimetableSlot>> GetByDayAsync(int timetableId, SchoolDay day, CancellationToken ct = default);
    Task<IReadOnlyList<TimetableSlot>> GetByDayWithDetailsAsync(int timetableId, SchoolDay day, CancellationToken ct = default);

    Task<IReadOnlyList<TimetableSlot>> GetByClassSubjectTeacherIdAsync(int classSubjectTeacherId, CancellationToken ct = default);

    Task<IReadOnlyList<TimetableSlot>> GetTeacherScheduleAsync(int teacherId, int academicYearId, CancellationToken ct = default);

    Task<bool> HasConflictAsync(int timetableId, SchoolDay day, int periodNumber, CancellationToken ct = default);
    Task<bool> HasConflictAsync(int timetableId, SchoolDay day, int periodNumber, int excludedSlotId, CancellationToken ct = default);

    Task<bool> HasTeacherConflictAsync(int teacherId, int academicYearId, SchoolDay day, int periodNumber, CancellationToken ct = default);
    Task<bool> HasTeacherConflictAsync(int teacherId, int academicYearId, SchoolDay day, int periodNumber, int excludedSlotId, CancellationToken ct = default);

    /// <summary>
    /// كشف تعارض المعلم ضد كل الجداول (منشورة + مسودات) ما عدا الجدول الحالي.
    /// يرجع اسم الفصل اللي محجوز فيه المعلم لبناء رسالة خطأ واضحة.
    /// </summary>
    Task<string?> GetTeacherConflictClassNameAcrossAllAsync(
        int teacherId,
        int academicYearId,
        SchoolDay day,
        int periodNumber,
        int excludeTimetableId,
        int excludeClassId,
        int excludeAcademicYearId,
        int? excludeSlotId,
        CancellationToken ct = default);

    Task<bool> HasRoomConflictAsync(int roomId, SchoolDay day, int periodNumber, CancellationToken ct = default);
    Task<bool> HasRoomConflictAsync(int roomId, SchoolDay day, int periodNumber, int excludedSlotId, CancellationToken ct = default);
    Task<bool> HasRoomConflictAgainstActiveTimetablesAsync(
        int roomId,
        SchoolDay day,
        int periodNumber,
        int timetableId,
        CancellationToken ct = default);

    /// <summary>
    /// كشف تعارض القاعة ضد كل الجداول (منشورة + مسودات) ما عدا الجدول الحالي.
    /// يرجع معلومات التعارض (اسم الفصل/المعلم) عشان الـ service يبني رسالة واضحة.
    /// مفتاح: (TeacherId) يكون null لو القاعة محجوزة بحصة دراسية عادية.
    /// </summary>
    Task<RoomConflictInfo?> GetRoomConflictAcrossAllAsync(
        int roomId,
        SchoolDay day,
        int periodNumber,
        int excludeTimetableId,
        int excludeClassId,
        int excludeAcademicYearId,
        int? excludeSlotId,
        CancellationToken ct = default);

    /// <summary>
    /// يستعلم عن كل تعارضات الغرف للـ slots المُعطاة ضد الجداول المنشورة الأخرى في query واحدة
    /// (بديل N+1). يرجع قائمة بالـ (RoomId, DayOfWeek, PeriodNumber) المتعارضة.
    /// </summary>
    Task<IReadOnlyList<(int RoomId, SchoolDay DayOfWeek, int PeriodNumber)>> GetRoomConflictsAgainstActiveAsync(
        IEnumerable<(int RoomId, SchoolDay DayOfWeek, int PeriodNumber)> candidates,
        int excludeTimetableId,
        int excludeClassId,
        int excludeAcademicYearId,
        CancellationToken ct = default);

    /// <summary>
    /// يستعلم عن كل تعارضات المعلمين للـ slots المُعطاة ضد الجداول المنشورة الأخرى في query واحدة
    /// (للتحقق وقت المراجعة). يرجع قائمة بالـ (TeacherId, DayOfWeek, PeriodNumber) المتعارضة.
    /// </summary>
    Task<IReadOnlyList<(int TeacherId, SchoolDay DayOfWeek, int PeriodNumber)>> GetTeacherConflictsAgainstActiveAsync(
        IEnumerable<(int TeacherId, SchoolDay DayOfWeek, int PeriodNumber)> candidates,
        int excludeTimetableId,
        int excludeClassId,
        int excludeAcademicYearId,
        CancellationToken ct = default);

    Task BulkReplaceAsync(int timetableId, IEnumerable<TimetableSlot> slots, CancellationToken ct = default);

    /// <summary>
    /// يجيب TimetableSlot بـ ClassSubjectTeacher → Subject + Teacher + Room محملين.
    /// يُستخدم في AddTimetableSlotAsync و UpdateTimetableSlotAsync بعد الـ save
    /// لإرجاع SubjectName و TeacherName و RoomName في الـ DTO.
    /// </summary>
    Task<TimetableSlot?> GetByIdWithDetailsAsync(int slotId, CancellationToken ct = default);
}
