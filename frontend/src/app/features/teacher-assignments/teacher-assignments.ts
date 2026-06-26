import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { ClassSubjectTeacherService, ClassSubjectTeacher } from '../../core/services/class-subject-teacher.service';
import { ClassService, ClassEntity } from '../../core/services/class.service';
import { SubjectService, Subject } from '../../core/services/subject.service';
import { TeacherService, Teacher } from '../../core/services/teacher.service';
import { AcademicYearService } from '../../core/services/academic-year.service';
import { GradeLevelService, GradeLevel } from '../../core/services/grade-level.service';
import { TimetableService, TimetableSlotDto } from '../../core/services/timetable.service';

const DAY_LABELS: Record<string, string> = {
  Sunday: 'الأحد',
  Monday: 'الإثنين',
  Tuesday: 'الثلاثاء',
  Wednesday: 'الأربعاء',
  Thursday: 'الخميس',
};

@Component({
  selector: 'app-teacher-assignments',
  standalone: true,
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './teacher-assignments.html',
  styleUrl: './teacher-assignments.css',
})
export class TeacherAssignments implements OnInit {
  sidebarOpen = signal(false);
  displayUserName = localStorage.getItem('fullName') || localStorage.getItem('username') || 'المشرف';

  private assignmentService = inject(ClassSubjectTeacherService);
  private classService = inject(ClassService);
  private subjectService = inject(SubjectService);
  private teacherService = inject(TeacherService);
  private academicYearService = inject(AcademicYearService);
  private gradeLevelService = inject(GradeLevelService);

  assignments = signal<ClassSubjectTeacher[]>([]);
  classes = signal<ClassEntity[]>([]);
  grades = signal<GradeLevel[]>([]);
  subjects = signal<Subject[]>([]);
  teachers = signal<Teacher[]>([]);

  currentPage = signal(1);
  itemsPerPage = signal(10);

  paginatedAssignments = computed(() => {
    const start = (this.currentPage() - 1) * this.itemsPerPage();
    return this.assignments().slice(start, start + this.itemsPerPage());
  });

  totalPages = computed(() => {
    return Math.max(1, Math.ceil(this.assignments().length / this.itemsPerPage()));
  });

  rangeStart = computed(() => {
    if (this.assignments().length === 0) return 0;
    return (this.currentPage() - 1) * this.itemsPerPage() + 1;
  });

