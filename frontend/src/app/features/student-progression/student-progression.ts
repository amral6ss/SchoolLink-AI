import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
import { AcademicYear, AcademicYearService } from '../../core/services/academic-year.service';
import { ClassEntity, ClassService } from '../../core/services/class.service';
import { GradeLevel, GradeLevelService } from '../../core/services/grade-level.service';
import {
  AcademicStatus,
  AcademicTermLabel,
  ProgressionTermScope,
  StudentProgressionCandidate,
  StudentProgressionRequest,
  StudentProgressionResult,
  StudentProgressionService
} from '../../core/services/student-progression.service';
import { Sidebar } from '../../layouts/sidebar/sidebar';
type ProgressionAction = 1 | 2 | 3;
type AccountFilter = 'all' | 'with-account' | 'without-account';
type StatusFilter = 'all' | 'passed' | 'failed' | 'unpublished' | 'no-grades';
type SortMode = 'name' | 'high' | 'low';
type DecisionKey = 'promote' | 'retain' | 'pending' | 'graduate';
type ClassMappingState = Record<number, number | null>;
interface SourceClassMappingRow {
  id: number;
  name: string;
  count: number;
}

@Component({
  selector: 'app-student-progression',
  standalone: true,
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './student-progression.html',
  styleUrl: './student-progression.css'
})
export class StudentProgression implements OnInit {
  sidebarOpen = signal(false);
  displayUserName = localStorage.getItem('fullName') || localStorage.getItem('username') || 'المشرف';

  private academicYearService = inject(AcademicYearService);
  private gradeLevelService = inject(GradeLevelService);
  private classService = inject(ClassService);
  private studentProgressionService = inject(StudentProgressionService);

  academicYears = signal<AcademicYear[]>([]);
  gradeLevels = signal<GradeLevel[]>([]);
  classes = signal<ClassEntity[]>([]);
  candidates = signal<StudentProgressionCandidate[]>([]);
  selectedEnrollmentIds = signal<number[]>([]);

  selectedSourceAcademicYearId = signal<number | null>(null);
  selectedSourceGradeLevelId = signal<number | null>(null);
  selectedTargetAcademicYearId = signal<number | null>(null);
  selectedTargetClassId = signal<number | null>(null);
  classMappings = signal<ClassMappingState>({});

  action = signal<ProgressionAction>(1);
  effectiveDate = signal(this.getTodayDate());
  note = signal('');
  threshold = signal(50);
  searchQuery = signal('');
  accountFilter = signal<AccountFilter>('all');
  statusFilter = signal<StatusFilter>('all');
  sortMode = signal<SortMode>('name');
  termScope = signal<ProgressionTermScope>(3); // Both semesters = end-of-year basis

  /** Enrollment IDs whose subject-breakdown row is expanded in the table. */
  expandedSubjectIds = signal<Set<number>>(new Set());

  currentPage = signal(1);
  itemsPerPage = signal(10);

  /** Snapshot of academic-status counts (Passed/Failed/Unpublished/NoGrades) for the loaded list. */
  statusCounts = computed(() => {
    const counts = { passed: 0, failed: 0, unpublished: 0, noGrades: 0 };
    for (const c of this.candidates()) {
      if (c.academicStatus === 'Passed') counts.passed++;
      else if (c.academicStatus === 'Failed') counts.failed++;
      else if (c.academicStatus === 'Unpublished') counts.unpublished++;
      else counts.noGrades++;
    }
    return counts;
  });

  annualDecisionCounts = computed(() => {
    const isFinalGradeLevel = !this.nextGradeLevel();
    const counts = { promote: 0, graduate: 0, retain: 0, pending: 0 };

    for (const candidate of this.candidates()) {
      if (candidate.academicStatus === 'Passed') {
        if (isFinalGradeLevel) counts.graduate++;
        else counts.promote++;
      } else if (candidate.academicStatus === 'Failed') {
        counts.retain++;
      } else {
        counts.pending++;
      }
    }

    return counts;
  });

  selectedDecisionCounts = computed(() => {
    const counts = { eligible: 0, blocked: 0, pending: 0 };

    for (const candidate of this.selectedCandidates()) {
      const eligible =
        (this.action() === 2 && candidate.academicStatus === 'Failed') ||
        (this.action() !== 2 && candidate.academicStatus === 'Passed');

      if (eligible) counts.eligible++;
      else if (candidate.academicStatus === 'NoGrades' || candidate.academicStatus === 'Unpublished') counts.pending++;
      else counts.blocked++;
    }

    return counts;
  });

