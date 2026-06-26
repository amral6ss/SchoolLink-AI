import {
  Component,
  OnInit,
  OnDestroy,
  ChangeDetectionStrategy,
  DestroyRef,
  inject,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { FocusTrap } from '../../core/directives/focus-trap';
import {
  TimetableService,
  TimetableListItem,
  TimetableDto as Timetable,
  TimetableValidationResult,
  TimetableValidationIssue,
} from '../../core/services/timetable.service';
import { ClassService, ClassEntity } from '../../core/services/class.service';
import { AcademicYearService, AcademicYear } from '../../core/services/academic-year.service';
import { ClassSubjectTeacherService, ClassSubjectTeacher } from '../../core/services/class-subject-teacher.service';
import { RoomService, Room } from '../../core/services/room.service';
import { SubjectService, Subject } from '../../core/services/subject.service';
import { UserService, User } from '../../core/services/user.service';
import { GradeLevelService, GradeLevel } from '../../core/services/grade-level.service';
import { TimetableSlotDto } from '../../core/models/timetable.models';

/** الشكل الداخلي للحصة أثناء التحرير في المودال */
interface SlotDraft {
  timetableId: number;
  dayOfWeek: string;
  periodNumber: number;
  startTime: string;
  endTime: string;
  classSubjectTeacherId: number | null;
  isBreak: boolean;
  roomId: number | null;
}

interface DayDef { value: string; label: string; }
interface PeriodDef { num: number; label: string; start: string; end: string; }
type TimetableStatusName = 'Draft' | 'Active' | 'Archived';
type TimetablesFilter = 'all' | 'active' | 'draft' | 'archived';

@Component({
  selector: 'app-admin-schedule',
  standalone: true,
  imports: [CommonModule, FormsModule, Sidebar, FocusTrap],
  templateUrl: './admin-schedule.html',
  styleUrl: './admin-schedule.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSchedule implements OnInit, OnDestroy {
  // ── UI panel toggles ──────────────────────────────────────────────────────
  sidebarOpen = signal(false);
  assignmentBankOpen = signal(false);
  timetablesPickerOpen = signal(false);
  actionsMenuOpen = signal(false);
  timetablesFilter = signal<TimetablesFilter>('all');

  // ── Reference / lookup data ───────────────────────────────────────────────
  academicYears = signal<AcademicYear[]>([]);
  classes = signal<ClassEntity[]>([]);
  grades = signal<GradeLevel[]>([]);
  subjects = signal<Subject[]>([]);
  teachers = signal<User[]>([]);

  selectedYearId = signal<number | null>(null);
  selectedGradeId = signal<number | null>(null);
  selectedClassId = signal<number | null>(null);

  /** الفصول المفلترة حسب الصف الدراسي المختار */
  readonly filterClasses = computed(() => {
    const gradeId = this.selectedGradeId();
    if (!gradeId) return [];
    return this.classes().filter(c => c.gradeLevelId === gradeId);
  });

  // ── Timetable / assignment state ──────────────────────────────────────────
  allTimetables = signal<Timetable[]>([]);
  selectedTimetableId = signal<number | null>(null);
  currentTimetable = signal<Timetable | null>(null);
  assignments = signal<ClassSubjectTeacher[]>([]);
  availableRooms = signal<Room[]>([]);

  /**
   * slotMap: lookup O(1) للخلية بمفتاح `${day}-${period}`.
   * يُعاد بناؤه تلقائيًا عند تغيّر الجدول الحالي (computed) بدل linear scan × 35.
   */
  readonly slotMap = computed(() => {
    const map = new Map<string, TimetableSlotDto>();
    for (const s of this.currentTimetable()?.slots ?? []) {
      map.set(`${s.dayOfWeek}-${s.periodNumber}`, s);
    }
    return map;
  });

  /** assignmentMap: lookup O(1) للتعيين بمفتاح id */
  readonly assignmentMap = computed(() => {
    const map = new Map<number, ClassSubjectTeacher>();
    for (const a of this.assignments()) {
      if (a.id != null) map.set(a.id, a);
    }
    return map;
  });

  /** usedPeriodsMap: عدد الحصص المسكّنة لكل تعيين — O(1) بعد الحساب */
  readonly usedPeriodsMap = computed(() => {
    const map = new Map<number, number>();
    const slots = this.currentTimetable()?.slots ?? [];
    for (const s of slots) {
      if (!s.isBreak && s.classSubjectTeacherId != null) {
        map.set(s.classSubjectTeacherId, (map.get(s.classSubjectTeacherId) ?? 0) + 1);
      }
    }
    return map;
  });

  // ── Loading / status signals ─────────────────────────────────────────────
  isLoading              = signal(false);
  isSaving               = signal(false);
  isPublishing           = signal(false);
  isCloning              = signal(false);
  isValidating           = signal(false);
  isDeletingTimetable    = signal(false);
  isDeactivating         = signal(false);

  errorMessage   = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  validationResult = signal<TimetableValidationResult | null>(null);

  // ── Inline confirmation dialogs ──────────────────────────────────────────
  deleteSlotConfirmOpen      = signal(false);
  deleteTimetableConfirmOpen = signal(false);
  deactivateConfirmOpen      = signal(false);
  replaceDraftConfirmOpen    = signal(false);

  // ── Schedule editor / slot modal ──────────────────────────────────────────
  scheduleEditorOpen = signal(false);
  isModalOpen        = signal(false);
  isEditingSlot      = signal(false);
  isReadOnlyModal    = signal(false);
  editingSlotId      = signal<number | null>(null);
  selectedInspection = signal<{ dayValue: string; periodNum: number; slot: TimetableSlotDto | null } | null>(null);
  slotFormError      = signal<string | null>(null);
  roomsLoading       = signal(false);

  /** قاعات بديلة مقترحة بعد التعارض (لعرضها للمستخدم في لوحة الاقتراحات) */
  alternativeRooms = signal<Room[]>([]);

  newSlot = signal<SlotDraft>(this.emptySlotDraft());

  assignmentSubjectFilter = signal('');
  assignmentTeacherFilter = signal('');

  /** الأيام — معلومات ثابتة افتراضية، يفضّل لاحقًا جلبها من الباك إند */
  days: DayDef[] = [
    { value: 'Sunday',    label: 'الأحد' },
    { value: 'Monday',    label: 'الإثنين' },
    { value: 'Tuesday',   label: 'الثلاثاء' },
    { value: 'Wednesday', label: 'الأربعاء' },
    { value: 'Thursday',  label: 'الخميس' },
  ];

  /**
   * شبكة التوقيت الموحّدة (Time Grid) — مصدر الحقيقة الواحد لتوقيت كل حصة.
   *
   * التصميم: توقيت كل حصة بيتحدد مرة واحدة هنا (من رأس الجدول) ويتطبّق على كل
   * أيام الأسبوع تلقائيًا. ده يطابق الواقع المصري (جرس موحّد للمدرسة) ولا يوجد فيه
   * أيام استثنائية.
   *
   * عدد الحصص قابل للتعديل من الرأس: إضافة/حذف عمود كامل (يضيف/يشيل كل خلاياه).
   *
   * ملاحظة معمارية: الـ backend بيخزّن StartTime/EndTime على مستوى كل slot، لكن
   * الـ frontend هنا بيتعامل مع الـ grid كمصدر وحيد، وعند حفظ/تعديل أي حصة بيبعت
   * التوقيت الجاي من الـ definition ده. (Phase 1a: تغيير frontend فقط.)
   */
  periods = signal<PeriodDef[]>([
    { num: 1, label: 'الحصة الأولى',   start: '08:00:00', end: '08:45:00' },
    { num: 2, label: 'الحصة الثانية',  start: '08:45:00', end: '09:30:00' },
    { num: 3, label: 'الحصة الثالثة',  start: '09:30:00', end: '10:15:00' },
    { num: 4, label: 'الحصة الرابعة',  start: '10:30:00', end: '11:15:00' },
    { num: 5, label: 'الحصة الخامسة',  start: '11:15:00', end: '12:00:00' },
    { num: 6, label: 'الحصة السادسة',  start: '12:00:00', end: '12:45:00' },
    { num: 7, label: 'الحصة السابعة',  start: '12:45:00', end: '13:30:00' },
  ]);

  /** حدود شبكة التوقيت (آمنة ومتوافقة مع Range(1,20) في الـ backend) */
  readonly MIN_PERIODS = 1;
  readonly MAX_PERIODS = 10;
  /** مدة الحصة الافتراضية بالدقائق (للحصص المُضافة تلقائيًا) */
  readonly DEFAULT_PERIOD_MINUTES = 45;

  // ── تحرير شبكة التوقيت من رأس الجدول ───────────────────────────────────────
  /** رقم الحصة قيد التحرير حاليًا في رأس الجدول (null = لا يوجد تحرير) */
  editingPeriod = signal<number | null>(null);
  /** قيم مؤقتة أثناء تحرير توقيت الحصة */
  editPeriodStart = signal<string>('');
  editPeriodEnd = signal<string>('');
  /** خطأ تحقق وقت أثناء تحرير التوقيت (للعرض inline أسفل الحقول) */
  editPeriodError = signal<string | null>(null);
  /** هل يجري حفظ توقيت حصة (لإظهار spinner على زرار الحفظ ومنع التحرير المزدوج) */
  isSavingPeriod = signal(false);
  /** snapshot للتوقيت القديم قبل الحفظ (للـ rollback لو فشل الـ backend) */
  private periodEditSnapshot: { num: number; start: string; end: string } | null = null;

  /** الحصول على تعريف الحصة برقمها من الـ grid (مصدر الحقيقة للتوقيت) */
  getPeriod(num: number): PeriodDef | undefined {
    return this.periods().find(p => p.num === num);
  }

  /**
   * عدد الحصص (slots) المخزّنة في الجدول الحالي لكل رقم حصة.
   * يُستخدم لمنع حذف عمود به حصص مجدولة.
   */
  readonly slotsCountByPeriod = computed(() => {
    const map = new Map<number, number>();
    for (const s of this.currentTimetable()?.slots ?? []) {
      map.set(s.periodNumber, (map.get(s.periodNumber) ?? 0) + 1);
    }
    return map;
  });

  // ── Private fields ────────────────────────────────────────────────────────
  private messageTimers = new Map<'success' | 'error', ReturnType<typeof setTimeout>>();
  private resizeHandler: (() => void) | undefined;

  // ── Service injections ────────────────────────────────────────────────────
  private timetableService = inject(TimetableService);
  private classService = inject(ClassService);
  private academicYearService = inject(AcademicYearService);
  private assignmentService = inject(ClassSubjectTeacherService);
  private roomService = inject(RoomService);
  private subjectService = inject(SubjectService);
  private userService = inject(UserService);
  private gradeLevelService = inject(GradeLevelService);
  private destroyRef = inject(DestroyRef);
  private route = inject(ActivatedRoute);

  /** فلتر مبدئي جاي من query params (مثلاً من زرار "عرض في الجدول" في صفحة التعيينات) */
  private pendingPreselect: { classId: number | null; yearId: number | null; gradeId: number | null } | null = null;
  /** عدد طلبات التحميل الأولية اللي لسه ما خلصتش (subjects/teachers/grades/classes/years) */
  private initialLoadsPending = 5;

  // ══════════════════════════════════════════════════════════════════════════
  // Lifecycle
  // ══════════════════════════════════════════════════════════════════════════

  ngOnInit(): void {
    this.readPreselectFromRoute();
    this.loadInitialData();

    // إغلاق بنك الحصص تلقائيًا على الشاشات الصغيرة لتفادي التداخل
    this.resizeHandler = () => {
      if (window.innerWidth < 1024 && this.assignmentBankOpen()) {
        this.assignmentBankOpen.set(false);
      }
    };
    window.addEventListener('resize', this.resizeHandler);
  }

  /** قراءة classId/yearId/gradeId من query params لو موجودة (deep link من صفحات تانية) */
  private readPreselectFromRoute(): void {
    const params = this.route.snapshot.queryParamMap;
    const classId = Number(params.get('classId'));
    const yearId = Number(params.get('yearId'));
    const gradeId = Number(params.get('gradeId'));
    if (classId || yearId || gradeId) {
      this.pendingPreselect = {
        classId: classId || null,
        yearId: yearId || null,
        gradeId: gradeId || null,
      };
    }
  }

  /** يتنادى من كل استدعاء تحميل أولي (next أو error) في loadInitialData() */
  private markInitialLoadDone(): void {
    this.initialLoadsPending = Math.max(0, this.initialLoadsPending - 1);
    if (this.initialLoadsPending === 0) {
      this.applyPendingPreselect();
    }
  }

  /**
   * بعد اكتمال تحميل البيانات المرجعية الأولية بالكامل (السنوات/الصفوف/الفصول)، نطبّق الفلتر
   * المبدئي الجاي من query params لو موجود وصالح (موجود بالفعل في البيانات المحمّلة).
   * بيعلو السنة الحالية المتعيّنة تلقائيًا لو فيها تعارض.
   */
  private applyPendingPreselect(): void {
    const p = this.pendingPreselect;
    this.pendingPreselect = null;
    if (!p) return;

    if (p.yearId && this.academicYears().some(y => y.id === p.yearId)) {
      this.selectedYearId.set(p.yearId);
    }
    if (p.gradeId && this.grades().some(g => g.id === p.gradeId)) {
      this.selectedGradeId.set(p.gradeId);
    }
    if (p.classId && this.classes().some(c => c.id === p.classId)) {
      this.selectedClassId.set(p.classId);
    }
    this.onFilterChange();
  }

  ngOnDestroy(): void {
    if (this.resizeHandler) {
      window.removeEventListener('resize', this.resizeHandler);
    }
    // تنظيف أي timers معلّقة لمنع تحديث signal بعد التدمير
    for (const timer of this.messageTimers.values()) clearTimeout(timer);
  }

  // ══════════════════════════════════════════════════════════════════════════
  // UI panel toggles
  // ══════════════════════════════════════════════════════════════════════════

  isAssignmentBankVisible(): boolean { return this.assignmentBankOpen(); }

  toggleAssignmentBank(): void { this.assignmentBankOpen.update(v => !v); }

  toggleTimetablesPicker(): void { this.timetablesPickerOpen.update(v => !v); }

  closeTimetablesPicker(): void { this.timetablesPickerOpen.set(false); }

  toggleActionsMenu(): void { this.actionsMenuOpen.update(v => !v); }

  closeActionsMenu(): void { this.actionsMenuOpen.set(false); }

  setTimetablesFilter(filter: TimetablesFilter): void { this.timetablesFilter.set(filter); }

  selectTimetable(timetableId: number): void {
    const timetable = this.allTimetables().find(t => t.id === timetableId);
    if (!timetable) return;
    this.selectedTimetableId.set(timetable.id);
    this.currentTimetable.set(timetable);
    this.validationResult.set(null);
    this.selectedInspection.set(null);
    this.closeTimetablesPicker();
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Schedule editor
  // ═══════════════════════════════════════════════════════​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​
  openScheduleEditor(): void {
    if (!this.currentTimetable()) return;
    this.scheduleEditorOpen.set(true);
  }

  closeScheduleEditor(): void {
    this.scheduleEditorOpen.set(false);
    this.closeModal();
    this.selectedInspection.set(null);
  }

  getSelectedClassName(): string {
    const id = this.selectedClassId();
    return this.classes().find(c => c.id === id)?.name || '';
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Time Grid editing (من رأس الجدول)
  // ══════════════════════════════════════════════════════════════════════════

  /** هل توقيت الحصة ده قيد التحرير حاليًا؟ */
  isEditingPeriod(num: number): boolean { return this.editingPeriod() === num; }

  /**
   * فتح تحرير توقيت حصة من رأس الجدول.
   * يُسمح به فقط على المسودة القابلة للتعديل (مش جدول منشور).
   */
  startEditPeriod(num: number): void {
    if (!this.canEditCurrentTimetable()) return;
    const p = this.getPeriod(num);
    if (!p) return;
    this.editingPeriod.set(num);
    this.editPeriodStart.set(this.toTimeInputValue(p.start));
    this.editPeriodEnd.set(this.toTimeInputValue(p.end));
    this.editPeriodError.set(null);
  }

  /**
   * حفظ التوقيت الجديد للحصة.
   *
   * Flow (optimistic update مع rollback آمن):
   *   1) تحقق من نطاق الوقت.
   *   2) خزّن snapshot للتوقيت القديم (للـ rollback).
   *   3) طبّق التوقيت optimistic على الـ grid + كل الـ slots في الـ frontend.
   *   4) استدعِ الـ backend لتحديث الـ DB (batch لكل slots برقم الحصة).
   *   5) لو فشل → ارجع للتوقيت القديم + اعرض الخطأ.
   *
   * لاحظ: لو مفيش جدول حالي (currentTimetable() === null)، نطبّق على الـ grid بس
   * (الـ slots هتتحدّث لما المستخدم يضيفها).
   */
  savePeriodEdit(): void {
    const num = this.editingPeriod();
    if (num == null || this.isSavingPeriod()) return;

    const start = this.toTimeOnlyString(this.editPeriodStart());
    const end = this.toTimeOnlyString(this.editPeriodEnd());

    if (this.toMinutes(end) <= this.toMinutes(start)) {
      this.editPeriodError.set('وقت النهاية يجب أن يكون بعد وقت البداية.');
      return;
    }

    // No-overlap validation: التوقيت الجديد ما يتعارضش مع أي حصة تانية في الـ grid.
    // الفحص ده مسبق عشان المستخدم يشوف الخطأ قبل استدعاء الـ API.
    const overlap = this.findOverlappingPeriod(num, start, end);
    if (overlap) {
      this.editPeriodError.set(
        `التوقيت يتداخل مع ${overlap.label} (${this.formatSlotTimeRange(overlap.start, overlap.end)}). ` +
        'اضبط التوقيت حتى لا يتداخل مع الحصص الأخرى.'
      );
      return;
    }

    // 1) snapshot للتوقيت القديم
    const oldPeriod = this.getPeriod(num);
    this.periodEditSnapshot = oldPeriod
      ? { num, start: oldPeriod.start, end: oldPeriod.end }
      : null;

    // 2) optimistic: حدّث الـ grid
    this.periods.update(list => list.map(p => p.num === num
      ? { ...p, start, end }
      : p));

    // 3) optimistic: حدّث كل الـ slots بنفس رقم الحصة في الجدول الحالي
    const timetable = this.currentTimetable();
    if (timetable) {
      this.currentTimetable.update(tt => tt ? {
        ...tt,
        slots: tt.slots.map(s => s.periodNumber === num
          ? { ...s, startTime: start, endTime: end }
          : s),
      } : tt);
    }

    // 4) أغلق وضع التحرير فورًا + اعرض حالة الحفظ
    const label = this.getPeriod(num)?.label ?? 'الحصة';
    this.cancelPeriodEdit();

    // لو مفيش جدول حالي → التغيير في الـ grid كافي (الـ slots هتتحدّث عند الإضافة)
    if (!timetable) {
      this.showSuccess(`تم تحديث توقيت ${label}.`);
      return;
    }

    // 5) استدعِ الـ backend لتحديث الـ DB
    this.isSavingPeriod.set(true);
    this.timetableService.updatePeriodTiming(timetable.id, num, start, end)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isSavingPeriod.set(false);
          this.showSuccess(`تم تحديث توقيت ${label} لكل أيام الأسبوع.`);
        },
        error: (err: any) => {
          this.isSavingPeriod.set(false);
          // rollback: ارجع للتوقيت القديم في الـ grid + الـ slots
          this.rollbackPeriodEdit();
          this.showError(this.getApiErrorMessage(err, 'تعذّر تحديث توقيت الحصة. تم إرجاع التوقيت السابق.'));
        },
      });
  }

  /** إرجاع التوقيت القديم بعد فشل الحفظ (rollback للـ grid + الـ slots) */
  private rollbackPeriodEdit(): void {
    const snap = this.periodEditSnapshot;
    if (!snap) return;
    this.periods.update(list => list.map(p => p.num === snap.num
      ? { ...p, start: snap.start, end: snap.end }
      : p));
    this.currentTimetable.update(tt => tt ? {
      ...tt,
      slots: tt.slots.map(s => s.periodNumber === snap.num
        ? { ...s, startTime: snap.start, endTime: snap.end }
        : s),
    } : tt);
    this.periodEditSnapshot = null;
  }

  /**
   * يفحص إن التوقيت الجديد للحصة (num) بيتداخل مع أي حصة تانية في الـ grid.
   * التداخل = فترتان [start,end) تشتركان في أي دقيقة (باستثناء التلامس عند الحدود).
   * يرجع أول حصة متداخلة، أو null لو مفيش تداخل.
   */
  private findOverlappingPeriod(
    num: number,
    newStart: string,
    newEnd: string
  ): PeriodDef | undefined {
    const newStartMin = this.toMinutes(newStart);
    const newEndMin = this.toMinutes(newEnd);
    for (const p of this.periods()) {
      if (p.num === num) continue;
      const pStart = this.toMinutes(p.start);
      const pEnd = this.toMinutes(p.end);
      // overlap: newStart < pEnd && pStart < newEnd
      if (newStartMin < pEnd && pStart < newEndMin) {
        return p;
      }
    }
    return undefined;
  }

  /** إلغاء تحرير التوقيت وإغلاق الحقول */
  cancelPeriodEdit(): void {
    this.editingPeriod.set(null);
    this.editPeriodStart.set('');
    this.editPeriodEnd.set('');
    this.editPeriodError.set(null);
  }

  /** هل مفروض يظهر زرار التعديل لتوقيت الحصة؟ (مسودة قابلة للتعديل + غير قيد التحرير) */
  canEditPeriod(num: number): boolean {
    return this.canEditCurrentTimetable() && !this.isEditingPeriod(num);
  }

  // ── إضافة/حذف أعمدة (حصص) من رأس الجدول ───────────────────────────────────

  /** هل يمكن إضافة حصة جديدة؟ (مسودة + لم نصل للحد الأقصى) */
  canAddPeriod(): boolean {
    return this.canEditCurrentTimetable() && this.periods().length < this.MAX_PERIODS;
  }

  /**
   * إضافة حصة جديدة في نهاية الـ grid.
   * التوقيت يُحسب تلقائيًا: نهاية آخر حصة + DEFAULT_PERIOD_MINUTES دقيقة.
   * الـ label يُولّد بالعربي (الحصة الثامنة/التاسعة/...).
   */
  addPeriod(): void {
    if (!this.canAddPeriod()) return;

    const list = this.periods();
    const last = list[list.length - 1];
    const nextNum = (list.length > 0 ? last.num : 0) + 1;

    // حساب التوقيت: آخر نهاية + 45 دقيقة. لو ما فيش حصص نبدأ من 08:00.
    const startMinutes = last ? this.toMinutes(last.end) : this.toMinutes('08:00:00');
    const startStr = this.minutesToTimeOnly(startMinutes);
    const endStr = this.minutesToTimeOnly(startMinutes + this.DEFAULT_PERIOD_MINUTES);

    this.periods.update(arr => [...arr, {
      num: nextNum,
      label: this.toArabicOrdinal(nextNum, 'الحصة'),
      start: startStr,
      end: endStr,
    }]);
    this.showSuccess(`تمت إضافة ${this.toArabicOrdinal(nextNum, 'الحصة')} لكل أيام الأسبوع.`);
  }

  /** عدد الحصص المجدولة في عمود معيّن (0 لو لا يوجد) */
  getPeriodSlotCount(num: number): number {
    return this.slotsCountByPeriod().get(num) ?? 0;
  }

  /**
   * هل يمكن حذف العمود (الحصة)؟
   * القواعد:
   *   1) مسودة قابلة للتعديل.
   *   2) فيه أكثر من حصة واحدة في الـ grid (لا نحذف آخر عمود).
   *   3) العمود فاضي تمامًا (لا توجد slots به) — لتجنّب بيانات أيتيمة/التباس منطقي.
   */
  canRemovePeriod(num: number): boolean {
    if (!this.canEditCurrentTimetable()) return false;
    if (this.periods().length <= this.MIN_PERIODS) return false;
    return this.getPeriodSlotCount(num) === 0;
  }

  /** سبب تعذّر حذف العمود (للعرض كـ tooltip) — null لو الحذف ممكن */
  getPeriodRemoveBlockReason(num: number): string | null {
    if (!this.canEditCurrentTimetable()) return 'لا يمكن تعديل جدول منشور.';
    if (this.periods().length <= this.MIN_PERIODS) return 'لا يمكن حذف آخر حصة في الجدول.';
    const count = this.getPeriodSlotCount(num);
    if (count > 0) return `لا يمكن حذف هذه الحصة لأن بها ${count} ${this.pluralizeSlot(count)} مجدولة. احذف الحصص من الخلايا أولًا.`;
    return null;
  }

  /** حذف العمود (الحصة) من الـ grid. يُفترض أن canRemovePeriod() === true. */
  removePeriod(num: number): void {
    if (!this.canRemovePeriod(num)) {
      const reason = this.getPeriodRemoveBlockReason(num);
      if (reason) this.showError(reason);
      return;
    }
    const label = this.getPeriod(num)?.label ?? `الحصة ${num}`;
    this.periods.update(arr => arr.filter(p => p.num !== num));
    // لو كنا بنحرر ده، نلغي التحرير
    if (this.editingPeriod() === num) this.cancelPeriodEdit();
    this.showSuccess(`تم حذف ${label} من الجدول.`);
  }

  /** إعادة ضبط الـ grid للـ 7 حصص الافتراضية */
  resetPeriodsToDefault(): void {
    if (!this.canEditCurrentTimetable()) return;
    this.cancelPeriodEdit();
    this.periods.set([
      { num: 1, label: 'الحصة الأولى',   start: '08:00:00', end: '08:45:00' },
      { num: 2, label: 'الحصة الثانية',  start: '08:45:00', end: '09:30:00' },
      { num: 3, label: 'الحصة الثالثة',  start: '09:30:00', end: '10:15:00' },
      { num: 4, label: 'الحصة الرابعة',  start: '10:30:00', end: '11:15:00' },
      { num: 5, label: 'الحصة الخامسة',  start: '11:15:00', end: '12:00:00' },
      { num: 6, label: 'الحصة السادسة',  start: '12:00:00', end: '12:45:00' },
      { num: 7, label: 'الحصة السابعة',  start: '12:45:00', end: '13:30:00' },
    ]);
    this.showSuccess('تمت إعادة ضبط شبكة الحصص للوضع الافتراضي.');
  }

  /** هل عدد الحصص وصل للحد الأقصى؟ */
  isAtMaxPeriods(): boolean { return this.periods().length >= this.MAX_PERIODS; }

  // ══════════════════════════════════════════════════════════════════════════
  // Data loading
  // ══════════════════════════════════════════════════════════════════════════

  private loadInitialData(): void {
    this.subjectService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => { this.subjects.set(this.unwrap(data)); this.markInitialLoadDone(); },
        error: () => { this.showError('تعذر تحميل بيانات المواد'); this.markInitialLoadDone(); },
      });

    // pageSize مرتفعة لاستيعاب كل المعلمين دفعة واحدة (مع إمكانية التوسعة لاحقًا)
    this.userService.getByRole('Teacher', 5000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res: any) => { this.teachers.set(res?.data?.items ?? res?.items ?? []); this.markInitialLoadDone(); },
        error: () => { this.showError('تعذر تحميل بيانات المعلمين'); this.markInitialLoadDone(); },
      });

    this.gradeLevelService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => {
          const unwrapped = this.unwrap(data);
          const sortedGrades = [...unwrapped].sort((a, b) => a.levelOrder - b.levelOrder);
          this.grades.set(sortedGrades);
          this.markInitialLoadDone();
        },
        error: () => { this.showError('تعذر تحميل بيانات الصفوف الدراسية'); this.markInitialLoadDone(); },
      });

    this.classService.getAll({ status: 1 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => { this.classes.set(this.unwrap(data)); this.markInitialLoadDone(); },
        error: () => { this.showError('تعذر تحميل بيانات الفصول'); this.markInitialLoadDone(); },
      });

    this.academicYearService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => {
          const unwrapped = this.unwrap(data);
          this.academicYears.set(unwrapped);
          const active = unwrapped.find(y => y.isCurrent);
          if (active) this.selectedYearId.set(active.id);
          this.markInitialLoadDone();
        },
        error: () => { this.showError('تعذر تحميل السنوات الدراسية'); this.markInitialLoadDone(); },
      });
  }

  onGradeFilterChange(): void {
    this.selectedClassId.set(null);
    this.onFilterChange();
  }

  onFilterChange(): void {
    this.selectedTimetableId.set(null);
    this.closeTimetablesPicker();
    this.closeScheduleEditor();
    this.closeModal();

    const yearId = this.selectedYearId();
    const classId = this.selectedClassId();

    if (yearId && classId) {
      this.loadTimetables();
      this.loadAssignments();
    } else {
      this.resetTimetableState();
      this.assignments.set([]);
    }
  }

  private loadAssignments(): void {
    this.assignmentService.getByClass(this.selectedClassId()!, this.selectedYearId()!)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => this.assignments.set(this.unwrap(data)),
        error: () => {
          this.assignments.set([]);
          this.showError('تعذر تحميل تعيينات هذا الفصل');
        },
      });
  }

  private loadTimetables(): void {
    this.isLoading.set(true);
    this.timetableService.getByClass(this.selectedClassId()!, this.selectedYearId()!)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => {
          const list = this.unwrap(data).map((item: any) => this.normalizeTimetable(item));
          const selectedId = this.selectedTimetableId();
          const next = list.find((t: Timetable) => t.id === selectedId)
            || this.pickWorkingTimetable(list);

          this.allTimetables.set(list);
          this.currentTimetable.set(next);
          this.selectedTimetableId.set(next?.id ?? null);
          this.validationResult.set(null);
          this.selectedInspection.set(null);
          this.isLoading.set(false);
        },
        error: (err: any) => {
          this.resetTimetableState();
          this.isLoading.set(false);
          if (err?.status === 403) {
            this.showError('ليس لديك صلاحية للوصول إلى هذا الجدول.');
            return;
          }
          if (err?.status && err.status !== 404) {
            this.showError(this.getApiErrorMessage(err, 'تعذر تحميل الجداول الدراسية لهذا الفصل.'));
          }
        },
      });
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Timetable CRUD
  // ══════════════════════════════════════════════════════════════════════════

  createTimetable(): void {
    const classId = this.selectedClassId();
    const yearId = this.selectedYearId();
    if (!classId || !yearId) return;

    if (this.hasDraftTimetable()) {
      this.showError('توجد بالفعل مسودة مفتوحة لهذا الفصل. أكمل العمل عليها أو فعّلها أولًا.');
      return;
    }

    this.timetableService.create({ classId, academicYearId: yearId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.selectedTimetableId.set(null);
          this.loadTimetables();
          this.showSuccess('تم إنشاء مسودة الجدول بنجاح، ويمكنك الآن بدء التسكين بأمان.');
        },
        error: (err: any) => this.showError(this.getApiErrorMessage(err, 'فشل إنشاء مسودة الجدول.')),
      });
  }

  cloneDraftFromCurrent(): void {
    const classId = this.selectedClassId();
    const yearId = this.selectedYearId();
    if (!classId || !yearId) return;

    // ملاحظة منطقية: النسخ يعتمد على الجدول الحالي. لو لا يوجد جدول أساس فالنسخ لا معنى له.
    if (!this.allTimetables().length) {
      this.showError('لا يوجد جدول حالي لنسخه. استخدم "إنشاء مسودة" للبدء من الصفر.');
      return;
    }

    // لو فيه مسودة موجودة → افتح confirm dialog لاستبدالها بدل ما نرفض الطلب.
    // المستخدم بيتأكد الأول لأن الاستبدال بيعمل soft delete للمسودة الحالية (كل حصصها).
    if (this.hasDraftTimetable()) {
      this.replaceDraftConfirmOpen.set(true);
      return;
    }

    this.runCloneDraft(false);
  }

  /** تنفيذ استدعاء النسخ للباك إند. replaceExisting بيحدد لو فيه استبدال لمسودة قديمة. */
  private runCloneDraft(replaceExisting: boolean): void {
    const classId = this.selectedClassId();
    const yearId = this.selectedYearId();
    if (!classId || !yearId) return;

    this.isCloning.set(true);
    this.timetableService.cloneDraft(classId, yearId, replaceExisting)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isCloning.set(false);
          this.selectedTimetableId.set(null);
          this.loadTimetables();
          this.showSuccess(replaceExisting
            ? 'تم استبدال المسودة السابقة بنسخة جديدة من الجدول الحالي، ويمكنك الآن تعديلها بأمان.'
            : 'تم إنشاء مسودة جديدة بنسخ الجدول الحالي، ويمكنك الآن تعديلها بأمان.');
        },
        error: (err: any) => {
          this.isCloning.set(false);
          this.showError(this.getApiErrorMessage(err, 'تعذر إنشاء مسودة من الجدول الحالي.'));
        },
      });
  }

  /** تأكيد استبدال المسودة الموجودة بنسخة جديدة من الجدول الحالي. */
  confirmReplaceDraft(): void {
    this.replaceDraftConfirmOpen.set(false);
    this.runCloneDraft(true);
  }

  /** إلغاء استبدال المسودة. */
  cancelReplaceDraft(): void {
    this.replaceDraftConfirmOpen.set(false);
  }

  // ── Delete draft ──────────────────────────────────────────────────────────

  canDeleteTimetable(): boolean {
    const t = this.currentTimetable();
    return !!t && this.isDraftTimetable(t);
  }

  deleteTimetable(): void {
    const timetable = this.currentTimetable();
    if (!timetable || !this.isDraftTimetable(timetable)) return;

    this.deleteTimetableConfirmOpen.set(false);
    this.isDeletingTimetable.set(true);

    this.timetableService.delete(timetable.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isDeletingTimetable.set(false);
          this.resetTimetableState();
          if (this.selectedClassId() && this.selectedYearId()) {
            this.loadTimetables();
          }
          this.showSuccess('تم حذف المسودة بنجاح.');
        },
        error: (err: any) => {
          this.isDeletingTimetable.set(false);
          this.showError(this.getApiErrorMessage(err, 'تعذر حذف المسودة الحالية.'));
        },
      });
  }

  // ── Deactivate published timetable ────────────────────────────────────────

  canDeactivateTimetable(): boolean {
    const t = this.currentTimetable();
    return !!t && this.isActiveTimetable(t);
  }

  deactivateTimetable(): void {
    const timetable = this.currentTimetable();
    if (!timetable || !this.isActiveTimetable(timetable)) return;

    this.deactivateConfirmOpen.set(false);
    this.isDeactivating.set(true);

    this.timetableService.deactivate(timetable.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isDeactivating.set(false);
          this.loadTimetables();
          this.validationResult.set(null);
          this.showSuccess('تم إلغاء نشر الجدول ونقله إلى الأرشيف.');
        },
        error: (err: any) => {
          this.isDeactivating.set(false);
          this.showError(this.getApiErrorMessage(err, 'تعذر إلغاء تفعيل الجدول.'));
        },
      });
  }

  // ── Publish (single unified flow: validate → publish) ─────────────────────

  /**
   * Flow موحّد للنشر:
   *   1) لو لم تتم المراجعة بعد → شغّلها أولًا وأظهر النتيجة.
   *   2) لو المراجعة موجودة وغير صالحة → أوقف وأظهر الأخطاء فقط.
   *   3) وإلا → فعّل الجدول مباشرة.
   * الإصلاح الأساسي: النسخة القديمة كانت تتوقف بعد المراجعة وتطلب من المستخدم
   * الضغط على "تفعيل" مرة أخرى (silent failure للنية). الآن كل شيء في خطوة واحدة.
   */
  publishCurrentTimetable(): void {
    const timetable = this.currentTimetable();
    if (!timetable || !this.isDraftTimetable(timetable)) return;

    const validation = this.validationResult();
    if (!validation) {
      this.validateCurrentTimetable(true);
      return;
    }

    if (!validation.canActivate) {
      this.showError('توجد أخطاء يجب إصلاحها قبل التفعيل. راجع لوحة المراجعة بالأسفل.');
      return;
    }

    this.isPublishing.set(true);
    this.timetableService.activate(timetable.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isPublishing.set(false);
          this.loadTimetables();
          this.validationResult.set(null);
          this.showSuccess('تم نشر الجدول بنجاح وأصبح هو الجدول المعروض للطلاب والمعلمين.');
        },
        error: (err: any) => {
          this.isPublishing.set(false);
          this.showError(this.getApiErrorMessage(err, 'تعذر تفعيل الجدول الحالي.'));
          // إعادة المراجعة تلقائيًا لإظهار أسباب الفشل
          if (!this.validationResult()?.errors?.length) {
            this.validateCurrentTimetable(true);
          }
        },
      });
  }

  // ── Validate ───────────────────────────────────────────────────────────────

  validateCurrentTimetable(silent = false): void {
    const timetable = this.currentTimetable();
    if (!timetable) return;

    this.isValidating.set(true);
    this.timetableService.validate(timetable.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result: any) => {
          this.isValidating.set(false);
          const res: TimetableValidationResult = result?.data ?? result;
          this.validationResult.set(res);
          if (!silent) {
            this.showSuccess(res.canActivate
              ? 'اكتملت المراجعة: الجدول جاهز للتفعيل.'
              : 'اكتملت المراجعة: توجد عناصر تحتاج معالجة قبل التفعيل.');
          } else if (res.canActivate) {
            // أثناء الـ publish flow: نواصل للنشر تلقائيًا إذا كان صالحًا
            this.publishCurrentTimetable();
          } else {
            this.showError('لا يمكن نشر الجدول قبل إصلاح التعارضات الظاهرة في نتيجة المراجعة.');
          }
        },
        error: (err: any) => {
          this.isValidating.set(false);
          this.showError(this.getApiErrorMessage(err, 'تعذر تنفيذ المراجعة على الجدول الحالي.'));
        },
      });
  }

  /** نسخة قابلة للاستدعاء من القالب */
  validateFromUi(): void { this.validateCurrentTimetable(false); }

  // ══════════════════════════════════════════════════════════════════════════
  // Slot modal
  // ══════════════════════════════════════════════════════════════════════════

  getSlot(dayValue: string, periodNum: number): TimetableSlotDto | null {
    return this.slotMap().get(`${dayValue}-${periodNum}`) ?? null;
  }

  /**
   * فتح مودال الحصة:
   *   - لا جدول محدد          → خطأ + return
   *   - منشور + خلية فارغة    → لا شيء (silent)
   *   - منشور + خلية ممتلئة   → مودال للقراءة فقط
   *   - مسودة + أي خلية       → مودال قابل للتحرير
   */
  openSlotModal(dayValue: string, periodNum: number): void {
    if (!this.currentTimetable()) {
      this.showError('أنشئ أو اختر جدولًا أولًا قبل إدارة الحصص.');
      return;
    }

    const existingSlot = this.getSlot(dayValue, periodNum);
    // مصدر الحقيقة للتوقيت: شبكة التوقيت الموحّدة (رأس الجدول) — مش إدخال المستخدم.
    const periodData = this.getPeriod(periodNum)!;
    this.selectedInspection.set({ dayValue, periodNum, slot: existingSlot });

    const canEdit = this.canEditCurrentTimetable();

    if (!canEdit) {
      if (!existingSlot) return;
      this.isReadOnlyModal.set(true);
    } else {
      this.isReadOnlyModal.set(false);
    }

    this.isEditingSlot.set(!!existingSlot);
    this.editingSlotId.set(existingSlot?.id ?? null);
    this.deleteSlotConfirmOpen.set(false);
    this.slotFormError.set(null);

    // التوقيت دائمًا من الـ grid definition (يتبع آخر تعديل في رأس الجدول).
    // بيانات الحصة الموجودة (مادة/معلم/قاعة/راحة) بس هي اللي بتيجي من existingSlot.
    this.newSlot.set({
      timetableId: this.currentTimetable()!.id,
      dayOfWeek: dayValue,
      periodNumber: periodNum,
      startTime: periodData.start,
      endTime: periodData.end,
      classSubjectTeacherId: existingSlot?.classSubjectTeacherId ?? null,
      isBreak: !!existingSlot?.isBreak,
      roomId: existingSlot?.roomId ?? null,
    });

    if (this.newSlot().isBreak) {
      this.availableRooms.set([]);
    } else {
      this.loadAvailableRooms(dayValue, periodNum);
    }

    this.isModalOpen.set(true);
  }

  /** نسخة من openSlotModal قابلة للاستدعاء من keyboard event */
  onCellKeydown(event: KeyboardEvent, dayValue: string, periodNum: number): void {
    if (event.key === 'Enter' || event.key === ' ' || event.key === 'Spacebar') {
      event.preventDefault();
      this.openSlotModal(dayValue, periodNum);
    }
  }

  closeModal(): void {
    this.isModalOpen.set(false);
    this.isEditingSlot.set(false);
    this.isReadOnlyModal.set(false);
    this.editingSlotId.set(null);
    this.availableRooms.set([]);
    this.alternativeRooms.set([]);
    this.deleteSlotConfirmOpen.set(false);
    this.slotFormError.set(null);
    this.roomsLoading.set(false);
  }

  saveSlot(): void {
    if (!this.currentTimetable()) {
      this.showSlotFormError('لا يوجد جدول محدد للحفظ عليه.');
      return;
    }
    if (this.isReadOnlyModal()) {
      this.showSlotFormError('هذه الحصة معروضة للقراءة فقط لأن الجدول الحالي منشور.');
      return;
    }
    if (!this.canEditCurrentTimetable()) {
      this.showSlotFormError('الجدول الحالي منشور ولا يقبل التعديل المباشر.');
      return;
    }

    const draft = this.newSlot();

    if (!draft.isBreak && !draft.classSubjectTeacherId) {
      this.showSlotFormError('الرجاء اختيار المادة والمعلم قبل حفظ الحصة.');
      return;
    }
    // تحقق سليم من الوقت عبر تحويله لدقائق (لا يعتمد على string lex-sort الهش)
    if (this.toMinutes(draft.endTime) <= this.toMinutes(draft.startTime)) {
      this.showSlotFormError('وقت النهاية يجب أن يكون بعد وقت البداية.');
      return;
    }

    const assignmentId = draft.isBreak ? null : draft.classSubjectTeacherId;
    const roomId       = draft.isBreak ? null : draft.roomId;
    this.slotFormError.set(null);
    this.alternativeRooms.set([]);
    this.isSaving.set(true);

    if (this.isEditingSlot() && this.editingSlotId()) {
      const slotId = this.editingSlotId()!;
      this.timetableService.updateSlot(slotId, {
        slotId,
        dayOfWeek: draft.dayOfWeek,
        periodNumber: draft.periodNumber,
        startTime: draft.startTime,
        endTime: draft.endTime,
        classSubjectTeacherId: assignmentId,
        isBreak: draft.isBreak,
        roomId,
      })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (res: any) => this.finishSlotMutation(this.getApiSuccessMessage(res, 'تم تحديث الحصة بنجاح')),
          error: (err: any) => {
            this.isSaving.set(false);
            const msg = this.getApiErrorMessage(err, 'تعذر تحديث الحصة الحالية.');
            this.showSlotFormError(msg);
            this.populateSuggestionsOnError(msg);
          },
        });
      return;
    }

    this.timetableService.addSlot({
      timetableId: draft.timetableId,
      dayOfWeek: draft.dayOfWeek,
      periodNumber: draft.periodNumber,
      startTime: draft.startTime,
      endTime: draft.endTime,
      classSubjectTeacherId: assignmentId,
      isBreak: draft.isBreak,
      roomId,
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res: any) => this.finishSlotMutation(this.getApiSuccessMessage(res, 'تم تسكين الحصة بنجاح')),
        error: (err: any) => {
          this.isSaving.set(false);
          const msg = this.getApiErrorMessage(err, 'تعذر إضافة الحصة. تأكد من عدم وجود تعارض للمعلم أو الفصل.');
          this.showSlotFormError(msg);
          this.populateSuggestionsOnError(msg);
        },
      });
  }

  /**
   * تعبئة الاقتراحات البديلة بناءً على نص رسالة الخطأ القادمة من الـ backend:
   *   - لو تعارض قاعة     → حمّل القاعات البديلة المتاحة في نفس اليوم/الحصة.
   *   - لو تعارض يوم/حصة  → القاعات المتاحة كمان هتفيد (لأن المستخدم غالبًا هيغيّر الحصة).
   * لو الخطأ نوع تاني (وقت/صلاحيات) مفيش اقتراحات.
   */
  private populateSuggestionsOnError(message: string): void {
    const draft = this.newSlot();
    const isRoomConflict = message.includes('القاعة') && message.includes('محجوزة');
    const isCellConflict = message.includes('الخلية') && message.includes('مشغولة');

    if ((isRoomConflict || isCellConflict) && !draft.isBreak) {
      this.loadAlternativeRooms(draft.dayOfWeek, draft.periodNumber);
    } else {
      this.alternativeRooms.set([]);
    }
  }

  /** تحميل القاعات البديلة المتاحة في يوم/حصة معيّنين (لعرضها كاقتراحات). */
  private loadAlternativeRooms(dayValue: string, periodNum: number): void {
    this.roomService.getAvailable(dayValue, periodNum)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => {
          this.alternativeRooms.set(this.unwrap(data));
        },
        error: () => this.alternativeRooms.set([]),
      });
  }

  /** تبديل القاعة المختارة بقاعة بديلة من الاقتراحات بنقرة واحدة. */
  pickAlternativeRoom(roomId: number): void {
    this.newSlot.update(d => ({ ...d, roomId }));
    this.slotFormError.set(null);
  }

  onBreakToggle(): void {
    const draft = this.newSlot();
    if (draft.isBreak) {
      this.newSlot.set({
        ...draft,
        classSubjectTeacherId: null,
        roomId: null,
      });
      this.availableRooms.set([]);
      this.alternativeRooms.set([]);
      return;
    }
    this.loadAvailableRooms(draft.dayOfWeek, draft.periodNumber);
  }

  /** تحديث حقل واحد في مسودة الحصة بشكل immutable (لأنها signal) */
  patchDraft<K extends keyof SlotDraft>(key: K, value: SlotDraft[K]): void {
    this.newSlot.update(d => ({ ...d, [key]: value }));
    // إعادة تحميل القاعات المتاحة تلقائيًا عند تغيير اليوم/الحصة (الحصة الدراسية فقط)
    // لأن قائمة القاعات تعتمد على اليوم والحصة المختارين.
    const draft = this.newSlot();
    if (!draft.isBreak && (key === 'dayOfWeek' || key === 'periodNumber')) {
      this.loadAvailableRooms(draft.dayOfWeek, draft.periodNumber);
      // لو القاعة الحالية مش في القائمة الجديدة، نصفّرها
      if (draft.roomId != null) {
        this.newSlot.update(d2 => ({ ...d2, roomId: null }));
      }
    }
  }

  // ── Two-step slot delete ──────────────────────────────────────────────────

  /**
   * تصنيف رسالة الخطأ الحالية لنوع بصري (لون + أيقونة) في الواجهة.
   * يرجع 'room' (تعارض قاعة)، 'teacher' (تعارض معلم)، 'cell' (خلية مشغولة)،
   * أو 'generic' لأي خطأ آخر.
   */
  readonly slotErrorType = computed<'room' | 'teacher' | 'cell' | 'generic'>(() => {
    const msg = this.slotFormError();
    if (!msg) return 'generic';
    if (msg.includes('القاعة') && msg.includes('محجوزة')) return 'room';
    if (msg.includes('تعارض') && (msg.includes('المعلم') || msg.includes('يدرّس'))) return 'teacher';
    if (msg.includes('الخلية') && msg.includes('مشغولة')) return 'cell';
    return 'generic';
  });

  /** هل توجد قاعات بديلة مقترحة لعرضها؟ */
  readonly hasAlternativeRooms = computed(() => this.alternativeRooms().length > 0);

  requestDeleteSlot(): void { this.deleteSlotConfirmOpen.set(true); }
  cancelDeleteSlot(): void { this.deleteSlotConfirmOpen.set(false); }

  deleteEditingSlot(): void {
    const slotId = this.editingSlotId();
    if (!slotId) return;
    if (!this.canEditCurrentTimetable()) {
      this.showSlotFormError('لا يمكن حذف حصص من جدول منشور.');
      return;
    }

    this.deleteSlotConfirmOpen.set(false);
    this.isSaving.set(true);

    this.timetableService.deleteSlot(slotId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.finishSlotMutation('تم حذف الحصة بنجاح'),
        error: (err: any) => {
          this.isSaving.set(false);
          this.showSlotFormError(this.getApiErrorMessage(err, 'تعذر حذف الحصة الحالية.'));
        },
      });
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Assignment helpers
  // ══════════════════════════════════════════════════════════════════════════

  getAssignmentDetails(cstId: number | null): { subjectName: string; teacherName: string } | null {
    if (cstId == null) return null;
    const assignment = this.assignmentMap().get(cstId);
    if (!assignment) return null;
    return {
      subjectName: assignment.subjectName || this.getSubjectName(assignment.subjectId),
      teacherName: assignment.teacherName || this.getTeacherName(assignment.teacherId),
    };
  }

  getSubjectName(subjectId: number | null | undefined): string {
    if (subjectId == null) return 'مجهول';
    return this.subjects().find(s => s.id === subjectId)?.name || 'مجهول';
  }

  getTeacherName(teacherId: number | null | undefined): string {
    if (teacherId == null) return 'مجهول';
    return this.teachers().find(t => t.id === teacherId)?.fullName || 'مجهول';
  }

  getRemainingPeriods(assignment: ClassSubjectTeacher): number {
    const used = this.usedPeriodsMap().get(assignment.id!) ?? 0;
    return Math.max(assignment.weeklyPeriods - used, 0);
  }

  getAssignmentProgressWidth(assignment: ClassSubjectTeacher): number {
    if (assignment.weeklyPeriods <= 0) return 0;
    const used = Math.min(this.usedPeriodsMap().get(assignment.id!) ?? 0, assignment.weeklyPeriods);
    return Math.max(0, Math.min(100, (used / assignment.weeklyPeriods) * 100));
  }

  isAssignmentSelectable(assignment: ClassSubjectTeacher): boolean {
    const remaining = this.getRemainingPeriods(assignment);
    const editingSlot = this.getEditingSlot();
    return remaining > 0 || editingSlot?.classSubjectTeacherId === assignment.id;
  }

  /** قائمة التعيينات المفلترة — مع memoization بسيطة عبر computed */
  readonly filteredAssignments = computed(() => {
    const subjectFilter = this.assignmentSubjectFilter().trim().toLowerCase();
    const teacherFilter = this.assignmentTeacherFilter().trim().toLowerCase();
    const list = this.assignments();

    if (!subjectFilter && !teacherFilter) return list;

    return list.filter(assign => {
      const subjectName = (assign.subjectName || this.getSubjectName(assign.subjectId)).toLowerCase();
      const teacherName = (assign.teacherName || this.getTeacherName(assign.teacherId)).toLowerCase();
      return (!subjectFilter || subjectName.includes(subjectFilter))
        && (!teacherFilter || teacherName.includes(teacherFilter));
    });
  });

  // ══════════════════════════════════════════════════════════════════════════
  // State predicates
  // ══════════════════════════════════════════════════════════════════════════

  hasDraftTimetable(): boolean { return this.allTimetables().some(t => this.isDraftTimetable(t)); }
  hasPublishedTimetable(): boolean { return this.allTimetables().some(t => this.isActiveTimetable(t)); }
  hasMultipleTimetables(): boolean { return this.allTimetables().length > 1; }
  canEditCurrentTimetable(): boolean {
    const t = this.currentTimetable();
    return !!t && this.isDraftTimetable(t);
  }
  canCreateDraft(): boolean {
    return !!this.selectedClassId() && !!this.selectedYearId() && !this.hasDraftTimetable();
  }
  canCloneDraft(): boolean {
    return !!this.selectedClassId()
      && !!this.selectedYearId()
      && !this.hasDraftTimetable()
      && this.allTimetables().some(t => this.isActiveTimetable(t) || this.isArchivedTimetable(t));
  }
  /** مصدر الحقيقة الوحيد لمنع النشر: canActivate من نتيجة المراجعة */
  hasBlockingValidationErrors(): boolean {
    const r = this.validationResult();
    return !!r && !r.canActivate;
  }
  hasPendingValidation(): boolean {
    const t = this.currentTimetable();
    return !!t && this.isDraftTimetable(t) && !this.validationResult();
  }

  private getTimetableStatus(t: Timetable | TimetableListItem): TimetableStatusName {
    const status = (t.status ?? '').toString().toLowerCase();
    if (status === 'active' || t.statusValue === 1 || t.isActive === true) return 'Active';
    if (status === 'archived' || t.statusValue === 2) return 'Archived';
    return 'Draft';
  }

  isDraftTimetable(t: Timetable | TimetableListItem): boolean {
    return this.getTimetableStatus(t) === 'Draft';
  }

  isActiveTimetable(t: Timetable | TimetableListItem): boolean {
    return this.getTimetableStatus(t) === 'Active';
  }

  isArchivedTimetable(t: Timetable | TimetableListItem): boolean {
    return this.getTimetableStatus(t) === 'Archived';
  }

  // ══════════════════════════════════════════════════════════════════════════
  // UI label / display helpers
  // ═══════════════════════════════════════════════════════════════​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​
  getCurrentStatusLabel(): string {
    const t = this.currentTimetable();
    if (!t) return '';
    if (this.isActiveTimetable(t)) return 'منشور';
    if (this.isArchivedTimetable(t)) return 'مؤرشف';
    return 'مسودة';
  }

  getTimetableCountLabel(): string {
    const total = this.allTimetables().length;
    return total === 0 ? 'لا توجد نسخ' : `${total} ${total === 1 ? 'نسخة' : 'نسخ'}`;
  }

  getFilteredTimetableCountLabel(): string {
    const total = this.filteredTimetables().length;
    return total === 0 ? 'لا توجد نتائج' : `${total} ${total === 1 ? 'نسخة' : 'نسخ'}`;
  }

  getCurrentTimetableVersionLabel(): string {
    const currentId = this.currentTimetable()?.id;
    const current = this.allTimetables().find(t => t.id === currentId);
    if (!current) return 'لم يتم اختيار نسخة';
    return current.versionNumber ? `النسخة ${current.versionNumber}` : `النسخة ${this.allTimetables().findIndex(t => t.id === currentId) + 1}`;
  }

  isSelectedTimetable(timetableId: number): boolean {
    return this.selectedTimetableId() === timetableId;
  }

  isTimetablesFilterActive(filter: TimetablesFilter): boolean {
    return this.timetablesFilter() === filter;
  }

  getPublishedTimetablesCount(): number {
    return this.allTimetables().filter(t => this.isActiveTimetable(t)).length;
  }

  getDraftTimetablesCount(): number {
    return this.allTimetables().filter(t => this.isDraftTimetable(t)).length;
  }

  getArchivedTimetablesCount(): number {
    return this.allTimetables().filter(t => this.isArchivedTimetable(t)).length;
  }

  readonly filteredTimetables = computed(() => {
    const filter = this.timetablesFilter();
    const list = this.allTimetables();
    if (filter === 'active') return list.filter(t => this.isActiveTimetable(t));
    if (filter === 'draft') return list.filter(t => this.isDraftTimetable(t));
    if (filter === 'archived') return list.filter(t => this.isArchivedTimetable(t));
    return list;
  });

  formatTimetableDate(value: string | null | undefined): string {
    if (!value) return 'غير متاح';
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) return 'غير متاح';
    return new Intl.DateTimeFormat('ar-EG', {
      year: 'numeric', month: 'short', day: 'numeric',
      hour: 'numeric', minute: '2-digit',
    }).format(parsed);
  }

  formatSlotTimeRange(startTime: string | null | undefined, endTime: string | null | undefined): string {
    if (!startTime || !endTime) return 'الوقت غير محدد';
    return `${startTime.slice(0, 5)} - ${endTime.slice(0, 5)}`;
  }

  getCurrentStatusMessage(): string {
    const t = this.currentTimetable();
    if (!t) return '';
    if (this.isActiveTimetable(t)) {
      return 'هذه هي النسخة المنشورة حاليًا. أنشئ مسودة جديدة إذا أردت تعديل الجدول بدون التأثير على الطلاب والمعلمين.';
    }
    if (this.isArchivedTimetable(t)) {
      return 'هذه نسخة مؤرشفة للقراءة فقط. لإنشاء تعديل جديد استخدم نسخ من الحالي وسيتم إنشاء مسودة قابلة للمراجعة والنشر.';
    }
    return 'أنت تعمل الآن على مسودة مستقلة. يمكنك تعديلها بأمان ثم نشرها بعد المراجعة.';
  }

  getValidationHeadline(): string {
    const r = this.validationResult();
    if (!r) return '';
    return r.canActivate ? 'نتيجة المراجعة: الجدول جاهز للتفعيل' : 'نتيجة المراجعة: توجد مشكلات تحتاج معالجة';
  }

  getValidationTone(): string {
    const r = this.validationResult();
    if (!r) return '';
    return r.canActivate
      ? 'is-ok'
      : (r.errors.length ? 'is-error' : 'is-warn');
  }

  getValidationSummary(): string {
    const r = this.validationResult();
    if (!r) return '';
    return `إجمالي الخلايا: ${r.totalSlots}، الحصص الدراسية: ${r.lessonSlots}، فترات الراحة: ${r.breakSlots}، الأخطاء: ${r.errors.length}، التحذيرات: ${r.warnings.length}`;
  }

  /** نسبة الإنجاز — computed لتفادي إعادة الحساب في كل CD */
  readonly completionPercent = computed(() => {
    const assignments = this.assignments();
    if (!assignments.length) return 0;
    const usedMap = this.usedPeriodsMap();
    const totalRequired = assignments.reduce((s, a) => s + a.weeklyPeriods, 0);
    if (totalRequired <= 0) return 0;
    const totalScheduled = assignments.reduce(
      (s, a) => s + Math.min(usedMap.get(a.id!) ?? 0, a.weeklyPeriods), 0
    );
    return Math.max(0, Math.min(100, Math.round((totalScheduled / totalRequired) * 100)));
  });

  readonly outstandingAssignmentsCount = computed(() => {
    const usedMap = this.usedPeriodsMap();
    return this.assignments().filter(a => {
      const used = usedMap.get(a.id!) ?? 0;
      return Math.max(a.weeklyPeriods - used, 0) > 0;
    }).length;
  });

  getDaySummary(dayValue: string): { filled: number; lessons: number; breaks: number; issues: number } {
    const slots = (this.currentTimetable()?.slots || []).filter(s => s.dayOfWeek === dayValue);
    return {
      filled: slots.length,
      lessons: slots.filter(s => !s.isBreak).length,
      breaks: slots.filter(s => s.isBreak).length,
      issues: slots.reduce((n, s) => n + this.getSlotIssueCount(s), 0),
    };
  }

  /** نسخة memoized من ملخص اليوم لتفادي إعادة الحساب 20 مرة في كل render */
  readonly daySummaries = computed(() => {
    const slots = this.currentTimetable()?.slots ?? [];
    const validation = this.validationResult();
    const issueSlotIds = new Set<number>();
    const issueDayKeys = new Set<string>();
    if (validation) {
      for (const i of [...validation.errors, ...validation.warnings]) {
        if (i.slotId != null) issueSlotIds.add(i.slotId);
        if (i.dayOfWeek && i.periodNumber != null) {
          issueDayKeys.add(`${i.dayOfWeek}-${i.periodNumber}`);
        }
      }
    }

    const map = new Map<string, { filled: number; lessons: number; breaks: number; issues: number }>();
    for (const day of this.days) {
      const daySlots = slots.filter(s => s.dayOfWeek === day.value);
      let issues = 0;
      for (const s of daySlots) {
        if (issueSlotIds.has(s.id) || issueDayKeys.has(`${s.dayOfWeek}-${s.periodNumber}`)) issues++;
      }
      map.set(day.value, {
        filled: daySlots.length,
        lessons: daySlots.filter(s => !s.isBreak).length,
        breaks: daySlots.filter(s => s.isBreak).length,
        issues,
      });
    }
    return map;
  });

  getDaySummaryTone(dayValue: string): string {
    const summary = this.daySummaries().get(dayValue);
    if (!summary) return 'tone-empty';
    if (summary.issues > 0) return 'tone-warn';
    if (summary.filled === 0) return 'tone-empty';
    return 'tone-active';
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Cell / slot display helpers
  // ═════════════════════════════════════════════════════════​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​
  getCellClass(dayValue: string, periodNum: number): string {
    const slot = this.getSlot(dayValue, periodNum);
    if (!slot) return '';
    if (this.hasSlotError(slot)) return 'cell-issue';
    if (this.hasSlotWarning(slot)) return 'cell-warn';
    return slot.isBreak ? 'cell-break' : 'cell-lesson';
  }

  getCellCursor(dayValue: string, periodNum: number): string {
    if (this.canEditCurrentTimetable()) return 'cursor-pointer';
    return this.getSlot(dayValue, periodNum) ? 'cursor-pointer' : 'cursor-default';
  }

  getCellAriaLabel(dayValue: string, periodNum: number): string {
    const dayLabel = this.getDayLabel(dayValue);
    const periodLabel = this.getPeriod(periodNum)?.label || `الحصة ${periodNum}`;
    const slot = this.getSlot(dayValue, periodNum);
    if (!slot) {
      return `${dayLabel} ${periodLabel}: خلية فارغة${this.canEditCurrentTimetable() ? '، اضغط لإضافة حصة' : ''}`;
    }
    if (slot.isBreak) return `${dayLabel} ${periodLabel}: فترة راحة`;
    const d = this.getAssignmentDetails(slot.classSubjectTeacherId);
    return `${dayLabel} ${periodLabel}: ${d?.subjectName || 'حصة دراسية'}${d?.teacherName ? ' - ' + d.teacherName : ''}`;
  }

  getSlotIssueCount(slot: TimetableSlotDto): number { return this.getSlotIssues(slot).length; }

  getSlotIssueIcon(slot: TimetableSlotDto): string {
    return this.hasSlotError(slot) ? 'error' : 'warning';
  }

  hasSlotError(slot: TimetableSlotDto): boolean {
    return this.getSlotIssues(slot).some(i => i.severity.toLowerCase() === 'error');
  }

  hasSlotWarning(slot: TimetableSlotDto): boolean {
    return this.getSlotIssues(slot).some(i => i.severity.toLowerCase() === 'warning');
  }

  getSlotIssueTooltip(slot: TimetableSlotDto): string {
    return this.getSlotIssues(slot).slice(0, 2).map(i => i.message).join(' | ');
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Inspection panel helpers
  // ══════════════════════════════════════════════════════════════════════════

  getSelectedInspectionTitle(): string {
    const sel = this.selectedInspection();
    if (!sel) return 'اختر خلية من الجدول';
    const dayLabel = this.getDayLabel(sel.dayValue);
    const periodLabel = this.getPeriod(sel.periodNum)?.label || `الحصة ${sel.periodNum}`;
    return `${dayLabel} - ${periodLabel}`;
  }

  getSelectedInspectionSubtitle(): string {
    const sel = this.selectedInspection();
    if (!sel?.slot) return 'يمكنك الضغط على أي خلية لعرض حالتها ومشكلاتها المرتبطة.';
    if (sel.slot.isBreak) return 'الفترة الحالية مسجلة كفترة راحة.';
    const details = this.getAssignmentDetails(sel.slot.classSubjectTeacherId);
    return `${details?.subjectName || 'حصة دراسية'}${details?.teacherName ? ` - ${details.teacherName}` : ''}`;
  }

  readonly selectedInspectionIssues = computed<TimetableValidationIssue[]>(() => {
    const sel = this.selectedInspection();
    if (!sel) return [];
    return this.resolveIssuesForContext(sel.slot, sel.dayValue, sel.periodNum);
  });

  hasSelectedInspectionIssues(): boolean { return this.selectedInspectionIssues().length > 0; }

  formatValidationIssue(issue: TimetableValidationIssue): string {
    const parts: string[] = [];
    if (issue.dayOfWeek) parts.push(this.getDayLabel(issue.dayOfWeek));
    if (issue.periodNumber) parts.push(`الحصة ${issue.periodNumber}`);
    return parts.length ? `${parts.join(' - ')}: ${issue.message}` : issue.message;
  }

  getDayLabel(dayValue: string): string {
    return this.days.find(d => d.value === dayValue)?.label || dayValue;
  }

  // ═════════════════════════════════════════════════════​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​​
  // Private helpers
  // ══════════════════════════════════════════════════════════════════════════

  private emptySlotDraft(): SlotDraft {
    return {
      timetableId: 0,
      dayOfWeek: '',
      periodNumber: 0,
      startTime: '',
      endTime: '',
      classSubjectTeacherId: null,
      isBreak: false,
      roomId: null,
    };
  }

  private pickWorkingTimetable(timetables: Timetable[]): Timetable | null {
    if (!timetables.length) return null;
    return timetables.find(t => this.isDraftTimetable(t))
      || timetables.find(t => this.isActiveTimetable(t))
      || timetables.find(t => this.isArchivedTimetable(t))
      || null;
  }

  private normalizeTimetable(item: Timetable | TimetableListItem): Timetable {
    const status = this.getTimetableStatus(item);
    return {
      id: item.id,
      classId: Number(item.classId),
      className: item.className ?? '',
      academicYearId: Number(item.academicYearId),
      isActive: status === 'Active',
      status,
      statusValue: item.statusValue ?? (status === 'Active' ? 1 : status === 'Archived' ? 2 : 0),
      versionNumber: item.versionNumber ?? 1,
      publishedAt: item.publishedAt ?? null,
      archivedAt: item.archivedAt ?? null,
      sourceTimetableId: item.sourceTimetableId ?? null,
      createdAt: item.createdAt ?? '',
      updatedAt: item.updatedAt ?? item.createdAt ?? '',
      slots: item.slots ?? [],
    };
  }

  private resetTimetableState(): void {
    this.allTimetables.set([]);
    this.selectedTimetableId.set(null);
    this.timetablesFilter.set('all');
    this.currentTimetable.set(null);
    this.validationResult.set(null);
    this.selectedInspection.set(null);
    this.deleteTimetableConfirmOpen.set(false);
    this.deactivateConfirmOpen.set(false);
    this.timetablesPickerOpen.set(false);
  }

  private loadAvailableRooms(dayValue: string, periodNum: number): void {
    this.roomsLoading.set(true);
    this.roomService.getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data: any) => {
          this.availableRooms.set(this.unwrap(data));
          this.roomsLoading.set(false);
        },
        error: () => {
          this.availableRooms.set([]);
          this.roomsLoading.set(false);
        },
      });
  }

  private getEditingSlot(): TimetableSlotDto | null {
    const slotId = this.editingSlotId();
    if (!slotId) return null;
    return (this.currentTimetable()?.slots || []).find(s => s.id === slotId) ?? null;
  }

  private getSlotIssues(slot: TimetableSlotDto): TimetableValidationIssue[] {
    if (!slot) return [];
    return this.resolveIssuesForContext(slot, slot.dayOfWeek, slot.periodNumber);
  }

  private resolveIssuesForContext(
    slot: TimetableSlotDto | null,
    dayValue: string,
    periodNum: number
  ): TimetableValidationIssue[] {
    const validation = this.validationResult();
    if (!validation) return [];
    return [...validation.errors, ...validation.warnings].filter(issue =>
      (!!slot && issue.slotId === slot.id) ||
      (!!issue.dayOfWeek && issue.dayOfWeek === dayValue && issue.periodNumber === periodNum) ||
      (!!slot && !!issue.classSubjectTeacherId && issue.classSubjectTeacherId === slot.classSubjectTeacherId)
    );
  }

  /**
   * إنهاء عملية تعديل حصة:
   *   1) تحديث optimistically بدل إعادة تحميل كل الجداول (أسرع وأكثر سلاسة).
   *   2) إغلاق المودال وإظهار رسالة نجاح.
   *
   * ملاحظة: نعتمد على re-fetch خفيف في الخلفية لضمان التزامن مع الباك إند،
   * لكن المستخدم يرى التغيير فورًا. لو فشل الـ re-fetch يبقى التحديث المحلي ساريًا.
   */
  private finishSlotMutation(message: string): void {
    this.isSaving.set(false);
    this.slotFormError.set(null);
    this.closeModal();
    // re-fetch لضمان التزامن الكامل (slots الجديدة تحتاج id من الباك إند)
    if (this.selectedClassId() && this.selectedYearId()) {
      this.loadTimetables();
    }
    this.showSuccess(message);
  }

  /** يفك wrapper الـ OperationResult/PagedResult بكافة أشكاله المعروفة */
  private unwrap<T = any>(data: any): T[] {
    if (Array.isArray(data)) return data as T[];
    if (Array.isArray(data?.data)) return data.data as T[];
    if (Array.isArray(data?.data?.items)) return data.data.items as T[];
    if (Array.isArray(data?.items)) return data.items as T[];
    return [];
  }

  /** يحوّل "HH:mm:ss" إلى عدد دقائق من بداية اليوم للمقارنة الرقمية السليمة */
  private toMinutes(time: string): number {
    if (!time) return -1;
    const parts = time.split(':').map(Number);
    if (parts.some(n => Number.isNaN(n))) return -1;
    const [h, m, s = 0] = parts;
    return h * 60 + m + s / 60;
  }

  /**
   * يحوّل "HH:mm:ss" إلى صيغة input type="time" وهي "HH:mm".
   * لازم نأخذ أول 5 حروف لأن <input type="time"> بيرفض الـ seconds إلا لو step="1".
   */
  private toTimeInputValue(time: string): string {
    return (time ?? '').slice(0, 5);
  }

  /**
   * يحوّل قيمة input type="time" ("HH:mm") إلى صيغة "HH:mm:ss" المتوقّعة من الباك إند.
   * لو القيمة فارشة بترجّع '' وده هيُرصد في التحقق.
   */
  private toTimeOnlyString(value: string): string {
    if (!value) return '';
    // لو المستخدم أدخل HH:mm بس، نضيف :00 للثواني
    const parts = value.split(':');
    if (parts.length === 2) return `${value}:00`;
    return value;
  }

  /**
   * يحوّل عدد دقائق من بداية اليوم إلى صيغة "HH:mm:ss" (للحصص المُضافة تلقائيًا).
   * يلف حول اليوم لو تجاوز 24 ساعة (آمن ضد القيم الكبيرة).
   */
  private minutesToTimeOnly(totalMinutes: number): string {
    if (totalMinutes < 0) totalMinutes = 0;
    const minutesInDay = 24 * 60;
    const m = Math.floor(totalMinutes % minutesInDay);
    const h = Math.floor(m / 60);
    const min = m % 60;
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${pad(h)}:${pad(min)}:00`;
  }

  /**
   * يرجّع الترتيب العربي لرقم مع بادئة اختيارية.
   * مثال: toArabicOrdinal(8, 'الحصة') = 'الحصة الثامنة'.
   */
  private toArabicOrdinal(num: number, prefix = 'الحصة'): string {
    const ordinals: Record<number, string> = {
      1: 'الأولى', 2: 'الثانية', 3: 'الثالثة', 4: 'الرابعة', 5: 'الخامسة',
      6: 'السادسة', 7: 'السابعة', 8: 'الثامنة', 9: 'التاسعة', 10: 'العاشرة',
    };
    const ord = ordinals[num] ?? `رقم ${num}`;
    return `${prefix} ${ord}`;
  }

  /** جمع كلمة «حصة/حصتين/حصص» بالعربي حسب العدد */
  private pluralizeSlot(count: number): string {
    if (count === 1) return 'حصة';
    if (count === 2) return 'حصتين';
    return 'حصص';
  }

  private showSlotFormError(message: string): void {
    this.slotFormError.set(message);
  }

  private getApiErrorMessage(err: any, fallback: string): string {
    // 1) OperationResult message (شكل الباك إند الموحّد)
    const opMessage = err?.error?.message ?? err?.error;
    if (typeof opMessage === 'string' && opMessage.trim()) return opMessage;

    // 2) ASP.NET ProblemDetails للـ validation errors ([Range], [EnumDataType], ...)
    //    شكلها: { errors: { "DayOfWeek": ["..."], "PeriodNumber": ["..."] } } أو { title, detail }
    const problem = err?.error;
    if (problem?.errors && typeof problem.errors === 'object') {
      const first = Object.values(problem.errors)[0];
      if (Array.isArray(first) && first.length) return String(first[0]);
    }
    if (typeof problem?.detail === 'string' && problem.detail.trim()) return problem.detail;
    if (typeof problem?.title === 'string' && problem.title.trim()) return problem.title;

    return fallback;
  }

  private getApiSuccessMessage(res: any, fallback: string): string {
    const message = res?.message ?? res?.data?.message;
    return typeof message === 'string' && message.trim() ? message : fallback;
  }

  private showError(msg: string): void {
    this.clearTimer('success');
    this.errorMessage.set(msg);
    this.successMessage.set(null);
    this.armTimer('error', () => this.errorMessage.set(null), 5000);
  }

  private showSuccess(msg: string): void {
    this.clearTimer('error');
    this.successMessage.set(msg);
    this.errorMessage.set(null);
    this.armTimer('success', () => this.successMessage.set(null), 3000);
  }

  private armTimer(key: 'success' | 'error', cb: () => void, delayMs: number): void {
    const existing = this.messageTimers.get(key);
    if (existing) clearTimeout(existing);
    this.messageTimers.set(key, setTimeout(cb, delayMs));
  }

  private clearTimer(key: 'success' | 'error'): void {
    const existing = this.messageTimers.get(key);
    if (existing) {
      clearTimeout(existing);
      this.messageTimers.delete(key);
    }
  }
}
