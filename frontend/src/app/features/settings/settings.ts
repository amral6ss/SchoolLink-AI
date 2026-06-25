import { Component, signal, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { AcademicYearService, AcademicYear } from '../../core/services/academic-year.service';
import { GradeLevelService, GradeLevel } from '../../core/services/grade-level.service';
import { ResultVisibilityService, ResultVisibilityDto, SetVisibilityRequest } from '../../core/services/result-visibility.service';
import { SchoolProfileService, SchoolProfile } from '../../core/services/school-profile.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './settings.html',
  styleUrl: './settings.css',
})
export class Settings implements OnInit {
  sidebarOpen = signal(false);
  activeTab = signal('academic'); // 'permissions' | 'academic' | 'results'
  displayUserName = localStorage.getItem('fullName') || localStorage.getItem('username') || 'المشرف';

  // Services
  private academicYearService = inject(AcademicYearService);
  private gradeLevelService = inject(GradeLevelService);
  private resultVisibilityService = inject(ResultVisibilityService);
  private schoolProfileService = inject(SchoolProfileService);

  // State
  academicYears = signal<AcademicYear[]>([]);
  gradeLevels = signal<GradeLevel[]>([]);
  editingYearId = signal<number | null>(null);
  editingGradeId = signal<number | null>(null);

  // Error / Success messages
  yearError = signal<string | null>(null);
  gradeError = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  // Forms
  newYear = { name: '', startDate: '', endDate: '', firstSemesterStartDate: '', firstSemesterEndDate: '', secondSemesterStartDate: '', secondSemesterEndDate: '' };
  newGrade: { name: string; stage: string | null; levelOrder: number } = {
    name: '',
    stage: null,
    levelOrder: 1,
  };

  readonly stageOptions = ['ابتدائي', 'إعدادي', 'ثانوي'];

  // Result visibility state
  visibilitySettings = signal<ResultVisibilityDto[]>([]);
  selectedYearForVisibility = signal<number | null>(null);
  selectedTermForVisibility = signal<string>('FirstSemester');
  isVisible = signal<boolean>(false);
  visibleFrom = signal<string | null>(null);
  visibleUntil = signal<string | null>(null);
  editingVisibilityId = signal<number | null>(null);
  visibilityError = signal<string | null>(null);
  visibilitySuccess = signal<string | null>(null);
  terms = [
    { value: 'FirstSemester', label: 'الترم الأول' },
    { value: 'SecondSemester', label: 'الترم الثاني' },
  ];

  // ── School Profile State ──────────────────────────────────────────────────
  schoolProfile = signal<SchoolProfile | null>(null);
  schoolForm = {
    schoolName: '',
    governorate: '',
    directorate: '',
    educationalAdministration: '',
    phone: '',
    address: '',
    email: '',
    managerName: '',
  };
  schoolLoading = signal(false);
  schoolError = signal<string | null>(null);
  schoolSuccess = signal<string | null>(null);

  ngOnInit() {
    this.loadData();
    this.loadSchoolProfile();
  }

  loadData() {
    this.academicYearService.getAll().subscribe({
      next: (data) => {
        this.academicYears.set(data.data ?? data);
        this.loadVisibilitySettings();
      },
      error: (err) => console.error('Failed to load academic years', err),
    });

    this.gradeLevelService.getAll().subscribe({
      next: (data) => this.gradeLevels.set(data.data ?? data),
      error: (err) => console.error('Failed to load grade levels', err),
    });
  }

  // ─── Helpers ─────────────────────────────────────────────────────────────

  private extractErrorMessage(err: any, fallback: string): string {
    return err?.error?.message || err?.message || fallback;
  }

  private showSuccess(msg: string) {
    this.successMessage.set(msg);
    setTimeout(() => this.successMessage.set(null), 3000);
  }

  private resetGradeForm() {
    this.editingGradeId.set(null);
    this.newGrade = { name: '', stage: null, levelOrder: 1 };
    this.gradeError.set(null);
  }

  // ─── Academic Year Methods ────────────────────────────────────────────────

