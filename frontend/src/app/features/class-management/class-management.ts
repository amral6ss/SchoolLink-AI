import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AcademicYear, AcademicYearService } from '../../core/services/academic-year.service';
import {
  ClassStudentBrowserItem,
  ClassStudentsBrowserResult,
  ClassStudentsBrowserService
} from '../../core/services/class-students-browser.service';
import {
  ClassEntity,
  ClassService,
  CopyClassesFromYearResult
} from '../../core/services/class.service';
import { GradeLevel, GradeLevelService } from '../../core/services/grade-level.service';
import {
  ClassEnrollmentPickerService,
  ClassPickerStudent
} from '../../core/services/class-enrollment-picker.service';
import { Sidebar } from '../../layouts/sidebar/sidebar';
@Component({
  selector: 'app-class-management',
  standalone: true,
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './class-management.html',
  styleUrl: './class-management.css',
})
export class ClassManagement implements OnInit {
  sidebarOpen = signal(false);
  displayUserName = localStorage.getItem('fullName') || localStorage.getItem('username') || 'المشرف';

  private classService = inject(ClassService);
  private classStudentsBrowserService = inject(ClassStudentsBrowserService);
  private gradeLevelService = inject(GradeLevelService);
  private academicYearService = inject(AcademicYearService);
  private pickerService = inject(ClassEnrollmentPickerService);

  classes = signal<ClassEntity[]>([]);
  gradeLevels = signal<GradeLevel[]>([]);
  academicYears = signal<AcademicYear[]>([]);

  currentPage = signal(1);
  itemsPerPage = signal(10);

  paginatedClasses = computed(() => {
    const start = (this.currentPage() - 1) * this.itemsPerPage();
    return this.classes().slice(start, start + this.itemsPerPage());
  });

  totalPages = computed(() => {
    return Math.max(1, Math.ceil(this.classes().length / this.itemsPerPage()));
  });

  rangeStart = computed(() => {
    if (this.classes().length === 0) return 0;
    return (this.currentPage() - 1) * this.itemsPerPage() + 1;
  });