  rangeEnd = computed(() => {
    return Math.min(this.currentPage() * this.itemsPerPage(), this.assignments().length);
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

  formSubjects = signal<Subject[]>([]);
  formTeachers = signal<Teacher[]>([]);
  noTeachersAvailable = signal(false);
  editFormTeachers = signal<Teacher[]>([]);
  editNoTeachersAvailable = signal(false);

  selectedNewGradeId: number | null = null;
  selectedFilterGradeId: number | null = null;

  formClasses = signal<ClassEntity[]>([]);
  filterClasses = signal<ClassEntity[]>([]);

  selectedClassFilter: number | null = null;

  currentAcademicYearId: number | null = null;
  currentAcademicYearName = signal('غير محددة');
  private availableTeachersRequestVersion = 0;
  private editTeachersRequestVersion = 0;

  newAssignment: Partial<ClassSubjectTeacher> = { classId: 0, subjectId: 0, teacherId: 0, weeklyPeriods: 1 };

  editingAssignmentId = signal<number | null>(null);
  editForm: { teacherId: number; weeklyPeriods: number } = { teacherId: 0, weeklyPeriods: 1 };

  isLoading = signal(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  removeAssignmentConfirmId = signal<number | null>(null);

  ngOnInit() {
    this.loadAcademicYear();
    this.loadGrades();
    this.loadSubjects();
    this.loadTeachers();
  }

  loadGrades() {
    this.gradeLevelService.getAll().subscribe({
      next: (data) => {
        const sortedGrades = (data.data ?? data).sort((a: any, b: any) => a.levelOrder - b.levelOrder);
        this.grades.set(sortedGrades);
      },
      error: () => this.showError('تعذر تحميل بيانات الصفوف الدراسية')
    });
  }

  loadAcademicYear() {
    this.academicYearService.getAll().subscribe({
      next: (years) => {
        const current = (years.data ?? years).find((y: any) => y.isCurrent);
        if (!current) {
          this.showError('تعذر تحديد السنة الدراسية الحالية');
          return;
        }

        this.currentAcademicYearId = current.id;
        this.currentAcademicYearName.set(current.name || 'السنة الحالية');
        this.loadClasses(current.id);
      },
      error: () => this.showError('تعذر تحميل السنة الدراسية الحالية')
    });
  }

  loadClasses(academicYearId?: number) {
    this.classService.getAll(academicYearId ? { academicYearId } : undefined).subscribe({
      next: (data) => this.classes.set(data.data ?? data),
      error: () => this.showError('تعذر تحميل بيانات الفصول')
    });
  }

  loadSubjects() {
    this.subjectService.getAll().subscribe({
      next: (data) => {
        this.subjects.set(data.data ?? data);
        this.formSubjects.set(data.data ?? data);
      },
      error: () => this.showError('تعذر تحميل بيانات المواد')
    });
  }

  loadTeachers() {
    this.teacherService.getAll(1000).subscribe({
      next: (res) => {
        const list = res.data?.items ?? [];
        this.teachers.set(list);
        this.formTeachers.set(list);
      },
      error: () => this.showError('تعذر تحميل بيانات المعلمين')
    });
  }

  onNewGradeChange() {
    this.newAssignment.classId = 0;
    if (this.selectedNewGradeId) {
      this.formClasses.set(this.classes().filter(c =>
        c.gradeLevelId === this.selectedNewGradeId &&
        (c.status ?? 1) === 1));
    } else {
      this.formClasses.set([]);
    }
    this.onNewClassChange();
  }

  onNewClassChange() {
    this.availableTeachersRequestVersion++;
    this.newAssignment.subjectId = 0;
    this.newAssignment.teacherId = 0;
    this.formSubjects.set(this.subjects());
    this.formTeachers.set([]);
    this.noTeachersAvailable.set(false);
  }

  onNewSubjectChange() {
    this.newAssignment.teacherId = 0;
    this.noTeachersAvailable.set(false);

    const classId = Number(this.newAssignment.classId);
    const subjectId = Number(this.newAssignment.subjectId);

    if (!classId || !subjectId || !this.currentAcademicYearId) {
      this.formTeachers.set([]);
      return;
    }

    const requestVersion = ++this.availableTeachersRequestVersion;

    this.assignmentService
      .getAvailableTeachers(subjectId, classId, this.currentAcademicYearId)
      .subscribe({
        next: (list) => {
          if (requestVersion !== this.availableTeachersRequestVersion) return;

          const data = list.data ?? list;
          if (data.length > 0) {
            this.formTeachers.set(data);
            this.noTeachersAvailable.set(false);
          } else {
            this.formTeachers.set([]);
            this.noTeachersAvailable.set(true);
          }
        },
        error: () => {
          if (requestVersion !== this.availableTeachersRequestVersion) return;
          this.formTeachers.set([]);
          this.noTeachersAvailable.set(false);
          this.showError('تعذر تحميل المعلمين المتاحين لهذه المادة');
        },
      });
  }

  onNewTeacherChange() {
  }

  loadAssignments() {
    if (!this.selectedClassFilter) {
      this.assignments.set([]);
      return;
    }

    this.isLoading.set(true);
    this.assignmentService.getByClass(this.selectedClassFilter, this.currentAcademicYearId ?? undefined).subscribe({
      next: (data) => {
        const items = data.data ?? data;
        this.assignments.set(items);
        this.currentPage.set(1);
        this.isLoading.set(false);
        for (const a of items) this.loadDaysForAssignment(a);
      },
      error: () => {
        this.assignments.set([]);
        this.isLoading.set(false);
        this.showError('تعذر تحميل التعيينات');
      }
    });
  }

  onFilterGradeChange() {
    this.selectedClassFilter = null;
    if (this.selectedFilterGradeId) {
      this.filterClasses.set(this.classes().filter(c => c.gradeLevelId === this.selectedFilterGradeId));
    } else {
      this.filterClasses.set([]);
    }
    this.onClassFilterChange();
  }

  onClassFilterChange() {
    this.loadAssignments();
  }

  getTeacherName(teacherId: number): string {
    return this.teachers().find(t => t.id == teacherId)?.fullName || 'غير محدد';
  }

  getSubjectName(subjectId: number): string {
    return this.subjects().find(s => s.id == subjectId)?.name || 'غير محدد';
  }

  getClassName(classId: number): string {
    const c = this.classes().find(c => c.id == classId);
    if (!c) return 'غير محدد';
    return c.gradeLevelName ? `${c.gradeLevelName} - ${c.name}` : c.name;
  }

  getSelectedClassName(): string {
    if (!this.selectedClassFilter) return 'لم يتم اختيار فصل';
    return this.getClassName(this.selectedClassFilter);
  }

  getAssignmentsCount(): number {
    return this.assignments().length;
  }

  getTeachersCount(): number {
    return this.teachers().length;
  }

  canAssign(): boolean {
    return !!this.newAssignment.classId
      && !!this.newAssignment.subjectId
      && !!this.newAssignment.teacherId
      && !!this.currentAcademicYearId
      && !this.noTeachersAvailable();
  }

  private normalizeWeeklyPeriods(value: number): number {
    const parsed = Number(value);
    if (!Number.isFinite(parsed) || parsed < 1) {
      return 1;
    }

    return Math.trunc(parsed);
  }

  assignTeacher() {
    if (!this.newAssignment.classId || !this.newAssignment.subjectId || !this.newAssignment.teacherId) {
      this.showError('يرجى تعبئة جميع الحقول المطلوبة');
      return;
    }

    if (!this.currentAcademicYearId) {
      this.showError('لم يتم تحديد السنة الدراسية الحالية، يرجى إعداد سنة دراسية نشطة أولا');
      return;
    }

    const payload = {
      classId: Number(this.newAssignment.classId),
      subjectId: Number(this.newAssignment.subjectId),
      teacherId: Number(this.newAssignment.teacherId),
      weeklyPeriods: this.normalizeWeeklyPeriods(Number(this.newAssignment.weeklyPeriods)),
      academicYearId: this.currentAcademicYearId,
    };

    this.assignmentService.assignTeacherToClass(payload).subscribe({
      next: () => {
        this.newAssignment = { classId: this.newAssignment.classId, subjectId: 0, teacherId: 0, weeklyPeriods: 1 };
        this.noTeachersAvailable.set(false);
        this.formTeachers.set([]);
        this.loadAssignments();
        this.showSuccess('تم إسناد المعلم بنجاح');
      },
      error: (err) => {
        const msg = err?.error?.message || err?.error || 'تأكد من عدم تكرار التعيين لنفس المادة والفصل.';
        this.showError('حدث خطأ أثناء الإسناد: ' + msg);
      }
    });
  }

  removeAssignment(id: number) {
    this.removeAssignmentConfirmId.set(id);
  }

  cancelRemoveAssignment() {
    this.removeAssignmentConfirmId.set(null);
  }

  confirmRemoveAssignment() {
    const id = this.removeAssignmentConfirmId();
    if (!id) return;
    this.removeAssignmentConfirmId.set(null);
    this.assignmentService.delete(id).subscribe({
      next: () => {
        this.loadAssignments();
        this.showSuccess('تم حذف التعيين بنجاح');
      },
      error: () => this.showError('تعذر حذف التعيين، حاول مرة أخرى')
    });
  }

  startEdit(assignment: ClassSubjectTeacher) {
    this.editingAssignmentId.set(assignment.id!);
    this.editForm = {
      teacherId: assignment.teacherId,
      weeklyPeriods: this.normalizeWeeklyPeriods(assignment.weeklyPeriods)
    };
    this.editNoTeachersAvailable.set(false);
    this.editTeachersRequestVersion++;

    // Pre-populate immediately with the current teacher so the select has
    // a valid option from the very first render (fixes the ngModel race condition)
    const currentTeacher = this.teachers().find(t => t.id === assignment.teacherId);
    this.editFormTeachers.set(currentTeacher ? [currentTeacher] : []);

    if (this.currentAcademicYearId) {
      const requestVersion = this.editTeachersRequestVersion;
      this.assignmentService
        .getAvailableTeachers(assignment.subjectId, assignment.classId, this.currentAcademicYearId)
        .subscribe({
          next: (list) => {
            if (requestVersion !== this.editTeachersRequestVersion) return;

            const current = this.teachers().find(t => t.id === assignment.teacherId);
            const others = list.filter((t: any) => t.id !== assignment.teacherId);
            const merged = current ? [current, ...others] : others;

            if (merged.length > 0) {
              this.editFormTeachers.set(merged);
              this.editNoTeachersAvailable.set(false);
            } else {
              this.editFormTeachers.set(current ? [current] : []);
              this.editNoTeachersAvailable.set(!current);
            }
          },
          error: () => {
            if (requestVersion !== this.editTeachersRequestVersion) return;
            const current = this.teachers().find(t => t.id === assignment.teacherId);
            this.editFormTeachers.set(current ? [current] : []);
            this.editNoTeachersAvailable.set(!current);
          },
        });
    } else {
      const current = this.teachers().find(t => t.id === assignment.teacherId);
      this.editFormTeachers.set(current ? [current] : []);
    }
  }

  cancelEdit() {
    this.editTeachersRequestVersion++;
    this.editingAssignmentId.set(null);
    this.editFormTeachers.set([]);
    this.editNoTeachersAvailable.set(false);
  }

  saveEdit(id: number) {
    if (!this.editForm.teacherId) {
      this.showError('يرجى اختيار معلم');
      return;
    }

    const weeklyPeriods = this.normalizeWeeklyPeriods(this.editForm.weeklyPeriods);
    this.editForm.weeklyPeriods = weeklyPeriods;

    this.assignmentService.update(id, {
      teacherId: Number(this.editForm.teacherId),
      weeklyPeriods
    }).subscribe({
      next: (response) => {
        const updatedAssignment = response?.data ?? response ?? {};
        const teacherId = Number(updatedAssignment.teacherId ?? this.editForm.teacherId);
        const teacherName = updatedAssignment.teacherName || this.getTeacherName(teacherId);
        const normalizedWeeklyPeriods = this.normalizeWeeklyPeriods(
          Number(updatedAssignment.weeklyPeriods ?? weeklyPeriods)
        );

        this.assignments.update(assignments =>
          assignments.map(assignment =>
            assignment.id === id
              ? {
                  ...assignment,
                  ...updatedAssignment,
                  teacherId,
                  teacherName,
                  weeklyPeriods: normalizedWeeklyPeriods,
                }
              : assignment
          )
        );

        this.cancelEdit();
        this.showSuccess('تم تحديث التعيين بنجاح');
      },
      error: (err) => this.showError(err?.message || 'تعذر تحديث التعيين')
    });
  }

  /* ── Days display (read-only) ────────────────────────────────────────
     الأيام هنا للعرض فقط — مصدرها الجدول الفعلي اللي بيتم بناؤه وإدارته
     من صفحة admin-schedule (Timetable / TimetableSlot)، مربوطة بنفس
     التعيين عبر classSubjectTeacherId.

     قصدًا مفيش هنا أي تعديل/كتابة مباشرة على الجدول: تعديل الأيام بيستلزم
     اختيار يوم + حصة + قاعة وتطبيق فحوصات التعارض والتوقيت الموحّد، وكل
     ده موجود وكامل في admin-schedule فقط. تكرار جزء منه هنا بمنطق مبسّط
     كان بيدّي بيانات غير مكتملة (بدون قاعة، بتوقيت وهمي 08:00-08:45، وبدون
     فحص تعارض حقيقي) ويفتح مسارين كتابة مختلفين على نفس البيانات.
     لإدارة الأيام فعليًا → زرار "عرض في الجدول" بيحوّل لـ admin-schedule. */

  private timetableService = inject(TimetableService);
  private router = inject(Router);

  assignmentDaysMap = signal<Record<number, string[]>>({});

  getAssignmentDays(assignmentId: number): string {
    const days = this.assignmentDaysMap()[assignmentId];
    if (!days || days.length === 0) return '—';
    return days.map(d => DAY_LABELS[d] || d).join('، ');
  }

  onLoadDaysForRow(assignment: ClassSubjectTeacher) {
    if (!this.currentAcademicYearId || this.assignmentDaysMap()[assignment.id!]) return;
    this.loadDaysForAssignment(assignment);
  }

  private async loadDaysForAssignment(assignment: ClassSubjectTeacher) {
    if (!this.currentAcademicYearId) return;
    try {
      const listRes: any = await this.timetableService.getByClass(assignment.classId, this.currentAcademicYearId).toPromise();
      const allTts: any[] = listRes?.data ?? listRes ?? [];
      const tt = Array.isArray(allTts) ? (allTts.find((t: any) => !t.isActive) || allTts.find((t: any) => t.isActive)) : null;
      if (!tt?.slots) return;
      const assignmentSlots = (tt.slots as TimetableSlotDto[]).filter(s => s.classSubjectTeacherId === assignment.id && !s.isBreak);
      const days = [...new Set(assignmentSlots.map(s => s.dayOfWeek))];
      this.assignmentDaysMap.update(m => { m[assignment.id!] = days; return m; });
    } catch { /* ignore */ }
  }

  /** تحويل الأدمن لصفحة admin-schedule على نفس الفصل/السنة لإدارة الجدول فعليًا */
  goToSchedule(assignment: ClassSubjectTeacher): void {
    const classEntity = this.classes().find(c => c.id === assignment.classId);
    this.router.navigate(['/admin-schedule'], {
      queryParams: {
        classId: assignment.classId,
        yearId: this.currentAcademicYearId,
        gradeId: classEntity?.gradeLevelId ?? null,
      },
    });
  }

  private showError(msg: string) {
    this.errorMessage.set(msg);
    this.successMessage.set(null);
    setTimeout(() => this.errorMessage.set(null), 5000);
  }

  private showSuccess(msg: string) {
    this.successMessage.set(msg);
    this.errorMessage.set(null);
    setTimeout(() => this.successMessage.set(null), 3000);
  }
}