  addAcademicYear() {
    if (!this.newYear.name || !this.newYear.startDate || !this.newYear.endDate) return;
    this.yearError.set(null);
    const isEditing = !!this.editingYearId();

    const request = { ...this.newYear };

    const action$ = this.editingYearId()
      ? this.academicYearService.update(this.editingYearId()!, {
          id: this.editingYearId()!,
          ...request,
        })
      : this.academicYearService.create(request);

    action$.subscribe({
      next: () => {
        this.loadData();
        this.cancelYearEdit();
        this.showSuccess(
          isEditing ? 'تم تحديث السنة الدراسية بنجاح' : 'تمت إضافة السنة الدراسية بنجاح'
        );
      },
      error: (err) => {
        this.yearError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء حفظ السنة الدراسية'));
      },
    });
  }

  setYearActive(id: number) {
    this.yearError.set(null);
    this.academicYearService.setActive(id).subscribe({
      next: () => {
        this.loadData();
        this.showSuccess('تم تعيين السنة الدراسية الحالية بنجاح');
      },
      error: (err) => {
        this.yearError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء تعيين السنة الحالية'));
      },
    });
  }

  editYear(year: AcademicYear) {
    this.editingYearId.set(year.id);
    this.newYear = {
      name: year.name,
      startDate: year.startDate,
      endDate: year.endDate,
      firstSemesterStartDate: year.firstSemesterStartDate ?? '',
      firstSemesterEndDate: year.firstSemesterEndDate ?? '',
      secondSemesterStartDate: year.secondSemesterStartDate ?? '',
      secondSemesterEndDate: year.secondSemesterEndDate ?? '',
    };
    this.yearError.set(null);
  }

  cancelYearEdit() {
    this.editingYearId.set(null);
    this.newYear = { name: '', startDate: '', endDate: '', firstSemesterStartDate: '', firstSemesterEndDate: '', secondSemesterStartDate: '', secondSemesterEndDate: '' };
    this.yearError.set(null);
  }

  deleteYear(id: number) {
    this.yearError.set(null);
    this.academicYearService.delete(id).subscribe({
      next: () => {
        this.loadData();
        this.showSuccess('تم حذف السنة الدراسية بنجاح');
      },
      error: (err) => {
        this.yearError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء حذف السنة الدراسية'));
      },
    });
  }

  // ─── Grade Level Methods ──────────────────────────────────────────────────

  addGradeLevel() {
    if (!this.newGrade.name) return;
    this.gradeError.set(null);
    const isEditing = !!this.editingGradeId();
    const request = {
      name: this.newGrade.name,
      stage: this.newGrade.stage,
      levelOrder: this.newGrade.levelOrder,
    };

    const action$ = this.editingGradeId()
      ? this.gradeLevelService.updateValidated(this.editingGradeId()!, {
          id: this.editingGradeId()!,
          ...request,
        })
      : this.gradeLevelService.createValidated(request);

    action$.subscribe({
      next: () => {
        this.loadData();
        this.resetGradeForm();
        this.showSuccess(isEditing ? 'تم تحديث المرحلة الدراسية بنجاح' : 'تمت إضافة المرحلة الدراسية بنجاح');
      },
      error: (err) => {
        this.gradeError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء حفظ المرحلة الدراسية'));
      },
    });
  }

  editGradeLevel(grade: GradeLevel) {
    this.editingGradeId.set(grade.id);
    this.newGrade = {
      name: grade.name,
      stage: grade.stage ?? null,
      levelOrder: grade.levelOrder,
    };
    this.gradeError.set(null);
  }

  cancelGradeEdit() {
    this.resetGradeForm();
  }

  deleteGradeLevel(id: number) {
    this.gradeError.set(null);
    this.gradeLevelService.delete(id).subscribe({
      next: () => {
        this.loadData();
        if (this.editingGradeId() === id) {
          this.resetGradeForm();
        }
        this.showSuccess('تم حذف المرحلة الدراسية بنجاح');
      },
      error: (err) => {
        this.gradeError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء حذف المرحلة الدراسية'));
      },
    });
  }