  isBootstrapping = signal(false);
  isLoadingCandidates = signal(false);
  isExecuting = signal(false);
  errorMessage = signal('');
  successMessage = signal('');
  lastResult = signal<StudentProgressionResult | null>(null);

  selectedSourceAcademicYear = computed(() =>
    this.academicYears().find(year => year.id === this.selectedSourceAcademicYearId()) ?? null
  );

  selectedSourceGradeLevel = computed(() =>
    this.gradeLevels().find(grade => grade.id === this.selectedSourceGradeLevelId()) ?? null
  );

  nextGradeLevel = computed(() => {
    const sourceGrade = this.selectedSourceGradeLevel();
    if (!sourceGrade) return null;

    return this.gradeLevels().find(grade => grade.levelOrder === sourceGrade.levelOrder + 1) ?? null;
  });

  futureAcademicYears = computed(() => {
    const sourceYear = this.selectedSourceAcademicYear();
    if (!sourceYear) return [];

    const sourceStart = new Date(sourceYear.startDate).getTime();
    return this.academicYears()
      .filter(year => year.id !== sourceYear.id && new Date(year.startDate).getTime() > sourceStart)
      .sort((a, b) => new Date(a.startDate).getTime() - new Date(b.startDate).getTime());
  });

  targetGradeLevelId = computed(() => {
    if (this.action() === 1) return this.nextGradeLevel()?.id ?? null;
    if (this.action() === 2) return this.selectedSourceGradeLevelId();
    return null;
  });

  availableTargetClasses = computed(() => {
    const targetYearId = this.selectedTargetAcademicYearId();
    const targetGradeId = this.targetGradeLevelId();

    if (!targetYearId || !targetGradeId) return [];

    return this.classes()
      .filter(cls =>
        cls.academicYearId === targetYearId &&
        cls.gradeLevelId === targetGradeId &&
        (cls.status ?? 1) === 1)
      .sort((a, b) => a.name.localeCompare(b.name, 'ar'));
  });

  filteredCandidates = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const accountFilter = this.accountFilter();
    const statusFilter = this.statusFilter();
    const sortMode = this.sortMode();

    const filtered = this.candidates().filter(candidate => {
      const matchesQuery =
        !query ||
        candidate.studentName.toLowerCase().includes(query) ||
        candidate.currentClassName.toLowerCase().includes(query);

      const matchesAccount =
        accountFilter === 'all' ||
        (accountFilter === 'with-account' && candidate.hasStudentAccount) ||
        (accountFilter === 'without-account' && !candidate.hasStudentAccount);

      const matchesStatus =
        statusFilter === 'all' ||
        (statusFilter === 'passed' && candidate.academicStatus === 'Passed') ||
        (statusFilter === 'failed' && candidate.academicStatus === 'Failed') ||
        (statusFilter === 'unpublished' && candidate.academicStatus === 'Unpublished') ||
        (statusFilter === 'no-grades' && candidate.academicStatus === 'NoGrades');

      return matchesQuery && matchesAccount && matchesStatus;
    });

