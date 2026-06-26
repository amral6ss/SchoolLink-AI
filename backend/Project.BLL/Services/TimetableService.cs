using AutoMapper;
using Common.Results;
using Microsoft.EntityFrameworkCore;
using Project.BLL.DTOs;
using Project.BLL.DTOs.Notifications;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class TimetableService : ITimetableService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper     _mapper;
    private readonly INotificationService _notificationService;

    private static readonly SchoolDay[] OrderedSchoolDays =
    {
        SchoolDay.Sunday,
        SchoolDay.Monday,
        SchoolDay.Tuesday,
        SchoolDay.Wednesday,
        SchoolDay.Thursday
    };

    public TimetableService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _mapper     = mapper;
        _notificationService = notificationService;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Create / Clone
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<OperationResult<TimetableDto>> CreateTimetableAsync(
        CreateTimetableRequest request,
        CancellationToken ct = default)
    {
        // 1. Validate Class
        var schoolClass = await _unitOfWork.Classes.GetByIdAsync(request.ClassId, ct);
        if (schoolClass is null || schoolClass.IsDeleted)
            return OperationResult<TimetableDto>.Failure("الفصل غير موجود");

        if (schoolClass.Status != ClassStatus.Active)
            return OperationResult<TimetableDto>.Failure("لا يمكن إنشاء جدول لفصل غير نشط");

        // 2. Validate AcademicYear
        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(request.AcademicYearId, ct);
        if (academicYear is null || academicYear.IsDeleted)
            return OperationResult<TimetableDto>.Failure("السنة الدراسية غير موجودة");

        // 3. Optimistic check (تتم في الـ application للسرعة، لكن الـ unique filtered index
        //    على مستوى الـ DB هو مصدر الحقيقة النهائي ضد الـ race conditions).
        var existingTimetables = await _unitOfWork.Timetables
            .GetByClassAndYearAsync(request.ClassId, request.AcademicYearId, ct);
        if (existingTimetables.Any(t => t.Status == TimetableStatus.Draft))
            return OperationResult<TimetableDto>.Failure(
                "توجد بالفعل مسودة جدول لهذا الفصل وهذه السنة الدراسية");

        // 4. Create entity as a draft
        var entity = new Timetable
        {
            ClassId        = request.ClassId,
            AcademicYearId = request.AcademicYearId,
            Status         = TimetableStatus.Draft,
            VersionNumber  = GetNextVersionNumber(existingTimetables)
        };

        await _unitOfWork.Timetables.AddAsync(entity, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // الـ unique filtered index (UX_Timetable_Draft) يضمن عدم وجود مسودتين
            // حتى لو اتنين admins ضغطوا في نفس اللحظة.
            if (IsUniqueViolation(ex))
            {
                return OperationResult<TimetableDto>.Failure(
                    "توجد بالفعل مسودة جدول لهذا الفصل وهذه السنة الدراسية");
            }
            return OperationResult<TimetableDto>.Failure("تعذر إنشاء الجدول الدراسي.");
        }

        // 5. Reload with Class navigation; slots are empty for a new draft
        var withClass = await _unitOfWork.Timetables.GetWithClassAndAllSlotsAsync(entity.Id, ct);

        return OperationResult<TimetableDto>.Success(
            _mapper.Map<TimetableDto>(withClass),
            "تم إنشاء الجدول الدراسي بنجاح");
    }

    public async Task<OperationResult<TimetableDto>> CloneDraftTimetableAsync(
        int classId,
        int academicYearId,
        bool replaceExisting = false,
        CancellationToken ct = default)
    {
        var schoolClass = await _unitOfWork.Classes.GetByIdAsync(classId, ct);
        if (schoolClass is null || schoolClass.IsDeleted)
            return OperationResult<TimetableDto>.Failure("الفصل غير موجود");

        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(academicYearId, ct);
        if (academicYear is null || academicYear.IsDeleted)
            return OperationResult<TimetableDto>.Failure("السنة الدراسية غير موجودة");

        var existingTimetables = await _unitOfWork.Timetables
            .GetByClassAndYearWithDetailsAsync(classId, academicYearId, ct);

        var hasExistingDraft = existingTimetables.Any(t => t.Status == TimetableStatus.Draft);

        // لو فيه مسودة موجودة وما طلبناش الاستبدال → نفس السلوك القديم (آمن افتراضيًا).
        if (hasExistingDraft && !replaceExisting)
            return OperationResult<TimetableDto>.Failure(
                "توجد بالفعل مسودة جدول لهذا الفصل وهذه السنة الدراسية");

        // اختيار المصدر للنسخ. ملاحظة: لو replaceExisting=true فالمسودة القديمة هتتسوفت
        // ديليت جوه الـ transaction، فلازم نختار المصدر من الجداول «قبل» الحذف، ونتجنّب
        // اختيار المسودة القديمة نفسها كمصدر (نفضّل الجدول المنشور).
        var source = existingTimetables
            .Where(t => t.Status == TimetableStatus.Active)
            .OrderByDescending(t => t.PublishedAt ?? t.CreatedAt)
            .FirstOrDefault();

        // fallback: لو مفيش جدول منشور، استخدم أحدث مسودة (قبل حذفها) كمصدر
        source ??= existingTimetables
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault();

        if (source is null)
            return OperationResult<TimetableDto>.Failure(
                "لا يوجد جدول سابق يمكن إنشاء مسودة منه لهذا الفصل وهذه السنة الدراسية");

        // نسخ بيانات المصدر قبل أي عملية كتابة (source من AsNoTracking query — detached).
        var sourceSlots = source.Slots
            .Where(s => !s.IsDeleted)
            .Select(slot => new TimetableSlot
            {
                DayOfWeek = slot.DayOfWeek,
                PeriodNumber = slot.PeriodNumber,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                ClassSubjectTeacherId = slot.IsBreak ? null : slot.ClassSubjectTeacherId,
                IsBreak = slot.IsBreak,
                RoomId = slot.IsBreak ? null : slot.RoomId
            })
            .ToList();

        var draft = new Timetable
        {
            ClassId = classId,
            AcademicYearId = academicYearId,
            Status = TimetableStatus.Draft,
            VersionNumber = GetNextVersionNumber(existingTimetables),
            SourceTimetableId = source.Id
        };

        // ── إصلاح جذري: كل العملية atomic في transaction واحدة ──
        // قبل الإصلاح كان المسودة بيتحفظ الأول (SaveChanges) بره transaction، فلو فشل
        // نسخ الحصص بعد كده، كانت المسودة الفارغة تفضل عالقة في الـ DB → يسبب:
        //   1) «مسودة فاضية» بدل نسخة كاملة.
        //   2) «unexpected error» (استثناء غير متوقع من فشل غير atomic).
        //   3) «توجد بالفعل مسودة» في المحاولة التالية (المسودة الفارغة ما اتمسحتش).
        //   دلوقتي: لو أي خطوة فشلت → rollback كامل، ما يبقاش أي أثر.
        //
        // ── استبدال المسودة (replaceExisting=true): ──
        //   لو فيه مسودة قديمة، بنسوفت-ديليتها (هي + حصصها) أولًا جوه نفس الـ transaction،
        //   وبكده نعدّي القيد UX_Timetable_Draft (اللي بيستثني IsDeleted=1) ونعمل مسودة جديدة.
        int draftId;
        try
        {
            draftId = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (hasExistingDraft)
                {
                    await _unitOfWork.Timetables.SoftDeleteDraftsByClassAndYearAsync(
                        classId, academicYearId, ct);
                }

                await _unitOfWork.Timetables.AddAsync(draft, ct);
                await _unitOfWork.SaveChangesAsync(ct); // يحصل draft.Id

                if (sourceSlots.Count > 0)
                {
                    foreach (var slot in sourceSlots)
                        slot.TimetableId = draft.Id;

                    await _unitOfWork.TimetableSlots.AddRangeAsync(sourceSlots, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                }
                return draft.Id;
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return OperationResult<TimetableDto>.Failure(
                "توجد بالفعل مسودة جدول لهذا الفصل وهذه السنة الدراسية");
        }

        var cloned = await _unitOfWork.Timetables.GetWithClassAndAllSlotsAsync(draftId, ct);
        return OperationResult<TimetableDto>.Success(
            _mapper.Map<TimetableDto>(cloned),
            hasExistingDraft
                ? "تم استبدال المسودة السابقة بنسخة جديدة من الجدول الحالي"
                : "تم إنشاء مسودة جديدة بنسخ الجدول الحالي بنجاح");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Read
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<OperationResult<TimetableDto>> GetTimetableByIdAsync(
        int timetableId,
        CancellationToken ct = default)
    {
        var timetable = await _unitOfWork.Timetables.GetWithClassAndAllSlotsAsync(timetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult<TimetableDto>.Failure("الجدول الدراسي غير موجود", 404);

        return OperationResult<TimetableDto>.Success(
            _mapper.Map<TimetableDto>(timetable),
            "تم جلب الجدول الدراسي بنجاح");
    }

    public async Task<OperationResult<IEnumerable<TimetableDto>>> GetTimetablesByClassAndYearAsync(
        int classId, int academicYearId, CancellationToken ct = default)
    {
        var schoolClass = await _unitOfWork.Classes.GetByIdAsync(classId, ct);
        if (schoolClass is null || schoolClass.IsDeleted)
            return OperationResult<IEnumerable<TimetableDto>>.Failure("الفصل غير موجود", 404);

        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(academicYearId, ct);
        if (academicYear is null || academicYear.IsDeleted)
            return OperationResult<IEnumerable<TimetableDto>>.Failure("السنة الدراسية غير موجودة", 404);

        var timetables = await _unitOfWork.Timetables
            .GetByClassAndYearWithDetailsAsync(classId, academicYearId, ct);

        return OperationResult<IEnumerable<TimetableDto>>.Success(
            _mapper.Map<IEnumerable<TimetableDto>>(timetables),
            "تم جلب الجداول الدراسية بنجاح");
    }

    public async Task<OperationResult<TimetableDto>> GetByClassAsync(
        int classId, int academicYearId, CancellationToken ct = default)
    {
        var timetable = await _unitOfWork.Timetables
            .GetActiveByClassAndYearAsync(classId, academicYearId, ct);
        if (timetable is null)
            return OperationResult<TimetableDto>.Failure(
                "لا يوجد جدول دراسي مفعل لهذا الفصل وهذه السنة الدراسية", 404);

        var full = await _unitOfWork.Timetables
            .GetWithClassAndAllSlotsAsync(timetable.Id, ct);

        return OperationResult<TimetableDto>.Success(
            _mapper.Map<TimetableDto>(full),
            "تم جلب الجدول الدراسي بنجاح");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Validate / Activate / Deactivate / Delete
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<OperationResult<TimetableValidationResultDto>> ValidateTimetableAsync(
        int timetableId, CancellationToken ct = default)
    {
        var validation = await BuildValidationResultAsync(timetableId, ct);
        if (validation is null)
            return OperationResult<TimetableValidationResultDto>.Failure("الجدول الدراسي غير موجود", 404);

        return OperationResult<TimetableValidationResultDto>.Success(
            validation,
            validation.CanActivate
                ? "الجدول جاهز للتفعيل"
                : "تم العثور على ملاحظات تحتاج مراجعة قبل التفعيل");
    }

    public async Task<OperationResult> ActivateTimetableAsync(
        int timetableId, CancellationToken ct = default)
    {
        var timetable = await _unitOfWork.Timetables.GetByIdAsync(timetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult.Failure("الجدول الدراسي غير موجود.", 404);

        if (timetable.Status == TimetableStatus.Active)
            return OperationResult.Success("الجدول الدراسي منشور بالفعل.");

        if (timetable.Status == TimetableStatus.Archived)
            return OperationResult.Failure("لا يمكن نشر نسخة مؤرشفة مباشرة. أنشئ مسودة جديدة منها ثم راجعها وانشرها.");

        var validation = await BuildValidationResultAsync(timetableId, ct);
        if (validation is null)
            return OperationResult.Failure("الجدول الدراسي غير موجود.", 404);
        if (!validation.CanActivate)
        {
            var reasons = validation.Errors
                .Take(3)
                .Select(i => i.Message)
                .ToList();
            var suffix = validation.Errors.Count > 3 ? "..." : string.Empty;
            return OperationResult.Failure(
                $"لا يمكن نشر الجدول قبل معالجة التعارضات والملاحظات الحرجة: {string.Join(" | ", reasons)}{suffix}");
        }

        var existingTimetables = await _unitOfWork.Timetables
            .GetByClassAndYearAsync(timetable.ClassId, timetable.AcademicYearId, ct);
        var now = DateTime.UtcNow;
        var versionNumber = timetable.VersionNumber > 0
            ? timetable.VersionNumber
            : GetNextVersionNumber(existingTimetables);

        try
        {
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _unitOfWork.Timetables.DeactivateByClassAndYearAsync(
                    timetable.ClassId, timetable.AcademicYearId, ct);

                timetable.Status        = TimetableStatus.Active;
                timetable.VersionNumber = versionNumber;
                timetable.PublishedAt   = now;
                timetable.ArchivedAt    = null;
                timetable.UpdatedAt     = now;
                _unitOfWork.Timetables.Update(timetable);
                await _unitOfWork.SaveChangesAsync(ct);
            }, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return OperationResult.Failure(
                "حدث تعارض أثناء النشر، قد يكون هناك جدول آخر نُشر في نفس اللحظة. حدّث الصفحة وحاول مرة أخرى.");
        }

        // Notification: إشعار الطلاب بتحديث الجدول (بره الـ transaction لتبقى صغيرة).
        // فشل الإشعار لا يلغي التفعيل — نسجّل التحذير فقط.
        await NotifyScheduleChangedSafelyAsync(timetable.ClassId, "تم نشر جدول دراسي جديد للفصل، يرجى مراجعة الجدول.");

        return OperationResult.Success("تم نشر الجدول الدراسي بنجاح.");
    }

    public async Task<OperationResult> DeactivateTimetableAsync(
        int timetableId, CancellationToken ct = default)
    {
        var timetable = await _unitOfWork.Timetables.GetByIdAsync(timetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult.Failure("الجدول الدراسي غير موجود.", 404);

        if (timetable.Status != TimetableStatus.Active)
            return OperationResult.Success("الجدول الدراسي غير منشور بالفعل.");

        timetable.Status     = TimetableStatus.Archived;
        timetable.ArchivedAt = DateTime.UtcNow;
        timetable.UpdatedAt  = DateTime.UtcNow;

        _unitOfWork.Timetables.Update(timetable);
        await _unitOfWork.SaveChangesAsync(ct);

        // إصلاح: إشعار الطلاب أن الجدول لم يعد منشورًا (كان مفقودًا).
        await NotifyScheduleChangedSafelyAsync(
            timetable.ClassId,
            "تم إلغاء نشر الجدول الدراسي مؤقتًا. سيظهر للطلاب آخر جدول منشور متاح بعد إعادة النشر.");

        return OperationResult.Success("تم إلغاء نشر الجدول الدراسي بنجاح.");
    }

    public async Task<OperationResult> DeleteTimetableAsync(
        int timetableId, CancellationToken ct = default)
    {
        var timetable = await _unitOfWork.Timetables.GetByIdAsync(timetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult.Failure("الجدول الدراسي غير موجود.", 404);

        if (timetable.Status == TimetableStatus.Active)
            return OperationResult.Failure("لا يمكن حذف جدول منشور. ألغِ النشر أولًا إذا كنت تريد إزالته.");

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var slots = await _unitOfWork.TimetableSlots.GetByTimetableIdAsync(timetableId, ct);
            _unitOfWork.TimetableSlots.SoftDeleteRange(slots);
            _unitOfWork.Timetables.SoftDelete(timetable);
            await _unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return OperationResult.Success("تم حذف الجدول الدراسي بنجاح.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Slots CRUD
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<OperationResult<TimetableSlotDto>> AddTimetableSlotAsync(
        AddTimetableSlotRequest request, CancellationToken ct = default)
    {
        // 1. Validate Timetable
        var timetable = await _unitOfWork.Timetables.GetByIdAsync(request.TimetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult<TimetableSlotDto>.Failure("الجدول الدراسي غير موجود", 404);
        if (timetable.Status != TimetableStatus.Draft)
            return OperationResult<TimetableSlotDto>.Failure(
                "لا يمكن تعديل هذا الجدول مباشرة. التعديل مسموح فقط على المسودة.");

        var effectiveClassSubjectTeacherId = request.IsBreak ? null : request.ClassSubjectTeacherId;
        var effectiveRoomId                = request.IsBreak ? null : request.RoomId;

        // 2. Time range validation (أولًا لأنها رخيصة ولا تحتاج استعلام).
        if (request.EndTime <= request.StartTime)
            return OperationResult<TimetableSlotDto>.Failure(
                "وقت النهاية يجب أن يكون بعد وقت البداية");

        // 3. Non-break: تحميل التعيين + التحقق من انتمائه للفصل/السنة الصحيحين.
        ClassSubjectTeacher? cst = null;
        if (!request.IsBreak)
        {
            if (effectiveClassSubjectTeacherId is null)
                return OperationResult<TimetableSlotDto>.Failure(
                    "يجب تحديد المادة والمعلم قبل حفظ الحصة.");

            cst = await _unitOfWork.ClassSubjectTeachers
                .GetByIdAsync(effectiveClassSubjectTeacherId.Value, ct);
            if (cst is null || cst.IsDeleted)
                return OperationResult<TimetableSlotDto>.Failure("تعيين المادة/المعلم غير موجود.");

            if (cst.ClassId != timetable.ClassId)
                return OperationResult<TimetableSlotDto>.Failure(
                    "تعيين المادة/المعلم تابع لفصل مختلف عن جدول هذا الفصل.");
        }

        // 4. كشف موحّد لكل التعارضات.
        //    HARD BLOCK (خلية مشغولة/قاعة محذوفة) → منع الحفظ.
        //    SOFT WARNING (تعارض قاعة/معلم مع جدول آخر) → نسمح بالحفظ ونعرض التحذير،
        //    لأن الفحص القاطع بيحصل وقت التفعيل (Activate).
        var conflict = await ResolveSlotConflictsAsync(
            timetable.Id, timetable.ClassId, timetable.AcademicYearId,
            request.DayOfWeek, request.PeriodNumber,
            request.IsBreak, cst, effectiveRoomId,
            excludeSlotId: null, ct);

        if (conflict.IsBlocked)
            return OperationResult<TimetableSlotDto>.Failure(conflict.BlockReason!);

        // 5. Create slot
        var slot = new TimetableSlot
        {
            TimetableId           = request.TimetableId,
            DayOfWeek             = request.DayOfWeek,
            PeriodNumber          = request.PeriodNumber,
            StartTime             = request.StartTime,
            EndTime               = request.EndTime,
            IsBreak               = request.IsBreak,
            ClassSubjectTeacherId = effectiveClassSubjectTeacherId,
            RoomId                = effectiveRoomId
        };

        // 6. Persist (مع catch لانتهاكات الـ unique constraints بسبب الـ race conditions).
        await _unitOfWork.TimetableSlots.AddAsync(slot, ct);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return OperationResult<TimetableSlotDto>.Failure(
                "حدث تعارض أثناء الحفظ (يبدو أن قاعة أو معلمًا حُجزا في نفس اللحظة). " +
                "أعد المحاولة بعد تحديث الصفحة، أو اختر قاعة/معلمًا/حصة أخرى.");
        }

        var withDetails = await _unitOfWork.TimetableSlots.GetByIdWithDetailsAsync(slot.Id, ct);

        // لو فيه تحذير ناعم، نرجّعه في رسالة النجاح (العملية نجحت فعليًا).
        return OperationResult<TimetableSlotDto>.Success(
            _mapper.Map<TimetableSlotDto>(withDetails),
            conflict.Warning ?? "تمت إضافة الحصة بنجاح");
    }

    public async Task<OperationResult<TimetableSlotDto>> UpdateTimetableSlotAsync(
        UpdateTimetableSlotRequest request, CancellationToken ct = default)
    {
        var slot = await _unitOfWork.TimetableSlots.GetByIdAsync(request.SlotId, ct);
        if (slot is null || slot.IsDeleted)
            return OperationResult<TimetableSlotDto>.Failure("الحصة غير موجودة", 404);

        var timetable = await _unitOfWork.Timetables.GetByIdAsync(slot.TimetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult<TimetableSlotDto>.Failure("الجدول الدراسي غير موجود", 404);
        if (timetable.Status != TimetableStatus.Draft)
            return OperationResult<TimetableSlotDto>.Failure(
                "لا يمكن تعديل هذا الجدول مباشرة. التعديل مسموح فقط على المسودة.");

        var effectiveClassSubjectTeacherId = request.IsBreak ? null : request.ClassSubjectTeacherId;
        var effectiveRoomId                = request.IsBreak ? null : request.RoomId;

        // 2. Time range validation (أولًا لأنها رخيصة ولا تحتاج استعلام).
        if (request.EndTime <= request.StartTime)
            return OperationResult<TimetableSlotDto>.Failure(
                "وقت النهاية يجب أن يكون بعد وقت البداية");

        // 3. Non-break: تحميل التعيين + التحقق من انتمائه للفصل/السنة الصحيحين.
        ClassSubjectTeacher? cst = null;
        if (!request.IsBreak)
        {
            if (effectiveClassSubjectTeacherId is null)
                return OperationResult<TimetableSlotDto>.Failure(
                    "يجب تحديد المادة والمعلم قبل حفظ الحصة.");

            cst = await _unitOfWork.ClassSubjectTeachers
                .GetByIdAsync(effectiveClassSubjectTeacherId.Value, ct);
            if (cst is null || cst.IsDeleted)
                return OperationResult<TimetableSlotDto>.Failure("تعيين المادة/المعلم غير موجود.");

            if (cst.ClassId != timetable.ClassId)
                return OperationResult<TimetableSlotDto>.Failure(
                    "تعيين المادة/المعلم تابع لفصل مختلف عن جدول هذا الفصل.");
        }

        // 4. كشف موحّد لكل التعارضات (يستثني الحصة الحالية بـ slot.Id).
        //    HARD BLOCK → منع، SOFT WARNING → نسمح ونعرض التحذير.
        var conflict = await ResolveSlotConflictsAsync(
            timetable.Id, timetable.ClassId, timetable.AcademicYearId,
            request.DayOfWeek, request.PeriodNumber,
            request.IsBreak, cst, effectiveRoomId,
            excludeSlotId: slot.Id, ct);

        if (conflict.IsBlocked)
            return OperationResult<TimetableSlotDto>.Failure(conflict.BlockReason!);

        // 5. Apply updates
        slot.DayOfWeek             = request.DayOfWeek;
        slot.PeriodNumber          = request.PeriodNumber;
        slot.StartTime             = request.StartTime;
        slot.EndTime               = request.EndTime;
        slot.IsBreak               = request.IsBreak;
        slot.ClassSubjectTeacherId = effectiveClassSubjectTeacherId;
        slot.RoomId                = effectiveRoomId;
        slot.UpdatedAt             = DateTime.UtcNow;

        _unitOfWork.TimetableSlots.Update(slot);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return OperationResult<TimetableSlotDto>.Failure(
                "حدث تعارض أثناء الحفظ (يبدو أن قاعة أو معلمًا حُجزا في نفس اللحظة). " +
                "أعد المحاولة بعد تحديث الصفحة، أو اختر قاعة/معلمًا/حصة أخرى.");
        }

        var withDetails = await _unitOfWork.TimetableSlots.GetByIdWithDetailsAsync(slot.Id, ct);

        return OperationResult<TimetableSlotDto>.Success(
            _mapper.Map<TimetableSlotDto>(withDetails),
            conflict.Warning ?? "تم تحديث الحصة بنجاح");
    }

    public async Task<OperationResult> DeleteTimetableSlotAsync(
        int slotId, CancellationToken ct = default)
    {
        var slot = await _unitOfWork.TimetableSlots.GetByIdAsync(slotId, ct);
        if (slot is null || slot.IsDeleted)
            return OperationResult.Failure("الحصة غير موجودة", 404);

        var timetable = await _unitOfWork.Timetables.GetByIdAsync(slot.TimetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult.Failure("الجدول الدراسي غير موجود", 404);
        if (timetable.Status != TimetableStatus.Draft)
            return OperationResult.Failure(
                "لا يمكن حذف حصة من هذا الجدول مباشرة. التعديل مسموح فقط على المسودة.");

        _unitOfWork.TimetableSlots.SoftDelete(slot);
        await _unitOfWork.SaveChangesAsync(ct);

        return OperationResult.Success("تم حذف الحصة بنجاح");
    }

    /// <summary>
    /// يحدّث توقيت كل الحصص (slots) برقم حصة معيّن داخل الجدول دفعة واحدة.
    ///
    /// تصميم:
    ///   - يعمل فقط على المسودات (مش الجداول المنشورة) — نفس قاعدة باقي عمليات slots.
    ///   - التحقق من نطاق الوقت قبل أي استعلام (رخيص).
    ///   - يفلتر الـ slots الموجودة برقم الحصة، يحدّث التوقيت، ويحفظ في transaction واحدة.
    ///   - لو مفيش slots بالرقم ده → نجاح بلا تغيير (التوقيت اتسجل في الـ grid للجلسة فقط).
    /// </summary>
    public async Task<OperationResult> UpdatePeriodTimingAsync(
        int timetableId,
        UpdatePeriodTimingRequest request,
        CancellationToken ct = default)
    {
        // 1. Validate timetable exists + is a draft.
        var timetable = await _unitOfWork.Timetables.GetByIdAsync(timetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return OperationResult.Failure("الجدول الدراسي غير موجود", 404);
        if (timetable.Status != TimetableStatus.Draft)
            return OperationResult.Failure(
                "لا يمكن تعديل توقيت الحصص إلا في المسودة.");

        // 2. Time range validation (رخيص — أولًا).
        if (request.EndTime <= request.StartTime)
            return OperationResult.Failure("وقت النهاية يجب أن يكون بعد وقت البداية");

        // 3. اجلب كل الـ slots في الجدول، وفلتر برقم الحصة.
        var allSlots = await _unitOfWork.TimetableSlots.GetByTimetableIdAsync(timetableId, ct);
        var targetSlots = allSlots
            .Where(s => !s.IsDeleted && s.PeriodNumber == request.PeriodNumber)
            .ToList();

        // 4. No-overlap validation: بما إن التوقيت موحّد لكل الأيام، نأخذ أول slot لكل
        //    period كعينة، ونفحص إن التوقيت الجديد لا يتداخل مع أي حصة أخرى.
        //    التداخل: الفترتان [start,end) تشتركان في أي دقيقة.
        var periodSamples = allSlots
            .Where(s => !s.IsDeleted && s.PeriodNumber != request.PeriodNumber)
            .GroupBy(s => s.PeriodNumber)
            .Select(g => g.First())
            .ToList();

        foreach (var sample in periodSamples)
        {
            if (TimeRangesOverlap(request.StartTime, request.EndTime, sample.StartTime, sample.EndTime))
            {
                return OperationResult.Failure(
                    $"توقيت الحصة يتداخل مع {GetPeriodLabelArabic(sample.PeriodNumber)} " +
                    $"({sample.StartTime:HH:mm} - {sample.EndTime:HH:mm}). " +
                    "اضبط التوقيت حتى لا يتداخل مع الحصص الأخرى.");
            }
        }

        // ملاحظة: التوقيت الموحّد لا يسبب تعارضات يوم/حصة لأنها نفس الـ slots الموجودة
        // (مش بنضيف جديد). كما إن تعارضات القاعة/المعلم في الـ backend معتمدة على
        // (RoomId/TeacherId, Day, PeriodNumber) — والتوقيت ما بيأثرش عليها.

        if (targetSlots.Count == 0)
        {
            // مفيش slots بهذا الرقم → التوقيت اتسجل في الـ grid للجلسة فقط. نجاح صامت.
            return OperationResult.Success("تم تحديث توقيت الحصة. لا توجد حصص مجدولة بهذا الرقم حاليًا.");
        }

        // 5. طبّق التوقيت الجديد على كل slot في transaction واحدة.
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var slot in targetSlots)
            {
                slot.StartTime = request.StartTime;
                slot.EndTime   = request.EndTime;
                slot.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.TimetableSlots.Update(slot);
            }
            await _unitOfWork.SaveChangesAsync(ct);
        }, ct);

        return OperationResult.Success(
            $"تم تحديث توقيت {targetSlots.Count} حصة بنجاح");
    }

    /// <summary>
    /// يفحص تداخل فترتين زمنيتين [startA,endA) و [startB,endB).
    /// التداخل = يوجد لحظة واحدة مشتركة بين الفترتين (باستثناء التلامس عند الحدود).
    /// مثال متداخل: 08:00-08:45 و 08:30-09:15 → true.
    /// مثال متلامس: 08:00-08:45 و 08:45-09:30 → false (الحصة التانية تبدأ حيث انتهت الأولى).
    /// </summary>
    private static bool TimeRangesOverlap(TimeOnly startA, TimeOnly endA, TimeOnly startB, TimeOnly endB)
        => startA < endB && startB < endA;

    // ══════════════════════════════════════════════════════════════════════════
    // Student / Teacher / Parent schedules
    // ══════════════════════════════════════════════════════════════════════════

    public async Task<OperationResult<TimetableDto>> GetByStudentAsync(
        int enrollmentId, CancellationToken ct = default)
    {
        var enrollment = await _unitOfWork.StudentEnrollments.GetByIdAsync(enrollmentId, ct);
        if (enrollment is null || enrollment.IsDeleted)
            return OperationResult<TimetableDto>.Failure("تسجيل الطالب غير موجود", 404);

        var timetable = await _unitOfWork.Timetables.GetActiveByClassAndYearAsync(
            enrollment.ClassId, enrollment.AcademicYearId, ct);
        if (timetable is null)
            return OperationResult<TimetableDto>.Failure(
                "لا يوجد جدول دراسي مفعل لهذا الفصل وهذه السنة الدراسية", 404);

        var full = await _unitOfWork.Timetables
            .GetWithClassAndAllSlotsAsync(timetable.Id, ct);

        return OperationResult<TimetableDto>.Success(
            _mapper.Map<TimetableDto>(full),
            "تم جلب جدول الطالب بنجاح");
    }

    public async Task<OperationResult<TimetableDto>> GetByStudentForUserAsync(
        int enrollmentId, int userId, CancellationToken ct = default)
    {
        var enrollment = await _unitOfWork.StudentEnrollments.GetWithStudentAsync(enrollmentId, ct);
        if (enrollment is null || enrollment.IsDeleted)
            return OperationResult<TimetableDto>.Failure("تسجيل الطالب غير موجود", 404);

        var student = enrollment.Student;
        if (student.UserId != userId)
        {
            var linked = await _unitOfWork.ParentStudents.GetByParentAndStudentAsync(userId, student.Id, ct);
            if (linked is null)
                return OperationResult<TimetableDto>.Failure("غير مسموح بالوصول لهذا الجدول", 403);
        }

        var timetable = await _unitOfWork.Timetables.GetActiveByClassAndYearAsync(
            enrollment.ClassId, enrollment.AcademicYearId, ct);
        if (timetable is null)
            return OperationResult<TimetableDto>.Failure(
                "لا يوجد جدول دراسي مفعل لهذا الفصل وهذه السنة الدراسية", 404);

        var full = await _unitOfWork.Timetables
            .GetWithClassAndAllSlotsAsync(timetable.Id, ct);

        return OperationResult<TimetableDto>.Success(
            _mapper.Map<TimetableDto>(full),
            "تم جلب جدول الطالب بنجاح");
    }

    public async Task<OperationResult<TimetableDto>> GetMyStudentScheduleAsync(
        int studentUserId, int academicYearId, CancellationToken ct = default)
    {
        var student = await _unitOfWork.Students.GetByUserIdAsync(studentUserId, ct);
        if (student is null || student.IsDeleted)
            return OperationResult<TimetableDto>.Failure("الطالب غير موجود", 404);

        var enrollment = await _unitOfWork.StudentEnrollments
            .GetActiveByStudentAndYearAsync(student.Id, academicYearId, ct);
        if (enrollment is null)
            return OperationResult<TimetableDto>.Failure(
                "لا يوجد تسجيل نشط لهذا الطالب في هذه السنة الدراسية", 404);

        return await GetByStudentAsync(enrollment.Id, ct);
    }

    public async Task<OperationResult<IEnumerable<ChildScheduleDto>>> GetMyChildSchedulesAsync(
        int parentUserId, int academicYearId, CancellationToken ct = default)
    {
        // إصلاح N+1: نجيب كل بيانات الأبناء + التسجيلات + الجداول النشطة في استعلامات
        // قليلة بدل loop فيه 3 query لكل ابن.
        var links = await _unitOfWork.ParentStudents
            .GetWithStudentDetailsByParentAsync(parentUserId, ct);

        var validLinks = links.Where(l => !l.IsDeleted && l.Student is not null).ToList();
        if (validLinks.Count == 0)
            return OperationResult<IEnumerable<ChildScheduleDto>>.Success(
                Enumerable.Empty<ChildScheduleDto>(),
                "تم جلب جداول الأبناء بنجاح");

        var studentIds = validLinks.Select(l => l.StudentId).ToList();

        // استعلام واحد لكل التسجيلات النشطة للأبناء في السنة
        var enrollments = await _unitOfWork.StudentEnrollments
            .GetActiveByStudentsAndYearAsync(studentIds, academicYearId, ct);

        if (enrollments.Count == 0)
            return OperationResult<IEnumerable<ChildScheduleDto>>.Success(
                Enumerable.Empty<ChildScheduleDto>(),
                "تم جلب جداول الأبناء بنجاح");

        var classIds = enrollments.Select(e => e.ClassId).Distinct().ToList();

        // استعلام واحد لكل الجداول النشطة لهذه الفصول
        var activeTimetables = (await Task.WhenAll(
                classIds.Select(cid => _unitOfWork.Timetables
                    .GetActiveByClassAndYearAsync(cid, academicYearId, ct))))
            .Where(t => t is not null)
            .Cast<Project.Domain.Entities.Timetable>()
            .ToList();

        if (activeTimetables.Count == 0)
            return OperationResult<IEnumerable<ChildScheduleDto>>.Success(
                Enumerable.Empty<ChildScheduleDto>(),
                "تم جلب جداول الأبناء بنجاح");

        var timetableIds = activeTimetables.Select(t => t.Id).ToList();

        // حمّل تفاصيل كل جدول (slots + class) دفعة واحدة عبر whenall
        var fullTimetables = (await Task.WhenAll(
                timetableIds.Select(tid => _unitOfWork.Timetables
                    .GetWithClassAndAllSlotsAsync(tid, ct))))
            .Where(t => t is not null)
            .Cast<Project.Domain.Entities.Timetable>()
            .ToList();

        var timetableByClass = fullTimetables.ToDictionary(t => t.ClassId);
        var studentById = validLinks.ToDictionary(l => l.StudentId, l => l.Student!);

        var schedules = new List<ChildScheduleDto>();
        foreach (var enrollment in enrollments)
        {
            if (!timetableByClass.TryGetValue(enrollment.ClassId, out var full))
                continue;
            if (!studentById.TryGetValue(enrollment.StudentId, out var student))
                continue;

            var dto = _mapper.Map<ChildScheduleDto>(full);
            dto.StudentId   = student.Id;
            dto.StudentName = student.FullName;
            schedules.Add(dto);
        }

        return OperationResult<IEnumerable<ChildScheduleDto>>.Success(
            schedules,
            "تم جلب جداول الأبناء بنجاح");
    }

    public async Task<OperationResult<IEnumerable<TeacherScheduleSlotDto>>> GetTeacherScheduleAsync(
        int teacherId, int academicYearId, CancellationToken ct = default)
    {
        var teacher = await _unitOfWork.Users.GetByIdAsync(teacherId, ct);
        if (teacher is null || teacher.IsDeleted)
            return OperationResult<IEnumerable<TeacherScheduleSlotDto>>.Failure("المعلم غير موجود", 404);
        if (teacher.Role != UserRole.Teacher)
            return OperationResult<IEnumerable<TeacherScheduleSlotDto>>.Failure("المستخدم ليس معلما");

        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(academicYearId, ct);
        if (academicYear is null || academicYear.IsDeleted)
            return OperationResult<IEnumerable<TeacherScheduleSlotDto>>.Failure("السنة الدراسية غير موجودة", 404);

        var slots = await _unitOfWork.TimetableSlots.GetTeacherScheduleAsync(teacherId, academicYearId, ct);

        // إصلاح: استخدام إسقاط واضح مع كل الحقول المطلوبة (IsBreak كان مفقودًا سابقًا).
        var result = slots.Select(slot => new TeacherScheduleSlotDto
        {
            Id                    = slot.Id,
            TimetableId           = slot.TimetableId,
            ClassId               = slot.Timetable.ClassId,
            ClassName             = slot.Timetable.Class.Name,
            DayOfWeek             = slot.DayOfWeek.ToString(),
            PeriodNumber          = slot.PeriodNumber,
            StartTime             = slot.StartTime,
            EndTime               = slot.EndTime,
            IsBreak               = slot.IsBreak,
            ClassSubjectTeacherId = slot.ClassSubjectTeacherId,
            SubjectName           = slot.ClassSubjectTeacher?.Subject.Name,
            RoomName              = slot.Room?.Name
        });

        return OperationResult<IEnumerable<TeacherScheduleSlotDto>>.Success(
            result,
            "تم جلب جدول المعلم بنجاح");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Validation engine
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<TimetableValidationResultDto?> BuildValidationResultAsync(
        int timetableId, CancellationToken ct = default)
    {
        var timetable = await _unitOfWork.Timetables.GetWithClassAndAllSlotsAsync(timetableId, ct);
        if (timetable is null || timetable.IsDeleted)
            return null;

        var assignments = await _unitOfWork.ClassSubjectTeachers.GetByClassWithAllDetailsAsync(
            timetable.ClassId, timetable.AcademicYearId, ct);

        var slots = timetable.Slots
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.DayOfWeek)
            .ThenBy(s => s.PeriodNumber)
            .ToList();

        var result = new TimetableValidationResultDto
        {
            TimetableId = timetable.Id,
            TotalSlots = slots.Count,
            LessonSlots = slots.Count(s => !s.IsBreak),
            BreakSlots = slots.Count(s => s.IsBreak)
        };

        if (!slots.Any())
        {
            result.Errors.Add(new TimetableValidationIssueDto
            {
                Severity = "Error",
                Code = "EMPTY_TIMETABLE",
                Message = "لا يمكن تفعيل جدول بدون أي حصص أو فترات راحة."
            });
        }

        // ── إصلاح N+1: استعلام واحد لكل تعارضات الغرف ضد الجداول المنشورة ──
        var roomCandidates = slots
            .Where(s => !s.IsBreak && s.RoomId.HasValue)
            .Select(s => (s.RoomId!.Value, s.DayOfWeek, s.PeriodNumber))
            .ToList();

        var roomConflicts = roomCandidates.Count > 0
            ? (await _unitOfWork.TimetableSlots.GetRoomConflictsAgainstActiveAsync(
                roomCandidates, timetable.Id, timetable.ClassId, timetable.AcademicYearId, ct))
                .ToHashSet()
            : new HashSet<(int, SchoolDay, int)>();

        // ── إصلاح: كشف تعارضات المعلمين ضد الجداول المنشورة (كانت مفقودة) ──
        var teacherCandidates = slots
            .Where(s => !s.IsBreak && s.ClassSubjectTeacher is not null)
            .Select(s => (s.ClassSubjectTeacher!.TeacherId, s.DayOfWeek, s.PeriodNumber))
            .ToList();

        var teacherConflicts = teacherCandidates.Count > 0
            ? (await _unitOfWork.TimetableSlots.GetTeacherConflictsAgainstActiveAsync(
                teacherCandidates, timetable.Id, timetable.ClassId, timetable.AcademicYearId, ct))
                .ToHashSet()
            : new HashSet<(int, SchoolDay, int)>();

        foreach (var slot in slots)
        {
            if (slot.EndTime <= slot.StartTime)
            {
                result.Errors.Add(BuildIssue("INVALID_TIME_RANGE", "Error",
                    $"الحصة في {slot.DayOfWeek} رقم {slot.PeriodNumber} لديها وقت نهاية غير صالح.",
                    slot));
            }

            if (slot.IsBreak)
            {
                if (slot.ClassSubjectTeacherId.HasValue || slot.RoomId.HasValue)
                {
                    result.Errors.Add(BuildIssue("BREAK_HAS_HIDDEN_DATA", "Error",
                        $"فترة الراحة في {slot.DayOfWeek} رقم {slot.PeriodNumber} تحتوي على بيانات مادة أو قاعة ويجب تنظيفها.",
                        slot));
                }
                continue;
            }

            if (!slot.ClassSubjectTeacherId.HasValue || slot.ClassSubjectTeacher is null)
            {
                result.Errors.Add(BuildIssue("LESSON_WITHOUT_ASSIGNMENT", "Error",
                    $"توجد حصة دراسية في {slot.DayOfWeek} رقم {slot.PeriodNumber} بدون مادة/معلم صالح.",
                    slot));
                continue;
            }

            if (slot.ClassSubjectTeacher.ClassId != timetable.ClassId ||
                slot.ClassSubjectTeacher.AcademicYearId != timetable.AcademicYearId)
            {
                result.Errors.Add(BuildIssue("ASSIGNMENT_OUTSIDE_CLASS", "Error",
                    $"الحصة في {slot.DayOfWeek} رقم {slot.PeriodNumber} مرتبطة بتعيين من فصل أو سنة مختلفة.",
                    slot));
            }

            // تعارض القاعة (batch)
            if (slot.RoomId.HasValue &&
                roomConflicts.Contains((slot.RoomId.Value, slot.DayOfWeek, slot.PeriodNumber)))
            {
                result.Errors.Add(BuildIssue("ROOM_CONFLICT", "Error",
                    $"الغرفة '{slot.Room?.Name ?? slot.RoomId.Value.ToString()}' محجوزة في {slot.DayOfWeek} للحصة رقم {slot.PeriodNumber}.",
                    slot));
            }

            // تعارض المعلم (batch) — جديد
            if (slot.ClassSubjectTeacher is not null &&
                teacherConflicts.Contains((slot.ClassSubjectTeacher.TeacherId, slot.DayOfWeek, slot.PeriodNumber)))
            {
                result.Errors.Add(BuildIssue("TEACHER_CONFLICT", "Error",
                    $"المعلم '{slot.ClassSubjectTeacher.Teacher?.FullName ?? slot.ClassSubjectTeacher.TeacherId.ToString()}' لديه حصة في جدول منشور آخر في {slot.DayOfWeek} للحصة رقم {slot.PeriodNumber}.",
                    slot));
            }
        }

        // ── كشف تداخل الأوقات (time-range overlaps) داخل نفس اليوم لكل معلم ──
        var teacherSlotsByDay = slots
            .Where(s => !s.IsBreak && s.ClassSubjectTeacher is not null)
            .GroupBy(s => (s.ClassSubjectTeacher!.TeacherId, s.DayOfWeek));

        foreach (var group in teacherSlotsByDay)
        {
            var ordered = group.OrderBy(s => s.StartTime).ToList();
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var a = ordered[i];
                var b = ordered[i + 1];
                if (a.EndTime > b.StartTime) // تداخل
                {
                    result.Warnings.Add(BuildIssue("TIME_OVERLAP", "Warning",
                        $"تداخل في الأوقات بمقدار {Math.Round((a.EndTime - b.StartTime).TotalMinutes)} دقيقة بين حصتين للمعلم في {a.DayOfWeek}.",
                        b));
                }
            }
        }

        // ── تحقق التغطية الأسبوعية لكل تعيين ──
        foreach (var assignment in assignments)
        {
            var scheduledCount = slots.Count(s => !s.IsBreak && s.ClassSubjectTeacherId == assignment.Id);
            if (scheduledCount < assignment.WeeklyPeriods)
            {
                result.Warnings.Add(new TimetableValidationIssueDto
                {
                    Severity = "Warning",
                    Code = "ASSIGNMENT_UNDER_SCHEDULED",
                    Message = $"المادة '{assignment.Subject?.Name ?? assignment.SubjectId.ToString()}' للمعلم '{assignment.Teacher?.FullName ?? assignment.TeacherId.ToString()}' تحتاج {assignment.WeeklyPeriods - scheduledCount} حصة إضافية.",
                    ClassSubjectTeacherId = assignment.Id
                });
            }

            if (scheduledCount > assignment.WeeklyPeriods)
            {
                result.Errors.Add(new TimetableValidationIssueDto
                {
                    Severity = "Error",
                    Code = "ASSIGNMENT_OVER_SCHEDULED",
                    Message = $"المادة '{assignment.Subject?.Name ?? assignment.SubjectId.ToString()}' مجدولة أكثر من العدد الأسبوعي المسموح به.",
                    ClassSubjectTeacherId = assignment.Id
                });
            }
        }

        foreach (var day in OrderedSchoolDays)
        {
            var daySlots = slots.Where(s => s.DayOfWeek == day).ToList();
            if (!daySlots.Any())
            {
                result.Warnings.Add(new TimetableValidationIssueDto
                {
                    Severity = "Warning",
                    Code = "EMPTY_DAY",
                    Message = $"اليوم {day} لا يحتوي على أي حصص أو فترات راحة."
                });
            }
        }

        result.MissingAssignmentsCount       = result.Warnings.Count(w => w.Code == "ASSIGNMENT_UNDER_SCHEDULED");
        result.OverScheduledAssignmentsCount = result.Errors.Count(e => e.Code == "ASSIGNMENT_OVER_SCHEDULED");
        result.CanActivate = result.Errors.Count == 0;

        return result;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private static TimetableValidationIssueDto BuildIssue(
        string code, string severity, string message, TimetableSlot slot)
        => new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            SlotId = slot.Id,
            DayOfWeek = slot.DayOfWeek.ToString(),
            PeriodNumber = slot.PeriodNumber,
            ClassSubjectTeacherId = slot.ClassSubjectTeacherId
        };

    /// <summary>
    /// يرجع الاسم العربي لليوم الدراسي ليظهر في رسائل الخطأ الموجّهة للمستخدم.
    /// </summary>
    private static string GetDayLabelArabic(SchoolDay day) => day switch
    {
        SchoolDay.Sunday    => "الأحد",
        SchoolDay.Monday    => "الإثنين",
        SchoolDay.Tuesday   => "الثلاثاء",
        SchoolDay.Wednesday => "الأربعاء",
        SchoolDay.Thursday  => "الخميس",
        _ => day.ToString()
    };

    /// <summary>
    /// يرجع ترتيبًا عربيًا لرقم الحصة (الأولى/الثانية/...).
    /// </summary>
    private static string GetPeriodLabelArabic(int periodNumber) => periodNumber switch
    {
        1  => "الحصة الأولى",
        2  => "الحصة الثانية",
        3  => "الحصة الثالثة",
        4  => "الحصة الرابعة",
        5  => "الحصة الخامسة",
        6  => "الحصة السادسة",
        7  => "الحصة السابعة",
        8  => "الحصة الثامنة",
        9  => "الحصة التاسعة",
        10 => "الحصة العاشرة",
        _  => $"الحصة رقم {periodNumber}"
    };

    /// <summary>
    /// يبني رسالة سياق (يوم + حصة) موحّدة لكل رسائل التعارض لتوفير التكرار.
    /// </summary>
    private static string BuildDayPeriodContext(SchoolDay day, int periodNumber)
        => $"{GetDayLabelArabic(day)} - {GetPeriodLabelArabic(periodNumber)}";

    /// <summary>
    /// نتيجة فحص تعارض الحصة قبل الحفظ.
    /// </summary>
    /// <param name="BlockReason">
    /// سبب المنع القاطع (hard block) — يعني المستحيلات: خلية مشغولة، قاعة محذوفة...
    /// null لو لا يوجد منع.
    /// </param>
    /// <param name="Warning">
    /// تحذير ناعم (soft warning) — تعارض محتمل مع جدول آخر منشور/مسودة.
    /// بيُسمح بالحفظ معه، لكن يُعرض للمستخدم ليصحّحه قبل النشر.
    /// null لو لا يوجد تحذير.
    /// </param>
    private sealed record SlotConflictResult(string? BlockReason, string? Warning)
    {
        public static readonly SlotConflictResult Ok = new(null, null);
        public bool IsBlocked => BlockReason is not null;
        public static SlotConflictResult Blocked(string reason) => new(reason, null);
        public static SlotConflictResult WithWarning(string warning) => new(null, warning);
    }

    /// <summary>
    /// كشف موحّد لكل أنواع تعارض الحصة قبل الحفظ.
    ///
    /// نموذج Draft → Publish: أثناء بناء المسودة نسمح بالتعارضات بين الجداول
    /// (قاعة/معلم محجوز في جدول آخر) ونعرضها كـ soft warning، عشان المدير يكمّل
    /// البناء بحرية. الفحص القاطع (hard) بيحصل وقت التفعيل (Activate).
    ///
    /// الفحوصات:
    ///   - HARD BLOCK: خلية مشغولة داخل نفس الجدول، قاعة/تعيين محذوف.
    ///   - SOFT WARNING: تعارض قاعة/معلم ضد جدول آخر (منشور أو مسودة).
    /// </summary>
    private async Task<SlotConflictResult> ResolveSlotConflictsAsync(
        int timetableId,
        int classId,
        int academicYearId,
        SchoolDay day,
        int periodNumber,
        bool isBreak,
        ClassSubjectTeacher? cst,
        int? roomId,
        int? excludeSlotId,
        CancellationToken ct)
    {
        var context = BuildDayPeriodContext(day, periodNumber);

        // 1) HARD BLOCK: تعارض يوم/حصة داخل نفس الجدول — الخلية مشغولة فعليًا.
        //    ده مستحيل يتعايش (مفيش مكان لخليتين في نفس الخانة داخل جدول الفصل).
        if (await _unitOfWork.TimetableSlots.HasConflictAsync(
                timetableId, day, periodNumber, excludeSlotId ?? 0, ct))
        {
            return SlotConflictResult.Blocked(
                $"الخلية في {context} مشغولة بحصة أخرى في نفس الجدول. " +
                "احذف الحصة الموجودة أو اختر خلية فارغة.");
        }

        // فترات الراحة لا تحتاج فحص معلم/قاعة.
        if (isBreak) return SlotConflictResult.Ok;

        string? warning = null;

        // 2) القاعة: HARD لو محذوفة، SOFT لو متعارضة مع جدول آخر.
        if (roomId.HasValue)
        {
            var room = await _unitOfWork.Rooms.GetByIdAsync(roomId.Value, ct);
            if (room is null || room.IsDeleted)
                return SlotConflictResult.Blocked("القاعة المختارة غير موجودة أو محذوفة. اختر قاعة أخرى.");

            var roomConflict = await _unitOfWork.TimetableSlots.GetRoomConflictAcrossAllAsync(
                roomId.Value, day, periodNumber, timetableId, classId, academicYearId, excludeSlotId, ct);

            if (roomConflict is not null)
            {
                var status = roomConflict.IsOtherDraft ? "مسودة" : "منشور";
                var who = roomConflict.SubjectName is not null
                    ? $" لحصة {roomConflict.SubjectName}" +
                      (roomConflict.TeacherName is not null ? $" ({roomConflict.TeacherName})" : "")
                    : "";
                // تحذير ناعم: المستخدم ممكن يكمل، بس لازم يصلّحه قبل النشر.
                warning = $"القاعة «{room.Name}» محجوزة في {context} لصالح الفصل " +
                          $"«{roomConflict.ClassName}» ({status}){who}. " +
                          "يمكنك الحفظ الآن، لكن لن تتمكن من نشر الجدول حتى تزيل هذا التعارض.";
            }
        }

        // 3) المعلم: SOFT لو متعارض مع جدول آخر.
        if (cst is not null)
        {
            var teacherConflict = await _unitOfWork.TimetableSlots
                .GetTeacherConflictClassNameAcrossAllAsync(
                    cst.TeacherId, cst.AcademicYearId, day, periodNumber,
                    timetableId, classId, academicYearId, excludeSlotId, ct);

            if (teacherConflict is not null)
            {
                var teacherWarning = $"تعارض محتمل في {context}: {teacherConflict}. " +
                                     "يمكنك الحفظ الآن، لكن لن تتمكن من نشر الجدول حتى تزيل هذا التعارض.";
                warning = warning is null ? teacherWarning : $"{warning}\n{teacherWarning}";
            }
        }

        return warning is null ? SlotConflictResult.Ok : SlotConflictResult.WithWarning(warning);
    }

    private static int GetNextVersionNumber(IEnumerable<Timetable> timetables)
    {
        var maxVersion = timetables
            .Where(t => !t.IsDeleted)
            .Select(t => t.VersionNumber)
            .DefaultIfEmpty(0)
            .Max();

        return maxVersion + 1;
    }

    /// <summary>
    /// يكشف ما إذا كان الاستثناء ناتجًا عن انتهاك قيد فريد (unique constraint/index)
    /// عبر مزوّد SQL Server أو غيره.
    /// </summary>
    /// <remarks>
    /// SQL Server بيرمي رقمين مختلفين:
    ///   - <c>2627</c> = انتهاك <b>unique constraint</b> (PRIMARY/UNIQUE constraint).
    ///   - <c>2601</c> = انتهاك <b>unique index</b> (اللي بتنشئه EF عبر HasIndex().IsUnique()).
    /// الاتنين لازم يتعامل معاهم؛ غير كده كان بيوصل لرسالة غامضة بدل الرسالة الواضحة.
    /// </remarks>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
            return true;

        // PostgreSQL/SQLite fallback (message-based) للأمان
        var msg = ex.InnerException?.Message ?? ex.Message ?? string.Empty;
        return msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    private async Task NotifyScheduleChangedSafelyAsync(int classId, string body)
    {
        try
        {
            var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
            if (classEntity is null) return;

            var enrollments = await _unitOfWork.StudentEnrollments
                .GetActiveByClassAsync(classId, classEntity.AcademicYearId);
            var studentIds = enrollments
                .Where(e => e.Student != null && e.Student.UserId != null)
                .Select(e => e.Student.UserId!.Value)
                .Distinct()
                .ToList();

            if (studentIds.Count == 0) return;

            await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
            {
                UserIds = studentIds,
                Title = "تحديث الجدول الدراسي",
                Body = body,
                Type = NotificationType.ScheduleChanged
            });
        }
        catch
        {
            // فشل الإشعار لا يجب أن يلغي العملية الأساسية (outbox-style: نتجاهل بأمان هنا).
        }
    }
}