  // ─── Result Visibility Methods ──────────────────────────────────────────────

  loadVisibilitySettings() {
    this.resultVisibilityService.getAll().subscribe({
      next: (res) => {
        this.visibilitySettings.set(res?.data ?? []);
      },
      error: () => this.visibilitySettings.set([]),
    });
  }

  saveVisibility() {
    if (!this.selectedYearForVisibility()) return;
    this.visibilityError.set(null);
    this.visibilitySuccess.set(null);

    const editId = this.editingVisibilityId();
    const isEditing = editId !== null;

    // Map term string to number for API
    const termNumber = this.selectedTermForVisibility() === 'SecondSemester' ? 2 : 1;

    if (isEditing) {
      this.resultVisibilityService.update(editId!, {
        isVisible: this.isVisible(),
        visibleFrom: this.visibleFrom(),
        visibleUntil: this.visibleUntil(),
      }).subscribe({
        next: () => {
          this.resetVisibilityForm();
          this.loadVisibilitySettings();
          this.showVisibilitySuccess('تم تحديث إعدادات إظهار النتائج بنجاح');
        },
        error: (err) => {
          this.visibilityError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء تحديث الإعدادات'));
        },
      });
    } else {
      const request: SetVisibilityRequest = {
        academicYearId: this.selectedYearForVisibility()!,
        term: termNumber,
        isVisible: this.isVisible(),
        visibleFrom: this.visibleFrom(),
        visibleUntil: this.visibleUntil(),
      };
      this.resultVisibilityService.setVisibility(request).subscribe({
        next: () => {
          this.resetVisibilityForm();
          this.loadVisibilitySettings();
          this.showVisibilitySuccess('تم حفظ إعدادات إظهار النتائج بنجاح');
        },
        error: (err) => {
          this.visibilityError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء حفظ الإعدادات'));
        },
      });
    }
  }

  editVisibility(setting: ResultVisibilityDto) {
    this.editingVisibilityId.set(setting.id);
    this.selectedYearForVisibility.set(setting.academicYearId);
    this.selectedTermForVisibility.set(setting.term);
    this.isVisible.set(setting.isVisible);
    this.visibleFrom.set(setting.visibleFrom);
    this.visibleUntil.set(setting.visibleUntil);
    this.visibilityError.set(null);
    this.visibilitySuccess.set(null);
  }

  deleteVisibility(id: number) {
    this.resultVisibilityService.delete(id).subscribe({
      next: () => {
        this.loadVisibilitySettings();
        if (this.editingVisibilityId() === id) this.resetVisibilityForm();
        this.showVisibilitySuccess('تم حذف إعدادات إظهار النتائج بنجاح');
      },
      error: (err) => {
        this.visibilityError.set(this.extractErrorMessage(err, 'حدث خطأ أثناء الحذف'));
      },
    });
  }

  resetVisibilityForm() {
    this.editingVisibilityId.set(null);
    this.selectedYearForVisibility.set(null);
    this.selectedTermForVisibility.set('FirstSemester');
    this.isVisible.set(false);
    this.visibleFrom.set(null);
    this.visibleUntil.set(null);
    this.visibilityError.set(null);
    this.visibilitySuccess.set(null);
  }

  private showVisibilitySuccess(msg: string) {
    this.visibilitySuccess.set(msg);
    setTimeout(() => this.visibilitySuccess.set(null), 3000);
  }

  getAcademicYearName(id: number): string {
    return this.academicYears().find(y => y.id === id)?.name || `سنة ${id}`;
  }

  getTermLabel(value: string): string {
    return this.terms.find(t => t.value === value)?.label || `الترم ${value}`;
  }