    return filtered.sort((a, b) => {
      if (sortMode === 'name') {
        return a.studentName.localeCompare(b.studentName, 'ar');
      }

      const aTotal = a.finalTotal ?? Number.NEGATIVE_INFINITY;
      const bTotal = b.finalTotal ?? Number.NEGATIVE_INFINITY;
      return sortMode === 'high' ? bTotal - aTotal : aTotal - bTotal;
    });
  });

  paginatedCandidates = computed(() => {
    const start = (this.currentPage() - 1) * this.itemsPerPage();
    return this.filteredCandidates().slice(start, start + this.itemsPerPage());
  });

  totalPages = computed(() => {
    return Math.max(1, Math.ceil(this.filteredCandidates().length / this.itemsPerPage()));
  });

  rangeStart = computed(() => {
    if (this.filteredCandidates().length === 0) return 0;
    return (this.currentPage() - 1) * this.itemsPerPage() + 1;
  });

  rangeEnd = computed(() => {
    return Math.min(this.currentPage() * this.itemsPerPage(), this.filteredCandidates().length);
  });

  pages = computed<(number | string)[]>(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    const result: (number | string)[] = [];
    result.push(1);
    if (current > 3) result.push('...');
    for (let i = Math.max(2, current - 1); i <= Math.min(total - 1, current + 1); i++) {
      result.push(i);
    }
    if (current < total - 2) result.push('...');
    if (total > 1) result.push(total);
    return result;
  });

  trackByPageIndex = (_: number, item: number | string) =>
    typeof item === 'string' ? `dot-${_}` : `page-${item}`;

  nextPage() {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update(p => p + 1);
    }
  }

  prevPage() {
    if (this.currentPage() > 1) {
      this.currentPage.update(p => p - 1);
    }
  }

  goToPage(page: number) {
    this.currentPage.set(page);
  }

  selectedCandidates = computed(() => {
    const selectedIds = new Set(this.selectedEnrollmentIds());
    return this.candidates().filter(candidate => selectedIds.has(candidate.enrollmentId));
  });

  sourceClassesForMapping = computed<SourceClassMappingRow[]>(() => {
    const selected = this.selectedCandidates();
    const source = selected.length ? selected : this.candidates();
    const byClassId = new Map<number, SourceClassMappingRow>();

    for (const candidate of source) {
      const row = byClassId.get(candidate.currentClassId);
      if (row) {
        row.count++;
      } else {
        byClassId.set(candidate.currentClassId, {
          id: candidate.currentClassId,
          name: candidate.currentClassName,
          count: 1
        });
      }
    }

    return Array.from(byClassId.values())
      .sort((a, b) => a.name.localeCompare(b.name, 'ar'));
  });

  selectedVisibleCount = computed(() => {
    const selectedIds = new Set(this.selectedEnrollmentIds());
    return this.filteredCandidates().filter(candidate => selectedIds.has(candidate.enrollmentId)).length;
  });

  allVisibleSelected = computed(() => {
    const visible = this.filteredCandidates();
    if (!visible.length) return false;

    const selectedIds = new Set(this.selectedEnrollmentIds());
    return visible.every(candidate => selectedIds.has(candidate.enrollmentId));
  });

  selectedCount = computed(() => this.selectedEnrollmentIds().length);

  selectedStatusIssue = computed(() => {
    const selected = this.selectedCandidates();
    if (!selected.length) return '';

    if (this.action() === 1 || this.action() === 3) {
      return selected.every(candidate => candidate.academicStatus === 'Passed')
        ? ''
        : 'لا يمكن تنفيذ الترقية أو التخرج إلا للطلاب الناجحين في نتيجة السنة كاملة.';
    }

    return selected.every(candidate => candidate.academicStatus === 'Failed')
      ? ''
      : 'لا يمكن تنفيذ الإبقاء إلا للطلاب الراسبين في نتيجة السنة كاملة.';
  });

  classMappingIssue = computed(() => {
    if (this.action() === 3) return '';
    if (!this.selectedCount()) return '';
    if (!this.selectedTargetAcademicYearId()) return 'اختر السنة الدراسية الهدف أولا.';

    const availableTargetIds = new Set(this.availableTargetClasses().map(cls => cls.id));
    if (!availableTargetIds.size) return '';

    const mappings = this.classMappings();
    const selectedSourceClassIds = new Set(this.selectedCandidates().map(candidate => candidate.currentClassId));

    for (const sourceClassId of selectedSourceClassIds) {
      const targetClassId = mappings[sourceClassId];
      if (!targetClassId) {
        return 'يجب تحديد فصل هدف لكل فصل مصدر ضمن الطلاب المحددين.';
      }

      if (!availableTargetIds.has(targetClassId)) {
        return 'يوجد ربط يشير إلى فصل هدف غير متاح للصف أو السنة المختارة.';
      }
    }

    return '';
  });

  hasTargetConfigurationIssue = computed(() => {
    if (this.action() === 3) return '';
    if (!this.selectedSourceGradeLevel() || !this.selectedSourceAcademicYear()) return '';

    if (this.action() === 1 && !this.nextGradeLevel()) {
      return 'هذا الصف هو الأخير حالياً، لذلك لا يمكن تنفيذ الترقية ويجب استخدام التخرج.';
    }

    if (this.futureAcademicYears().length === 0) {
      return 'لا توجد سنة دراسية لاحقة متاحة حالياً لتنفيذ الترقية أو الإبقاء.';
    }

    if (this.selectedTargetAcademicYearId() && this.availableTargetClasses().length === 0) {
      return 'لا توجد فصول متاحة في الوجهة المختارة. أنشئ الفصول أولاً ثم أعد المحاولة.';
    }

    return '';
  });

  canExecute = computed(() => {
    if (this.selectedCount() === 0 || !this.effectiveDate()) return false;
    if (this.selectedStatusIssue()) return false;

    if (this.action() === 3) {
      return !this.nextGradeLevel();
    }

    if (this.hasTargetConfigurationIssue()) return false;

    return !!this.selectedTargetAcademicYearId() && !this.classMappingIssue();
  });

  ngOnInit(): void {
    this.loadPageData();
  }

  loadPageData() {
    this.isBootstrapping.set(true);
    this.clearMessages();

    forkJoin({
      years: this.academicYearService.getAll(),
      grades: this.gradeLevelService.getAll(),
      classes: this.classService.getAll({ status: 1 })
    })
      .pipe(finalize(() => this.isBootstrapping.set(false)))
      .subscribe({
        next: ({ years, grades, classes }) => {
          const unwrappedYears = this.unwrapData<AcademicYear[]>(years) ?? [];
          const unwrappedGrades = (this.unwrapData<GradeLevel[]>(grades) ?? [])
            .slice()
            .sort((a, b) => a.levelOrder - b.levelOrder);
          const unwrappedClasses = this.unwrapData<ClassEntity[]>(classes) ?? [];

          this.academicYears.set(unwrappedYears);
          this.gradeLevels.set(unwrappedGrades);
          this.classes.set(unwrappedClasses);

          const currentYear = unwrappedYears.find(year => year.isCurrent) ?? unwrappedYears[0] ?? null;
          if (currentYear) {
            this.selectedSourceAcademicYearId.set(currentYear.id);
          }
        },
        error: err => this.showError(this.extractErrorMessage(err, 'تعذر تحميل بيانات الصفحة.'))
      });
  }

  loadCandidates() {
    const sourceYearId = this.selectedSourceAcademicYearId();
    const sourceGradeId = this.selectedSourceGradeLevelId();

    if (!sourceYearId || !sourceGradeId) {
      this.showError('اختر السنة الدراسية المصدر والصف الدراسي المصدر أولاً.');
      return;
    }

    this.isLoadingCandidates.set(true);
    this.clearMessages();
    this.lastResult.set(null);
    this.selectedEnrollmentIds.set([]);

    this.studentProgressionService
        .getCandidates(sourceGradeId, sourceYearId, this.termScope(), this.threshold())
        .pipe(finalize(() => this.isLoadingCandidates.set(false)))
        .subscribe({
          next: res => {
            const candidates = this.unwrapData<StudentProgressionCandidate[]>(res) ?? [];
            this.candidates.set(candidates);
            this.selectedTargetClassId.set(null);
            this.classMappings.set({});
            this.currentPage.set(1);
            this.autoMapTargetClasses();

            if (!candidates.length) {
              this.successMessage.set('لا يوجد طلاب نشطون في هذا الصف خلال السنة الدراسية المحددة.');
            }
          },
          error: err => {
            this.candidates.set([]);
            this.showError(this.extractErrorMessage(err, 'تعذر تحميل الطلاب المرشحين.'));
          }
        });
  }

  onSourceSelectionChanged() {
    this.candidates.set([]);
    this.selectedEnrollmentIds.set([]);
    this.selectedTargetAcademicYearId.set(null);
    this.selectedTargetClassId.set(null);
    this.classMappings.set({});
    this.lastResult.set(null);
    this.clearMessages();
  }

  onTermScopeChange(value: ProgressionTermScope) {
    this.termScope.set(value);
    // Threshold is a server-side decision now; reload with the new scope.
    if (this.candidates().length || this.selectedSourceGradeLevelId()) {
      this.loadCandidates();
    }
  }

  /**
   * Selects all visible candidates whose academic status matches the given value.
   * Used by the "passed / failed / unpublished / no-grades" quick-select buttons.
   */
  selectVisibleByStatus(status: AcademicStatus) {
    const matched = this.filteredCandidates()
      .filter(c => c.academicStatus === status)
      .map(c => c.enrollmentId);

    const visibleIds = new Set(this.filteredCandidates().map(c => c.enrollmentId));
    const preservedHidden = this.selectedEnrollmentIds().filter(id => !visibleIds.has(id));
    this.selectedEnrollmentIds.set([...preservedHidden, ...matched]);
  }

  /** Quick auto-decide helper: leaves "no-grades/unpublished" unselected. */
  autoDecideSelection() {
    const ids = this.filteredCandidates()
      .filter(c => c.academicStatus === 'Passed' || c.academicStatus === 'Failed') // passed or failed
      .map(c => c.enrollmentId);

    const visibleIds = new Set(this.filteredCandidates().map(c => c.enrollmentId));
    const preservedHidden = this.selectedEnrollmentIds().filter(id => !visibleIds.has(id));
    this.selectedEnrollmentIds.set([...preservedHidden, ...ids]);
  }

  selectRecommendedForCurrentAction() {
    const desiredStatus: AcademicStatus = this.action() === 2 ? 'Failed' : 'Passed';
    this.selectVisibleByStatus(desiredStatus);
  }

  getAnnualDecisionKey(candidate: StudentProgressionCandidate): DecisionKey {
    if (candidate.academicStatus === 'Passed') {
      return this.nextGradeLevel() ? 'promote' : 'graduate';
    }
    if (candidate.academicStatus === 'Failed') return 'retain';
    return 'pending';
  }

  getAnnualDecisionLabel(candidate: StudentProgressionCandidate): string {
    switch (this.getAnnualDecisionKey(candidate)) {
      case 'promote': return 'جاهز للترقية';
      case 'graduate': return 'جاهز للتخرج';
      case 'retain': return 'إبقاء في نفس الصف';
      default: return 'بانتظار اكتمال النتيجة';
    }
  }

  getAnnualDecisionClass(candidate: StudentProgressionCandidate): string {
    switch (this.getAnnualDecisionKey(candidate)) {
      case 'promote': return 'decision-badge decision-promote';
      case 'graduate': return 'decision-badge decision-graduate';
      case 'retain': return 'decision-badge decision-retain';
      default: return 'decision-badge decision-pending';
    }
  }

  getLifecycleStatusLabel(candidate: StudentProgressionCandidate): string {
    switch (candidate.studentLifecycleStatus) {
      case 2: return 'متخرج';
      case 3: return 'منقول خارج المدرسة';
      case 4: return 'منسحب';
      default: return 'نشط';
    }
  }

  getFailedSubjectNames(candidate: StudentProgressionCandidate): string {
    const failed = candidate.subjectGrades
      .filter(subject => subject.isPublished && !subject.isPassed)
      .map(subject => subject.subjectName);

    return failed.length ? failed.join('، ') : 'لا توجد مواد راسبة';
  }

  getPendingReason(candidate: StudentProgressionCandidate): string {
    if (candidate.academicStatus === 'NoGrades') return 'لم يتم حساب درجات نهائية لهذا الطالب بعد.';
    if (candidate.academicStatus === 'Unpublished') return 'توجد درجات ناقصة أو غير منشورة في الترمين.';
    return '';
  }

  getActionGuidanceTitle(): string {
    if (this.action() === 1) return 'تنفيذ الترقية';
    if (this.action() === 2) return 'تنفيذ الإبقاء';
    return 'تنفيذ التخرج';
  }

  getActionGuidanceText(): string {
    if (this.action() === 1) {
      return 'سيتم إغلاق قيد السنة الحالية وفتح قيد جديد في الصف التالي داخل السنة الدراسية الهدف. التنفيذ مسموح للطلاب الناجحين فقط.';
    }

    if (this.action() === 2) {
      return 'سيتم إغلاق قيد السنة الحالية وفتح قيد جديد في نفس الصف داخل السنة الدراسية الهدف. التنفيذ مسموح للطلاب الراسبين فقط.';
    }

    return 'سيتم إغلاق القيد الحالي وتحويل حالة الطالب إلى متخرج، مع بقاء حساب الطالب وولي الأمر فعالين داخل النظام.';
  }

  getExecuteButtonLabel(): string {
    if (this.isExecuting()) return 'جارٍ تنفيذ العملية...';
    if (this.action() === 1) return 'ترقية الطلاب المحددين';
    if (this.action() === 2) return 'إبقاء الطلاب المحددين';
    return 'تخريج الطلاب المحددين';
  }

  getStatusLabel(status: AcademicStatus): string {
    switch (status) {
      case 'Passed': return 'ناجح';
      case 'Failed': return 'راسب';
      case 'Unpublished': return 'غير منشورة';
      default: return 'بدون درجات';
    }
  }

  getStatusBadgeClass(status: AcademicStatus): string {
    switch (status) {
      case 'Passed': return 'bg-emerald-50 text-emerald-800 border-emerald-200';
      case 'Failed': return 'bg-red-50 text-red-800 border-red-200';
      case 'Unpublished': return 'bg-amber-50 text-amber-800 border-amber-200';
      default: return 'bg-gray-100 text-gray-800 border-gray-200';
    }
  }

  getTermLabel(term: AcademicTermLabel | null | undefined): string {
    if (term === 'FirstSemester') return 'الترم الأول';
    if (term === 'SecondSemester') return 'الترم الثاني';
    return '—';
  }

  getScopeLabel(scope: ProgressionTermScope): string {
    return scope === 1 ? 'الترم الأول' : scope === 2 ? 'الترم الثاني' : 'الترمين';
  }

  toggleSubjectDetails(enrollmentId: number) {
    const next = new Set(this.expandedSubjectIds());
    if (next.has(enrollmentId)) {
      next.delete(enrollmentId);
    } else {
      next.add(enrollmentId);
    }
    this.expandedSubjectIds.set(next);
  }

  isSubjectDetailsOpen(enrollmentId: number): boolean {
    return this.expandedSubjectIds().has(enrollmentId);
  }

  onActionChanged(action: ProgressionAction) {
    this.action.set(action);
    this.selectedTargetClassId.set(null);
    this.classMappings.set({});

    if (action === 3) {
      this.selectedTargetAcademicYearId.set(null);
    } else if (!this.futureAcademicYears().some(year => year.id === this.selectedTargetAcademicYearId())) {
      this.selectedTargetAcademicYearId.set(this.futureAcademicYears()[0]?.id ?? null);
    }

    this.autoMapTargetClasses();
  }

  onTargetAcademicYearChanged(value: number | null) {
    this.selectedTargetAcademicYearId.set(value);
    this.selectedTargetClassId.set(null);
    this.classMappings.set({});
    this.autoMapTargetClasses();
  }

  setClassMapping(sourceClassId: number, targetClassId: number | null) {
    const normalizedTargetClassId = targetClassId ? Number(targetClassId) : null;
    this.classMappings.update(current => ({
      ...current,
      [sourceClassId]: normalizedTargetClassId
    }));
  }

  autoMapTargetClasses() {
    if (this.action() === 3 || !this.selectedTargetAcademicYearId()) {
      this.classMappings.set({});
      return;
    }

    const sourceClasses = this.sourceClassesForMapping();
    const targetClasses = this.availableTargetClasses();
    if (!sourceClasses.length || !targetClasses.length) {
      this.classMappings.set({});
      return;
    }

    const usedTargetIds = new Set<number>();
    const nextMappings: ClassMappingState = {};

    sourceClasses.forEach((sourceClass, index) => {
      const sourceSection = this.extractClassSectionKey(sourceClass.name);
      let match = targetClasses.find(target =>
        !usedTargetIds.has(target.id) &&
        sourceSection &&
        this.extractClassSectionKey(target.name) === sourceSection);

      if (!match) {
        match = targetClasses.find(target =>
          !usedTargetIds.has(target.id) &&
          target.name.trim().toLowerCase() === sourceClass.name.trim().toLowerCase());
      }

      if (!match && targetClasses[index] && !usedTargetIds.has(targetClasses[index].id)) {
        match = targetClasses[index];
      }

      nextMappings[sourceClass.id] = match?.id ?? null;
      if (match) usedTargetIds.add(match.id);
    });

    this.classMappings.set(nextMappings);
  }

  private buildClassMappings() {
    const mappings = this.classMappings();
    const selectedSourceClassIds = Array.from(new Set(
      this.selectedCandidates().map(candidate => candidate.currentClassId)
    ));

    return selectedSourceClassIds.map(sourceClassId => ({
      sourceClassId,
      targetClassId: mappings[sourceClassId]!
    }));
  }

  private extractClassSectionKey(className: string): string {
    const trimmed = className.trim();
    const slashParts = trimmed.split(/[\/\\]/).map(part => part.trim()).filter(Boolean);
    if (slashParts.length > 1) return slashParts[slashParts.length - 1];

    const trailingNumber = trimmed.match(/(\d+)\s*$/);
    if (trailingNumber) return trailingNumber[1];

    const letterPart = trimmed.match(/([A-Za-z\u0600-\u06FF]+)\s*$/);
    return letterPart?.[1]?.toLowerCase() ?? trimmed.toLowerCase();
  }

  toggleCandidateSelection(enrollmentId: number, checked: boolean) {
    const selected = new Set(this.selectedEnrollmentIds());

    if (checked) {
      selected.add(enrollmentId);
    } else {
      selected.delete(enrollmentId);
    }

    this.selectedEnrollmentIds.set(Array.from(selected));
  }

  toggleAllVisibleCandidates(checked: boolean) {
    const visibleIds = this.filteredCandidates().map(candidate => candidate.enrollmentId);
    const selected = new Set(this.selectedEnrollmentIds());

    for (const id of visibleIds) {
      if (checked) {
        selected.add(id);
      } else {
        selected.delete(id);
      }
    }

    this.selectedEnrollmentIds.set(Array.from(selected));
  }

  clearSelection() {
    this.selectedEnrollmentIds.set([]);
  }

  execute() {
    if (!this.canExecute()) {
      this.showError('أكمل البيانات المطلوبة وحدد الطلاب قبل التنفيذ.');
      return;
    }

    const request: StudentProgressionRequest = {
      enrollmentIds: this.selectedEnrollmentIds(),
      action: this.action(),
      effectiveDate: this.effectiveDate(),
      passingThreshold: this.threshold(),
      note: this.note().trim() || undefined,
      targetAcademicYearId: this.action() === 3 ? null : this.selectedTargetAcademicYearId(),
      targetClassId: null,
      classMappings: this.action() === 3 ? [] : this.buildClassMappings()
    };

    this.isExecuting.set(true);
    this.clearMessages();

    this.studentProgressionService.execute(request)
      .pipe(finalize(() => this.isExecuting.set(false)))
      .subscribe({
        next: res => {
          const result = this.unwrapData<StudentProgressionResult>(res);
          if (!result) {
            this.showError('تم استلام استجابة غير متوقعة من الخادم.');
            return;
          }

          this.lastResult.set(result);
          this.successMessage.set(res?.message || this.buildExecutionMessage(result));

          if (result.failureCount > 0) {
            const failedIds = new Set(result.failures.map(item => item.enrollmentId));
            this.candidates.set(this.candidates().filter(candidate => failedIds.has(candidate.enrollmentId)));
            this.selectedEnrollmentIds.set(Array.from(failedIds));
          } else {
            this.candidates.set([]);
            this.selectedEnrollmentIds.set([]);
          }
        },
        error: err => this.showError(this.extractErrorMessage(err, 'تعذر تنفيذ العملية المطلوبة.'))
      });
  }

  isCandidateSelected(enrollmentId: number): boolean {
    return this.selectedEnrollmentIds().includes(enrollmentId);
  }

  getActionLabel(action: ProgressionAction): string {
    if (action === 1) return 'ترقية';
    if (action === 2) return 'إبقاء';
    return 'تخرج';
  }

  getClassOptionLabel(cls: ClassEntity): string {
    return cls.capacity ? `${cls.name} - السعة ${cls.capacity}` : cls.name;
  }

  trackByEnrollmentId(_: number, candidate: StudentProgressionCandidate): number {
    return candidate.enrollmentId;
  }

  private unwrapData<T>(response: any): T | null {
    if (response === null || response === undefined) return null;
    return (response.data ?? response) as T;
  }

  private extractErrorMessage(err: any, fallback: string): string {
    return err?.error?.message || err?.message || fallback;
  }

  private showError(message: string) {
    this.errorMessage.set(message);
    this.successMessage.set('');
  }

  private clearMessages() {
    this.errorMessage.set('');
    this.successMessage.set('');
  }

  private buildExecutionMessage(result: StudentProgressionResult): string {
    if (result.failureCount === 0) {
      return `تم تنفيذ العملية بنجاح على ${result.successCount} طالب.`;
    }

    return `تمت معالجة ${result.successCount} طالب، وتعذر تنفيذ العملية على ${result.failureCount} طالب.`;
  }

  private getTodayDate(): string {
    return new Date().toISOString().split('T')[0];
  }
}
