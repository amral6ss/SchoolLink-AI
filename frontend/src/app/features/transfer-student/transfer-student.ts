import { Component, signal, computed, inject, OnInit, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
import { AcademicYear, AcademicYearService } from '../../core/services/academic-year.service';
import { ClassEntity, ClassService } from '../../core/services/class.service';
import { GradeLevel, GradeLevelService } from '../../core/services/grade-level.service';
import { EnrollmentService, Enrollment, TransferHistory, GetEnrollmentsFilter } from '../../core/services/enrollment.service';
import { PagedResult } from '../../core/models/api.model';
import { Sidebar } from '../../layouts/sidebar/sidebar';
@Component({
  selector: 'app-transfer-student',
  standalone: true,
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './transfer-student.html',
  styleUrl: './transfer-student.css'
})
export class TransferStudent implements OnInit {
  sidebarOpen = signal(false);
  displayUserName = localStorage.getItem('fullName') || localStorage.getItem('username') || 'المشرف';

  private enrollmentService = inject(EnrollmentService);
  private classService = inject(ClassService);
  private academicYearService = inject(AcademicYearService);
  private gradeLevelService = inject(GradeLevelService);

  allClasses = signal<ClassEntity[]>([]);
  academicYears = signal<AcademicYear[]>([]);
  gradeLevels = signal<GradeLevel[]>([]);
  enrollments = signal<Enrollment[]>([]);
  totalCount = signal(0);
  totalPages = signal(1);
  transferHistory = signal<TransferHistory[]>([]);

  selectedSourceGradeLevelId = signal<number | null>(null);
  selectedSourceClassId = signal<number | null>(null);
  selectedTargetClassId = signal<number | null>(null);
  transferReason = signal('');
  searchQuery = signal('');

  currentPage = signal(1);
  pageSize = signal(20);

  historyPage = signal(1);
  historyPageSize = signal(10);
  historyTotalCount = signal(0);
  historyTotalPages = signal(1);

  selectedEnrollmentIds = signal<number[]>([]);
  selectAllCurrentPage = signal(false);

  isBootstrapping = signal(false);
  isLoadingEnrollments = signal(false);
  isTransferring = signal(false);
  errorMessage = signal('');
  successMessage = signal('');
  lastTransfer = signal<{ count: number; fromClass: string; toClass: string } | null>(null);
  selectedReason = signal<string | null>(null);
  showHistory = signal(false);

  private historyLoadEffect = effect(() => {
    const show = this.showHistory();
    const yearId = this.currentAcademicYear()?.id;
    if (show && yearId) {
      this.loadTransferHistory();
    }
  });

  currentAcademicYear = computed(() =>
    this.academicYears().find(y => y.isCurrent) ?? this.academicYears()[0] ?? null
  );

  sourceClass = computed(() =>
    this.allClasses().find(c => c.id === this.selectedSourceClassId()) ?? null
  );

  filteredSourceClasses = computed(() => {
    const yearId = this.currentAcademicYear()?.id;
    const gradeId = this.selectedSourceGradeLevelId();
    if (!yearId || !gradeId) return [];
    return this.allClasses()
      .filter(c => c.academicYearId === yearId && c.gradeLevelId === gradeId)
      .sort((a, b) => a.name.localeCompare(b.name, 'ar'));
  });

  availableTargetClasses = computed(() => {
    const sc = this.sourceClass();
    if (!sc) return [];
    return this.allClasses()
      .filter(c =>
        c.id !== sc.id &&
        c.academicYearId === sc.academicYearId &&
        c.gradeLevelId === sc.gradeLevelId &&
        (c.status ?? 1) === 1)
      .sort((a, b) => a.name.localeCompare(b.name, 'ar'));
  });

  hasSelection = computed(() => this.selectedEnrollmentIds().length > 0);
  selectedCount = computed(() => this.selectedEnrollmentIds().length);

  canTransfer = computed(() =>
    this.hasSelection() &&
    !!this.selectedTargetClassId() &&
    this.selectedTargetClassId() !== this.selectedSourceClassId() &&
    !this.isTransferring()
  );

  getClassOptionLabel(cls: ClassEntity): string {
    return cls.capacity ? `${cls.name} - السعة ${cls.capacity}` : cls.name;
  }

  ngOnInit() {
    this.loadPageData();
  }

  loadPageData() {
    this.isBootstrapping.set(true);
    this.clearMessages();

    forkJoin({
      years: this.academicYearService.getAll(),
      grades: this.gradeLevelService.getAll(),
      classes: this.classService.getAll()
    }).pipe(finalize(() => this.isBootstrapping.set(false)))
    .subscribe({
      next: ({ years, grades, classes }) => {
        const unwrappedYears = this.unwrapData<AcademicYear[]>(years) ?? [];
        const unwrappedGrades = this.unwrapData<GradeLevel[]>(grades) ?? [];
        const unwrappedClasses = this.unwrapData<ClassEntity[]>(classes) ?? [];

        this.academicYears.set(unwrappedYears);
        this.gradeLevels.set(unwrappedGrades);
        this.allClasses.set(unwrappedClasses);
      },
      error: err => this.showError(this.extractError(err, 'تعذر تحميل بيانات الصفحة'))
    });
  }

  onSourceGradeLevelChange() {
    this.selectedSourceClassId.set(null);
    this.resetSelection();
    this.enrollments.set([]);
  }

  onSourceClassChange() {
    this.resetSelection();
    this.currentPage.set(1);
    this.loadEnrollments();
  }

  loadEnrollments() {
    const classId = this.selectedSourceClassId();
    const yearId = this.currentAcademicYear()?.id;

    if (!classId || !yearId) return;

    this.isLoadingEnrollments.set(true);
    this.clearMessages();

    const filter: GetEnrollmentsFilter & { classId: number } = {
      classId,
      academicYearId: yearId,
      page: this.currentPage(),
      pageSize: this.pageSize(),
      activeOnly: true,
      searchTerm: this.searchQuery().trim() || undefined
    };

    this.enrollmentService.getByClassPaged(filter)
      .pipe(finalize(() => this.isLoadingEnrollments.set(false)))
      .subscribe({
        next: (res) => {
          const result = this.unwrapData<PagedResult<Enrollment>>(res);
          if (result) {
            this.enrollments.set(result.items);
            this.totalCount.set(result.totalCount);
            this.totalPages.set(result.totalPages ?? 1);
          }
          this.updateSelectAllState();
        },
        error: err => this.showError(this.extractError(err, 'تعذر تحميل الطلاب'))
      });
  }

  onSearchChange() {
    this.currentPage.set(1);
    this.loadEnrollments();
  }

  onPageChange(page: number) {
    if (page >= 1 && page <= this.totalPages() && page !== this.currentPage()) {
      this.currentPage.set(page);
      this.loadEnrollments();
    }
  }

  onPageSizeChange() {
    this.currentPage.set(1);
    this.loadEnrollments();
  }

  toggleSelection(enrollmentId: number, checked: boolean) {
    const selected = new Set(this.selectedEnrollmentIds());
    if (checked) selected.add(enrollmentId);
    else selected.delete(enrollmentId);
    this.selectedEnrollmentIds.set(Array.from(selected));
  }

  toggleSelectAllCurrentPage(checked: boolean) {
    const ids = this.enrollments().map(e => e.id);
    const selected = new Set(this.selectedEnrollmentIds());
    if (checked) ids.forEach(id => selected.add(id));
    else ids.forEach(id => selected.delete(id));
    this.selectedEnrollmentIds.set(Array.from(selected));
    this.selectAllCurrentPage.set(checked);
  }

  private updateSelectAllState() {
    const currentIds = new Set(this.enrollments().map(e => e.id));
    const selectedIds = new Set(this.selectedEnrollmentIds());
    this.selectAllCurrentPage.set(currentIds.size > 0 && [...currentIds].every(id => selectedIds.has(id)));
  }

  private resetSelection() {
    this.selectedEnrollmentIds.set([]);
    this.selectAllCurrentPage.set(false);
    this.selectedTargetClassId.set(null);
    this.transferReason.set('');
    this.lastTransfer.set(null);
  }

  confirmTransfer() {
    if (!this.canTransfer()) {
      this.showError('يرجى تحديد طالب/طلاب واختيار الفصل الهدف');
      return;
    }

    const sourceClassName = this.sourceClass()?.name || '';
    const targetClassName = this.allClasses().find(c => c.id === this.selectedTargetClassId())?.name || '';
    const enrollmentIds = this.selectedEnrollmentIds();
    const yearId = this.currentAcademicYear()!.id;

    this.isTransferring.set(true);
    this.clearMessages();

    if (enrollmentIds.length === 1) {
      this.enrollmentService.transferStudent({
        currentEnrollmentId: enrollmentIds[0],
        newClassId: this.selectedTargetClassId()!,
        transferDate: new Date().toISOString().split('T')[0],
        transferReason: this.transferReason().trim() || undefined
      }).pipe(finalize(() => this.isTransferring.set(false)))
      .subscribe({
        next: (res) => {
          if (res && (res as any).isSuccess === false) {
            this.showError((res as any).message || 'فشل في عملية النقل');
            return;
          }
          this.onTransferSuccess(1, sourceClassName, targetClassName, yearId);
        },
        error: (err) => this.handleTransferError(err)
      });
    } else {
      this.enrollmentService.bulkTransfer({
        enrollmentIds,
        newClassId: this.selectedTargetClassId()!,
        transferDate: new Date().toISOString().split('T')[0],
        transferReason: this.transferReason().trim() || undefined
      }).pipe(finalize(() => this.isTransferring.set(false)))
      .subscribe({
        next: (res) => {
          const data = this.unwrapData<any>(res);
          if (res && (res as any).isSuccess === false) {
            this.showError((res as any).message || 'فشل في عملية النقل الجماعي');
            return;
          }
          const successCount = data?.successCount ?? this.selectedCount();
          this.onTransferSuccess(successCount, sourceClassName, targetClassName, yearId);
          if (data?.failureCount > 0) {
            this.showError(`تم نقل ${successCount} طالب، وفشل ${data.failureCount} طالب`);
          }
        },
        error: (err) => this.handleTransferError(err)
      });
    }
  }

  private onTransferSuccess(count: number, fromClass: string, toClass: string, yearId: number) {
    this.lastTransfer.set({ count, fromClass, toClass });
    this.successMessage.set(`تم نقل ${count} طالب بنجاح من "${fromClass}" إلى "${toClass}"`);
    this.loadEnrollments();
    this.historyPage.set(1);
    if (this.showHistory()) {
      this.loadTransferHistory();
    }
    this.resetSelection();
    setTimeout(() => {
      this.successMessage.set('');
      this.lastTransfer.set(null);
    }, 4000);
  }

  private handleTransferError(err: any) {
    const msg = err?.error?.message || err?.message || 'حدث خطأ أثناء النقل. حاول مرة أخرى.';
    this.showError(msg);
  }

  loadTransferHistory() {
    const yearId = this.currentAcademicYear()?.id;
    if (!yearId) return;

    this.enrollmentService.getTransferHistory(yearId, this.historyPage(), this.historyPageSize()).subscribe({
      next: res => {
        const paged = this.unwrapData<PagedResult<TransferHistory>>(res);
        if (paged) {
          this.transferHistory.set(paged.items);
          this.historyTotalCount.set(paged.totalCount);
          this.historyTotalPages.set(paged.totalPages ?? 1);
        } else {
          const flat = this.unwrapData<TransferHistory[]>(res);
          this.transferHistory.set(flat ?? []);
        }
      },
      error: err => console.error('Failed to load transfer history', err)
    });
  }

  onHistoryPageChange(page: number) {
    if (page >= 1 && page <= this.historyTotalPages() && page !== this.historyPage()) {
      this.historyPage.set(page);
      this.loadTransferHistory();
    }
  }

  isEnrollmentSelected(id: number): boolean {
    return this.selectedEnrollmentIds().includes(id);
  }

  trackByPageIndex = (_: number, item: number | string) =>
    typeof item === 'string' ? `dot-${_}` : `page-${item}`;

  trackByHistoryId(_: number, item: TransferHistory): number {
    return item.id;
  }

  trackById = (_: number, item: Enrollment): number => item.id;

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

  historyPages = computed<(number | string)[]>(() => {
    const total = this.historyTotalPages();
    const current = this.historyPage();
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

  rangeStart = computed(() => {
    if (this.totalCount() === 0) return 0;
    return (this.currentPage() - 1) * this.pageSize() + 1;
  });

  rangeEnd = computed(() => {
    return Math.min(this.currentPage() * this.pageSize(), this.totalCount());
  });

  private unwrapData<T>(response: any): T | null {
    if (!response) return null;
    return (response.data ?? response) as T;
  }

  private extractError(err: any, fallback: string): string {
    return err?.error?.message || err?.message || fallback;
  }

  private showError(msg: string) {
    this.errorMessage.set(msg);
    this.successMessage.set('');
  }

  private clearMessages() {
    this.errorMessage.set('');
    this.successMessage.set('');
  }
}