  onAcademicYearChange() {
    if (this.selectedYearForVisibility()) {
      this.loadVisibilitySettings();
      // Auto-fill form if there's an existing setting for this year+term
      const yearId = this.selectedYearForVisibility();
      const termVal = this.selectedTermForVisibility();
      const existing = this.visibilitySettings().find(
        s => s.academicYearId === yearId && s.term === termVal
      );
      if (existing) {
        this.isVisible.set(existing.isVisible);
        this.visibleFrom.set(existing.visibleFrom);
        this.visibleUntil.set(existing.visibleUntil);
        this.editingVisibilityId.set(existing.id);
        this.visibilityError.set(null);
        this.visibilitySuccess.set(null);
      } else {
        // New entry – keep year/term, clear edit mode
        this.isVisible.set(false);
        this.visibleFrom.set(null);
        this.visibleUntil.set(null);
        this.editingVisibilityId.set(null);
        this.visibilityError.set(null);
        this.visibilitySuccess.set(null);
      }
    }
  }

  onTermChange() {
    this.onAcademicYearChange();
  }

  // ─── School Profile Methods ───────────────────────────────────────────────

  loadSchoolProfile() {
    this.schoolLoading.set(true);
    this.schoolProfileService.get().subscribe({
      next: (profile) => {
        this.schoolLoading.set(false);
        this.schoolProfile.set(profile);
        this.schoolForm = {
          schoolName: profile.schoolName || '',
          governorate: profile.governorate || '',
          directorate: profile.directorate || '',
          educationalAdministration: profile.educationalAdministration || '',
          phone: profile.phone || '',
          address: profile.address || '',
          email: profile.email || '',
          managerName: profile.managerName || '',
        };
      },
      error: () => {
        this.schoolLoading.set(false);
        // If no profile exists yet, show empty form
        this.schoolProfile.set(null);
        this.schoolForm = { schoolName: '', governorate: '', directorate: '', educationalAdministration: '', phone: '', address: '', email: '', managerName: '' };
      },
    });
  }

  saveSchoolProfile() {
    if (!this.schoolForm.schoolName || !this.schoolForm.governorate || !this.schoolForm.directorate || !this.schoolForm.educationalAdministration) {
      this.schoolError.set('يرجى ملء اسم المدرسة والمحافظة والإدارة التعليمية');
      return;
    }
    this.schoolError.set(null);
    this.schoolSuccess.set(null);
    this.schoolLoading.set(true);

    // تنظيف رقم الهاتف من الشرطات والمسافات للتوافق مع الـ Regex
    const cleanPhone = this.schoolForm.phone ? this.schoolForm.phone.replace(/[-\s]/g, '') : '';

    this.schoolProfileService.update({
      schoolName: this.schoolForm.schoolName,
      governorate: this.schoolForm.governorate,
      directorate: this.schoolForm.directorate,
      educationalAdministration: this.schoolForm.educationalAdministration,
      phone: cleanPhone || undefined,
      address: this.schoolForm.address || undefined,
      email: this.schoolForm.email || undefined,
      managerName: this.schoolForm.managerName || undefined,
    }).subscribe({
      next: (profile) => {
        this.schoolLoading.set(false);
        this.schoolProfile.set(profile);
        this.showSchoolSuccess('تم حفظ الملف التعريفي للمدرسة بنجاح');
      },
      error: (err) => {
        this.schoolLoading.set(false);
        // عرض أخطاء الفاليديشن من الـ API
        const apiErrors = err?.error?.errors;
        if (apiErrors) {
          const msgs = Object.values(apiErrors).flat();
          this.schoolError.set(msgs.join(' • '));
        } else {
          this.schoolError.set(err?.error?.message || err?.message || 'حدث خطأ أثناء حفظ الملف التعريفي');
        }
      },
    });
  }

  private showSchoolSuccess(msg: string) {
    this.schoolSuccess.set(msg);
    setTimeout(() => this.schoolSuccess.set(null), 3000);
  }

  get filteredVisibilityByYear(): ResultVisibilityDto[] {
    const yearId = this.selectedYearForVisibility();
    let filtered = yearId
      ? this.visibilitySettings().filter(s => s.academicYearId === yearId)
      : this.visibilitySettings();
    // Only show settings for valid terms (FirstSemester / SecondSemester)
    return filtered.filter(s => s.term === 'FirstSemester' || s.term === 'SecondSemester');
  }
}