  rangeEnd = computed(() => {
    return Math.min(this.currentPage() * this.itemsPerPage(), this.classes().length);
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

  selectedGradeFilter: number | null = null;
  selectedYearFilter: number | null = null;

  editingClassId = signal<number | null>(null);

  showStudentsModal = signal(false);
  studentsModalLoading = signal(false);
  studentsModalError = signal('');
  selectedClassForStudents = signal<ClassEntity | null>(null);
  studentsBrowserResult = signal<ClassStudentsBrowserResult | null>(null);
  studentsCurrentPage = signal(1);
  studentsItemsPerPage = signal(10);
  studentSearchTerm = '';

  errorMessage = signal('');
  successMessage = signal('');
  deleteClassConfirmId = signal<number | null>(null);

  newClass: Partial<ClassEntity> = {
    name: '',
    gradeLevelId: 0,
    academicYearId: 0,
    capacity: null,
    status: 1
  };

  copySourceAcademicYearId: number | null = null;
  copyTargetAcademicYearId: number | null = null;
  copyPreview = signal<CopyClassesFromYearResult | null>(null);
  copyLoading = signal(false);

  classStudents = computed<ClassStudentBrowserItem[]>(() => {
    return this.studentsBrowserResult()?.students.items ?? [];
  });

  totalStudentPages = computed(() => {
    return Math.max(1, this.studentsBrowserResult()?.students.totalPages ?? 1);
  });

  studentRangeStart = computed(() => {
    const totalCount = this.studentsBrowserResult()?.students.totalCount ?? 0;
    if (totalCount === 0) return 0;
    return (this.studentsCurrentPage() - 1) * this.studentsItemsPerPage() + 1;
  });

  studentRangeEnd = computed(() => {
    const totalCount = this.studentsBrowserResult()?.students.totalCount ?? 0;
    return Math.min(this.studentsCurrentPage() * this.studentsItemsPerPage(), totalCount);
  });

  studentFilteredCount = computed(() => this.studentsBrowserResult()?.filteredStudentsCount ?? 0);

  studentPages = computed<(number | string)[]>(() => {
    const total = this.totalStudentPages();
    const current = this.studentsCurrentPage();
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

  studentTrackByPageIndex = (_: number, item: number | string) =>
    typeof item === 'string' ? `dot-${_}` : `page-${item}`;

  ngOnInit() {
    this.loadAcademicYears();
    this.loadGradeLevels();
  }

  loadAcademicYears() {
    this.academicYearService.getAll().subscribe({
      next: (data) => {
        const d = data.data ?? data;
        this.academicYears.set(d);
        const activeYear = d.find((y: any) => y.isCurrent);
        if (activeYear) {
          this.newClass.academicYearId = activeYear.id;
          this.selectedYearFilter = activeYear.id;
        }
        this.loadClasses();
      },
      error: (err) => {
        console.error('Failed to load academic years', err);
        this.showError('فشل في تحميل السنوات الدراسية.');
        this.loadClasses();
      }
    });
  }

  loadGradeLevels() {
    this.gradeLevelService.getAll().subscribe({
      next: (data) => this.gradeLevels.set(data.data ?? data),
      error: (err) => {
        console.error('Failed to load grade levels', err);
        this.showError('فشل في تحميل المراحل الدراسية.');
      }
    });
  }

  loadClasses() {
    const filter: any = {};
    if (this.selectedGradeFilter) filter.gradeLevelId = this.selectedGradeFilter;
    if (this.selectedYearFilter) filter.academicYearId = this.selectedYearFilter;

    this.classService.getAll(filter).subscribe({
      next: (data) => {
        this.classes.set(data.data ?? data);
        this.currentPage.set(1);
      },
      error: (err) => {
        console.error('Failed to load classes', err);
        this.showError('فشل في تحميل الفصول الدراسية.');
      }
    });
  }

  onFilterChange() {
    this.loadClasses();
  }

  editClass(cls: ClassEntity) {
    this.editingClassId.set(cls.id);
    this.newClass = {
      name: cls.name,
      gradeLevelId: cls.gradeLevelId,
      academicYearId: cls.academicYearId,
      capacity: cls.capacity ?? null,
      status: cls.status ?? 1
    };

    setTimeout(() => {
      document.getElementById('classEditorSection')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  }

  cancelEdit() {
    this.editingClassId.set(null);
    const yearId = this.selectedYearFilter
      ?? this.academicYears().find(y => y.isCurrent)?.id
      ?? 0;
    this.newClass = { name: '', gradeLevelId: 0, academicYearId: yearId, capacity: null, status: 1 };
  }

  saveClass() {
    if (!this.newClass.name?.trim() || !this.newClass.gradeLevelId || !this.newClass.academicYearId) return;

    if (this.editingClassId()) {
      const { name, gradeLevelId, academicYearId, capacity, status } = this.newClass;
      this.classService.update(this.editingClassId()!, {
        name,
        gradeLevelId,
        academicYearId,
        capacity: capacity || null,
        status: status ?? 1
      }).subscribe({
        next: () => {
          this.loadClasses();
          this.cancelEdit();
          this.showSuccess('تم تحديث الفصل بنجاح!');
        },
        error: (err) => {
          console.error('Update failed', err);
          this.showError('فشل في تحديث الفصل. حاول مرة أخرى.');
        }
      });
    } else {
      this.classService.create({
        name: this.newClass.name,
        gradeLevelId: this.newClass.gradeLevelId,
        academicYearId: this.newClass.academicYearId,
        capacity: this.newClass.capacity || null,
        status: this.newClass.status ?? 1
      }).subscribe({
        next: () => {
          this.loadClasses();
          this.cancelEdit();
          this.showSuccess('تم إضافة الفصل بنجاح!');
        },
        error: (err) => {
          console.error('Create failed', err);
          this.showError('فشل في إضافة الفصل. حاول مرة أخرى.');
        }
      });
    }
  }

  deleteClass(id: number) {
    this.deleteClassConfirmId.set(id);
  }

  cancelDeleteClass() {
    this.deleteClassConfirmId.set(null);
  }

  confirmDeleteClass() {
    const id = this.deleteClassConfirmId();
    if (!id) return;
    this.deleteClassConfirmId.set(null);
    this.classService.delete(id).subscribe({
      next: () => {
        this.loadClasses();
        this.showSuccess('تم حذف الفصل بنجاح!');
      },
      error: (err) => {
        console.error('Delete failed', err);
        this.showError('فشل في حذف الفصل. حاول مرة أخرى.');
      }
    });
  }

  openStudentsModal(cls: ClassEntity) {
    this.selectedClassForStudents.set(cls);
    this.showStudentsModal.set(true);
    this.studentsModalError.set('');
    this.studentsBrowserResult.set(null);
    this.studentSearchTerm = '';
    this.studentsCurrentPage.set(1);
    this.studentsItemsPerPage.set(10);
    this.loadClassStudents();
  }

  loadClassStudents() {
    const selectedClass = this.selectedClassForStudents();
    if (!selectedClass) return;

    this.studentsModalLoading.set(true);
    this.studentsModalError.set('');

    this.classStudentsBrowserService.getClassStudents(selectedClass.id, {
      academicYearId: selectedClass.academicYearId,
      page: this.studentsCurrentPage(),
      pageSize: this.studentsItemsPerPage(),
      searchTerm: this.studentSearchTerm
    }).subscribe({
      next: (result) => {
        this.studentsBrowserResult.set(result);
        this.studentsCurrentPage.set(result.students.page);
        this.studentsItemsPerPage.set(result.students.pageSize);
        this.studentsModalLoading.set(false);
      },
      error: (err) => {
        console.error('Failed to load class students', err);
        this.studentsModalError.set('فشل في تحميل طلاب الفصل. حاول مرة أخرى.');
        this.studentsModalLoading.set(false);
      }
    });
  }

  closeStudentsModal() {
    this.showStudentsModal.set(false);
    this.studentsModalLoading.set(false);
    this.studentsModalError.set('');
    this.selectedClassForStudents.set(null);
    this.studentsBrowserResult.set(null);
    this.studentSearchTerm = '';
    this.studentsCurrentPage.set(1);
    this.studentsItemsPerPage.set(10);
  }

  applyStudentSearch() {
    this.studentsCurrentPage.set(1);
    this.loadClassStudents();
  }

  clearStudentSearch() {
    this.studentSearchTerm = '';
    this.studentsCurrentPage.set(1);
    this.loadClassStudents();
  }

  onStudentsPageSizeChange() {
    this.studentsCurrentPage.set(1);
    this.loadClassStudents();
  }

  nextStudentsPage() {
    if (this.studentsCurrentPage() < this.totalStudentPages()) {
      this.studentsCurrentPage.update(page => page + 1);
      this.loadClassStudents();
    }
  }

  prevStudentsPage() {
    if (this.studentsCurrentPage() > 1) {
      this.studentsCurrentPage.update(page => page - 1);
      this.loadClassStudents();
    }
  }

  goToStudentsPage(page: number) {
    if (page < 1 || page > this.totalStudentPages() || page === this.studentsCurrentPage()) {
      return;
    }

    this.studentsCurrentPage.set(page);
    this.loadClassStudents();
  }

  getStudentsRangeStart(): number {
    const totalCount = this.studentsBrowserResult()?.students.totalCount ?? 0;
    if (totalCount === 0) return 0;

    return (this.studentsCurrentPage() - 1) * this.studentsItemsPerPage() + 1;
  }

  getStudentsRangeEnd(): number {
    const totalCount = this.studentsBrowserResult()?.students.totalCount ?? 0;
    return Math.min(this.studentsCurrentPage() * this.studentsItemsPerPage(), totalCount);
  }

  getStudentsTotalCount(): number {
    return this.studentsBrowserResult()?.totalStudents ?? 0;
  }

  getStudentsFilteredCount(): number {
    return this.studentsBrowserResult()?.filteredStudentsCount ?? 0;
  }

  hasStudentSearch(): boolean {
    return this.studentSearchTerm.trim().length > 0;
  }

  getGenderLabel(gender?: number | null): string {
    if (gender === 1) return 'ذكر';
    if (gender === 2) return 'أنثى';
    return 'غير محدد';
  }

  formatDisplayDate(dateValue?: string | null): string {
    if (!dateValue) return '—';

    const date = new Date(dateValue);
    if (isNaN(date.getTime())) return '—';

    return new Intl.DateTimeFormat('ar-EG', {
      year: 'numeric',
      month: 'short',
      day: 'numeric'
    }).format(date);
  }

  getClassStatusLabel(status?: number | null): string {
    if (status === 2) return 'مغلق';
    if (status === 3) return 'مؤرشف';
    return 'نشط';
  }

  previewCopyClasses() {
    if (!this.copySourceAcademicYearId || !this.copyTargetAcademicYearId) {
      this.showError('اختر السنة المصدر والسنة الهدف أولاً.');
      return;
    }

    this.copyLoading.set(true);
    this.copyPreview.set(null);
    this.classService.previewCopyFromYear({
      sourceAcademicYearId: this.copySourceAcademicYearId,
      targetAcademicYearId: this.copyTargetAcademicYearId
    }).subscribe({
      next: (res) => {
        this.copyPreview.set(res.data ?? res);
        this.copyLoading.set(false);
      },
      error: (err) => {
        console.error('Copy preview failed', err);
        this.copyLoading.set(false);
        this.showError(err?.error?.message || 'فشل في معاينة نسخ الفصول.');
      }
    });
  }

  executeCopyClasses() {
    if (!this.copySourceAcademicYearId || !this.copyTargetAcademicYearId) {
      this.showError('اختر السنة المصدر والسنة الهدف أولاً.');
      return;
    }

    this.copyLoading.set(true);
    this.classService.copyFromYear({
      sourceAcademicYearId: this.copySourceAcademicYearId,
      targetAcademicYearId: this.copyTargetAcademicYearId
    }).subscribe({
      next: (res) => {
        this.copyPreview.set(res.data ?? res);
        this.copyLoading.set(false);
        this.selectedYearFilter = this.copyTargetAcademicYearId;
        this.loadClasses();
        this.showSuccess(res.message || 'تم نسخ الفصول بنجاح.');
      },
      error: (err) => {
        console.error('Copy classes failed', err);
        this.copyLoading.set(false);
        this.showError(err?.error?.message || 'فشل في نسخ الفصول.');
      }
    });
  }

  private showError(msg: string) {
    this.errorMessage.set(msg);
    this.successMessage.set('');
    setTimeout(() => this.errorMessage.set(''), 4000);
  }

  private showSuccess(msg: string) {
    this.successMessage.set(msg);
    this.errorMessage.set('');
    setTimeout(() => this.successMessage.set(''), 3000);
  }

  // ─── Class Enrollment Picker ─────────────────────────────────────────────────

  showPickerModal          = signal(false);
  pickerLoading            = signal(false);
  pickerError              = signal('');
  selectedClassForPicker   = signal<ClassEntity | null>(null);

  availableStudents        = signal<ClassPickerStudent[]>([]);
  availableTotal           = signal(0);
  availablePage            = signal(1);
  availablePageSize        = signal(20);

  pickerSearchTerm         = '';
  birthDateFrom            = signal('');
  birthDateTo              = signal('');
  sortBy                   = signal('name');
  sortDescending           = signal(false);

  selectedStudentIds       = signal<Set<number>>(new Set());
  enrollDate               = '';
  enrolling                = signal(false);
  enrollError              = signal('');
  enrollSuccess            = signal('');

  availableTotalPages = computed(() =>
    Math.max(1, Math.ceil(this.availableTotal() / this.availablePageSize()))
  );

  pickerRangeStart = computed(() => {
    if (this.availableTotal() === 0) return 0;
    return (this.availablePage() - 1) * this.availablePageSize() + 1;
  });

  pickerRangeEnd = computed(() =>
    Math.min(this.availablePage() * this.availablePageSize(), this.availableTotal())
  );

  openPickerModal(cls: ClassEntity): void {
    this.selectedClassForPicker.set(cls);
    this.showPickerModal.set(true);
    this.pickerError.set('');
    this.enrollError.set('');
    this.enrollSuccess.set('');
    this.availableStudents.set([]);
    this.availableTotal.set(0);
    this.availablePage.set(1);
    this.pickerSearchTerm = '';
    this.birthDateFrom.set('');
    this.birthDateTo.set('');
    this.sortBy.set('name');
    this.sortDescending.set(false);
    this.selectedStudentIds.set(new Set());
    const today = new Date();
    this.enrollDate = today.toISOString().split('T')[0];
    this.loadAvailableStudents();
  }

  closePickerModal(): void {
    this.showPickerModal.set(false);
    this.pickerLoading.set(false);
    this.pickerError.set('');
    this.enrollError.set('');
    this.enrollSuccess.set('');
    this.selectedClassForPicker.set(null);
    this.availableStudents.set([]);
    this.selectedStudentIds.set(new Set());
  }

  loadAvailableStudents(): void {
    const cls = this.selectedClassForPicker();
    if (!cls) return;

    this.pickerLoading.set(true);
    this.pickerError.set('');

    this.pickerService.getAvailableStudents(cls.id, {
      page:           this.availablePage(),
      pageSize:       this.availablePageSize(),
      searchTerm:     this.pickerSearchTerm || undefined,
      birthDateFrom:  this.birthDateFrom() || undefined,
      birthDateTo:    this.birthDateTo()   || undefined,
      sortBy:         this.sortBy(),
      sortDescending: this.sortDescending()
    }).subscribe({
      next: (res) => {
        this.availableStudents.set(res.data?.items as ClassPickerStudent[] ?? []);
        this.availableTotal.set(res.data?.totalCount ?? 0);
        this.availablePage.set(res.data?.page ?? 1);
        this.pickerLoading.set(false);
      },
      error: () => {
        this.pickerError.set('فشل في تحميل الطلاب المتاحين. حاول مرة أخرى.');
        this.pickerLoading.set(false);
      }
    });
  }

  applyPickerSearch(): void {
    this.availablePage.set(1);
    this.loadAvailableStudents();
  }

  clearPickerSearch(): void {
    this.pickerSearchTerm = '';
    this.availablePage.set(1);
    this.loadAvailableStudents();
  }

  applyPickerDateFilter(): void {
    this.availablePage.set(1);
    this.loadAvailableStudents();
  }

  clearPickerDateFilter(): void {
    this.birthDateFrom.set('');
    this.birthDateTo.set('');
    this.availablePage.set(1);
    this.loadAvailableStudents();
  }

  changeSortBy(value: string): void {
    if (this.sortBy() === value) {
      this.sortDescending.update(d => !d);
    } else {
      this.sortBy.set(value);
      this.sortDescending.set(false);
    }
    this.availablePage.set(1);
    this.loadAvailableStudents();
  }

  toggleStudentSelection(id: number): void {
    this.selectedStudentIds.update(set => {
      const next = new Set(set);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  isStudentSelected(id: number): boolean {
    return this.selectedStudentIds().has(id);
  }

  selectAllVisible(): void {
    this.selectedStudentIds.update(set => {
      const next = new Set(set);
      this.availableStudents().forEach(s => next.add(s.id));
      return next;
    });
  }

  clearSelection(): void {
    this.selectedStudentIds.set(new Set());
  }

  nextPickerPage(): void {
    if (this.availablePage() < this.availableTotalPages()) {
      this.availablePage.update(p => p + 1);
      this.loadAvailableStudents();
    }
  }

  prevPickerPage(): void {
    if (this.availablePage() > 1) {
      this.availablePage.update(p => p - 1);
      this.loadAvailableStudents();
    }
  }

  confirmBulkEnroll(): void {
    const ids = Array.from(this.selectedStudentIds());
    if (ids.length === 0) {
      this.enrollError.set('الرجاء اختيار طالب واحد على الأقل.');
      return;
    }
    if (!this.enrollDate) {
      this.enrollError.set('الرجاء تحديد تاريخ التسجيل.');
      return;
    }

    const cls = this.selectedClassForPicker();
    if (!cls) return;

    this.enrolling.set(true);
    this.enrollError.set('');
    this.enrollSuccess.set('');

    this.pickerService.bulkEnroll({
      classId:    cls.id,
      studentIds: ids,
      enrolledAt: this.enrollDate
    }).subscribe({
      next: (res) => {
        this.enrolling.set(false);
        const d = res.data;
        if (d && d.failureCount > 0) {
          this.enrollError.set(
            `تم تسجيل ${d.successCount} طالب. فشل ${d.failureCount} طالب.`
          );
        } else {
          this.enrollSuccess.set(res.message ?? 'تم التسجيل بنجاح.');
        }
        this.selectedStudentIds.set(new Set());
        this.availablePage.set(1);
        this.loadAvailableStudents();
      },
      error: () => {
        this.enrolling.set(false);
        this.enrollError.set('فشل في تسجيل الطلاب. حاول مرة أخرى.');
      }
    });
  }
}
