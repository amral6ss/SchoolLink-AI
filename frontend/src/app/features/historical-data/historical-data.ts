import { Component, signal, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { RoleService } from '../../shared/role.service';
import { AuthService } from '../../core/services/auth.service';
import {
  HistoricalDataService, HistoricalYear, HistoricalClass, HistoricalStudent,
  HistoricalFinalGrade, HistoricalEvaluation, HistoricalAssessment,
  HistoricalExam, HistoricalAssignment, HistoricalAbsence, HistoricalDataOverview
} from './historical-data.service';
import { SchoolProfileService, SchoolProfile } from '../../core/services/school-profile.service';
import { catchError } from 'rxjs/operators';
import { of } from 'rxjs';

@Component({
  selector: 'app-historical-data',
  imports: [Sidebar, CommonModule, FormsModule],
  templateUrl: './historical-data.html',
  styleUrl: './historical-data.css',
})
export class HistoricalDataComponent implements OnInit {
  private dataSvc = inject(HistoricalDataService);
  private roleSvc = inject(RoleService);
  private auth   = inject(AuthService);
  private schoolProfileSvc = inject(SchoolProfileService);

  sidebarOpen = signal(false);
  loading = signal(false);
  errorMsg = signal<string | null>(null);
  schoolProfile = signal<SchoolProfile | null>(null);

  role = computed(() => this.roleSvc.currentRole() ?? '');
  isAdminOrTeacher = computed(() => this.role() === 'admin' || this.role() === 'teacher');
  isStudent = computed(() => this.role() === 'student');
  isParent  = computed(() => this.role() === 'parent');

  years: HistoricalYear[] = [];
  selectedYearId: number | null = null;
  get selectedYearName(): string {
    return this.years.find(y => y.id === this.selectedYearId)?.name ?? '';
  }

  classes: HistoricalClass[] = [];
  selectedClassId: number | null = null;
  get selectedClassName(): string {
    return this.classes.find(c => c.id === this.selectedClassId)?.name ?? '';
  }

  students: HistoricalStudent[] = [];
  selectedStudentId: number | null = null;
  selectedEnrollmentId: number | null = null;

  overview: HistoricalDataOverview | null = null;

  activeTab: 'grades' | 'evaluations' | 'assessments' | 'exams' | 'assignments' | 'absences' = 'grades';

  finalGrades: HistoricalFinalGrade[] = [];
  evaluations: HistoricalEvaluation[] = [];
  assessments: HistoricalAssessment[] = [];
  exams: HistoricalExam[] = [];
  assignments: HistoricalAssignment[] = [];
  absences: HistoricalAbsence[] = [];

  loadingGrades = signal(false);
  loadingEvals = signal(false);
  loadingAssess = signal(false);
  loadingExams = signal(false);
  loadingAssign = signal(false);
  loadingAbsences = signal(false);

  // Filters
  selectedTerm: '' | '1' | '2' = '';
  selectedSubjectName: string = '';
  studentSearch: string = '';

  // Sorting
  sortCol: string = 'percentage';
  sortDir: 'asc' | 'desc' = 'desc';

  // Pagination
  currentPage = 1;
  pageSize = 20;

  // Parent / Student
  children: { studentId: number; studentName: string }[] = [];
  selectedChildId: number | null = null;
  showStudentDetail = signal(false);
  detailLoading = signal(false);
  detailStudent: any = null;
  studentViewMode: 'classes' | 'detail' = 'classes';
  studentOwnSummary: any = null;
  studentDetailTerm: '' | 1 | 2 = '';
  selectedGradeFilter: string = '';
  absenceDateFrom: string = '';
  absenceDateTo: string = '';
  absenceThreshold: number | null = null;
  sectionsExpanded: any = {
    grades: true,
    evaluations: true,
    exams: true,
    assignments: true,
    absences: true,
    assessments: true 
  };

  ngOnInit() {
    this.schoolProfileSvc.get().subscribe(p => this.schoolProfile.set(p));
    this.loadYears();
  }

  loadYears() {
    this.loading.set(true);
    this.dataSvc.getYears().pipe(catchError(() => of({ isSuccess: false, data: [] }))).subscribe({
      next: (res) => {
        const raw = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
        this.years = (Array.isArray(raw) ? raw : []).filter((y: HistoricalYear) => !y.isCurrent);
        this.selectedYearId = this.years[0]?.id ?? null;
        this.loading.set(false);
        if (this.selectedYearId) this.onYearChange();
      },
      error: () => this.loading.set(false)
    });
  }

  onYearChange() {
    this.reset();
    if (!this.selectedYearId) return;
    const r = this.role();
    if (r === 'admin' || r === 'teacher') this.loadClasses();
    else if (r === 'student') this.loadStudentData();
    else if (r === 'parent') this.loadChildren();
  }

  private reset() {
    this.classes = []; this.selectedClassId = null;
    this.students = []; this.selectedStudentId = null; this.selectedEnrollmentId = null;
    this.overview = null;
    this.finalGrades = []; this.evaluations = []; this.assessments = [];
    this.exams = []; this.assignments = []; this.absences = [];
    this.errorMsg.set(null);
    this.activeTab = 'grades';
    this.showStudentDetail.set(false);
    this.detailStudent = null;
    this.studentOwnSummary = null;
    this.studentViewMode = 'classes';
    this.selectedTerm = '';
    this.selectedSubjectName = '';
    this.studentSearch = '';
    this.selectedGradeFilter = '';
    this.absenceDateFrom = '';
    this.absenceDateTo = '';
    this.absenceThreshold = null;
    this.sortCol = 'percentage';
    this.sortDir = 'desc';
    this.currentPage = 1;
  }

  // ── Admin / Teacher ────────────────────────────

  loadClasses() {
    if (!this.selectedYearId) return;
    this.loading.set(true);
    this.dataSvc.getClasses(this.selectedYearId).pipe(catchError(() => of({ isSuccess: false, data: [] }))).subscribe({
      next: (res) => {
        const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
        this.classes = Array.isArray(data) ? data : [];
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  selectClass(classId: number) {
    this.selectedClassId = classId;
    this.selectedStudentId = null;
    this.selectedEnrollmentId = null;
    this.currentPage = 1;
    this.loadClassData();
  }

  backToClasses() {
    this.selectedClassId = null;
    this.selectedStudentId = null;
    this.selectedEnrollmentId = null;
    this.overview = null;
    this.finalGrades = []; this.evaluations = []; this.assessments = [];
    this.exams = []; this.assignments = []; this.absences = [];
    this.showStudentDetail.set(false);
    this.currentPage = 1;
  }

  private loadClassData() {
    if (!this.selectedClassId) return;
    this.loadOverview();
    this.loadFinalGrades();
  }

  loadOverview() {
    if (!this.selectedClassId) return;
    this.dataSvc.getOverview(this.selectedClassId, this.selectedTerm ? +this.selectedTerm : undefined)
      .pipe(catchError(() => of({ isSuccess: false, data: null })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : null;
          this.overview = data;
        }
      });
  }

  loadFinalGrades() {
    if (!this.selectedClassId) return;
    this.loadingGrades.set(true);
    this.dataSvc.getFinalGrades(this.selectedClassId, this.selectedTerm ? +this.selectedTerm : undefined)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          this.finalGrades = Array.isArray(data) ? data : [];
          this.loadingGrades.set(false);
          this.currentPage = 1;
        },
        error: () => this.loadingGrades.set(false)
      });
  }

  loadEvaluations() {
    if (!this.selectedClassId) return;
    this.loadingEvals.set(true);
    this.dataSvc.getEvaluations(this.selectedClassId)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          this.evaluations = Array.isArray(data) ? data : [];
          this.loadingEvals.set(false);
        },
        error: () => this.loadingEvals.set(false)
      });
  }

  loadAssessments() {
    if (!this.selectedClassId) return;
    this.loadingAssess.set(true);
    this.dataSvc.getAssessments(this.selectedClassId, undefined, this.selectedTerm ? +this.selectedTerm : undefined)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          this.assessments = Array.isArray(data) ? data : [];
          this.loadingAssess.set(false);
        },
        error: () => this.loadingAssess.set(false)
      });
  }

  loadExams() {
    if (!this.selectedEnrollmentId) { this.exams = []; return; }
    this.loadingExams.set(true);
    this.dataSvc.getExams(this.selectedEnrollmentId)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          this.exams = Array.isArray(data) ? data : [];
          this.loadingExams.set(false);
        },
        error: () => this.loadingExams.set(false)
      });
  }

  loadAssignments() {
    if (!this.selectedEnrollmentId) { this.assignments = []; return; }
    this.loadingAssign.set(true);
    this.dataSvc.getAssignments(this.selectedEnrollmentId)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          this.assignments = Array.isArray(data) ? data : [];
          this.loadingAssign.set(false);
        },
        error: () => this.loadingAssign.set(false)
      });
  }

  loadAbsences() {
    if (!this.selectedClassId) return;
    this.loadingAbsences.set(true);
    this.dataSvc.getAbsences(this.selectedClassId)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          this.absences = Array.isArray(data) ? data : [];
          this.loadingAbsences.set(false);
        },
        error: () => this.loadingAbsences.set(false)
      });
  }

  onTermChange() {
    this.currentPage = 1;
    this.loadOverview();
    this.loadFinalGrades();
  }

  onSubjectChange() {
    this.currentPage = 1;
  }

  onStudentSearch() {
    this.currentPage = 1;
  }

  onTabChange(tab: typeof this.activeTab) {
    this.activeTab = tab;
    if (tab === 'evaluations' && this.evaluations.length === 0) this.loadEvaluations();
    if (tab === 'assessments' && this.assessments.length === 0) this.loadAssessments();
    if (tab === 'absences' && this.absences.length === 0) this.loadAbsences();
  }

  // ── Sorting ─────────────────────────────────

  toggleSort(col: string) {
    if (this.sortCol === col) {
      this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortCol = col;
      this.sortDir = 'asc';
    }
  }

  // ── Pagination ──────────────────────────────

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.sortedGrades.length / this.pageSize));
  }

  get pagedGrades(): HistoricalFinalGrade[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.sortedGrades.slice(start, start + this.pageSize);
  }

  get visiblePages(): number[] {
    const total = this.totalPages;
    const current = this.currentPage;
    const pages: number[] = [];
    const start = Math.max(1, current - 2);
    const end = Math.min(total, current + 2);
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  }

  goToPage(page: number) {
    if (page >= 1 && page <= this.totalPages) {
      this.currentPage = page;
    }
  }

  // ── Student detail ──────────────────────────

  async openStudentDetail(studentId: number) {
    this.selectedStudentId = studentId;
    this.showStudentDetail.set(true);
    this.detailLoading.set(true);
    if (!this.selectedYearId) return;
    this.dataSvc.getStudentSummary(studentId, this.selectedYearId)
      .pipe(catchError(() => of({ isSuccess: false, data: null })))
      .subscribe({
        next: (res) => {
          this.detailStudent = res?.isSuccess ? res.data : null;
          this.processDetailStudent();
          this.resetSections();
          this.resetStudentDetailPagination();
          this.detailLoading.set(false);
        },
        error: () => this.detailLoading.set(false)
      });
  }

  closeStudentDetail() {
    this.showStudentDetail.set(false);
    this.detailStudent = null;
    this.selectedStudentId = null;
    this.resetStudentDetailPagination();
  }

  resetStudentDetailPagination() {
    this.studentGradesPage = 1;
    this.studentEvalsPage = 1;
    this.studentExamsPage = 1;
    this.studentAssignsPage = 1;
    this.studentAbsencesPage = 1;
    this.studentAssessPage = 1;
  }

  // ── Student ─────────────────────────────────

  loadStudentData() {
    if (!this.selectedYearId) return;
    this.loading.set(true);
    const userId = this.auth.user()?.userId;
    if (!userId) { this.loading.set(false); return; }
    this.dataSvc.getStudentsByYear(this.selectedYearId)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          const allStudents: HistoricalStudent[] = Array.isArray(data) ? data : [];
          this.students = allStudents;
          const authUser = this.auth.user();
          const userStudent = allStudents.find(s =>
            s.fullName && authUser?.fullName && s.fullName.includes(authUser.fullName)
          );
          if (userStudent) {
            this.loading.set(false);
            this.openStudentDetail(userStudent.id);
          } else {
            this.studentViewMode = 'classes';
            this.loadClasses();
            this.loading.set(false);
          }
        },
        error: () => this.loading.set(false)
      });
  }

  // ── Parent ──────────────────────────────────

  loadChildren() {
    const userId = this.auth.user()?.userId;
    if (!userId || !this.selectedYearId) return;
    this.loading.set(true);
    this.dataSvc.getStudentsByYear(this.selectedYearId)
      .pipe(catchError(() => of({ isSuccess: false, data: [] })))
      .subscribe({
        next: (res) => {
          const data = res?.isSuccess ? res.data : (Array.isArray(res) ? res : []);
          const allStudents: HistoricalStudent[] = Array.isArray(data) ? data : [];
          this.children = allStudents.map(s => ({ studentId: s.id, studentName: s.fullName }));
          this.loading.set(false);
          if (this.children.length > 0) this.selectChild(this.children[0].studentId);
        },
        error: () => this.loading.set(false)
      });
  }

  selectChild(studentId: number) {
    this.selectedChildId = studentId;
    this.showStudentDetail.set(true);
    this.detailLoading.set(true);
    if (!this.selectedYearId) return;
    this.dataSvc.getStudentSummary(studentId, this.selectedYearId)
      .pipe(catchError(() => of({ isSuccess: false, data: null })))
      .subscribe({
        next: (res) => {
          this.detailStudent = res?.isSuccess ? res.data : null;
          this.processDetailStudent();
          this.resetSections();
          this.resetStudentDetailPagination();
          this.detailLoading.set(false);
        },
        error: () => this.detailLoading.set(false)
      });
  }

  resetSections() {
    this.sectionsExpanded = { grades: true, evaluations: true, exams: true, assignments: true, absences: true, assessments: true };
  }

  toggleSection(sec: string) {
    this.sectionsExpanded[sec] = !this.sectionsExpanded[sec];
  }

  processDetailStudent() {
    if (!this.detailStudent) return;
    if (this.detailStudent.finalGrades) {
      this.detailStudent.finalGrades.sort((a: any, b: any) => (a.subjectName ?? '').localeCompare(b.subjectName ?? ''));
    }
    if (this.detailStudent.assessments) {
      this.detailStudent.assessments.sort((a: any, b: any) => (a.subjectName ?? '').localeCompare(b.subjectName ?? ''));
    }
    if (this.detailStudent.evaluations) {
      this.detailStudent.evaluations.sort((a: any, b: any) => (a.itemName ?? '').localeCompare(b.itemName ?? ''));
    }
    if (this.detailStudent.exams) {
      this.detailStudent.exams.sort((a: any, b: any) => (a.subjectName ?? '').localeCompare(b.subjectName ?? ''));
    }
    if (this.detailStudent.assignments) {
      this.detailStudent.assignments.sort((a: any, b: any) => (a.subjectName ?? '').localeCompare(b.subjectName ?? ''));
    }
    if (this.detailStudent.absences) {
      this.detailStudent.absences.sort((a: any, b: any) => new Date(b.absenceDate).getTime() - new Date(a.absenceDate).getTime());
    }
  }

  // ── Getters for Student Detail Tabs ──────────────────────────────────
  get studentGrades() {
    if (!this.detailStudent?.finalGrades) return [];
    if (!this.studentDetailTerm) return this.detailStudent.finalGrades;
    return this.detailStudent.finalGrades.filter((g: any) => g.academicTerm == this.studentDetailTerm);
  }

  get studentAssessments() {
    if (!this.detailStudent?.assessments) return [];
    if (!this.studentDetailTerm) return this.detailStudent.assessments;
    return this.detailStudent.assessments.filter((a: any) => a.term == this.studentDetailTerm);
  }

  get studentAbsences() {
    if (!this.detailStudent?.absences) return [];
    let abs = this.detailStudent.absences;
    if (this.absenceDateFrom) {
      const from = new Date(this.absenceDateFrom).getTime();
      abs = abs.filter((a: any) => new Date(a.absenceDate).getTime() >= from);
    }
    if (this.absenceDateTo) {
      const to = new Date(this.absenceDateTo).getTime();
      abs = abs.filter((a: any) => new Date(a.absenceDate).getTime() <= to);
    }
    return abs;
  }

  // ── Student Detail Pagination ──────────────────
  studentPageSize = 10;
  studentGradesPage = 1;
  studentEvalsPage = 1;
  studentExamsPage = 1;
  studentAssignsPage = 1;
  studentAbsencesPage = 1;
  studentAssessPage = 1;

  get pagedStudentGrades() { const s = (this.studentGradesPage - 1) * this.studentPageSize; return this.studentGrades.slice(s, s + this.studentPageSize); }
  get studentGradesTotalPages() { return Math.max(1, Math.ceil(this.studentGrades.length / this.studentPageSize)); }

  get studentEvals() {
    if (!this.detailStudent?.evaluations) return [];
    return this.detailStudent.evaluations;
  }
  get pagedStudentEvals() { const s = (this.studentEvalsPage - 1) * this.studentPageSize; return this.studentEvals.slice(s, s + this.studentPageSize); }
  get studentEvalsTotalPages() { return Math.max(1, Math.ceil(this.studentEvals.length / this.studentPageSize)); }

  get studentExams() {
    if (!this.detailStudent?.exams) return [];
    return this.detailStudent.exams;
  }
  get pagedStudentExams() { const s = (this.studentExamsPage - 1) * this.studentPageSize; return this.studentExams.slice(s, s + this.studentPageSize); }
  get studentExamsTotalPages() { return Math.max(1, Math.ceil(this.studentExams.length / this.studentPageSize)); }

  get studentAssigns() {
    if (!this.detailStudent?.assignments) return [];
    return this.detailStudent.assignments;
  }
  get pagedStudentAssigns() { const s = (this.studentAssignsPage - 1) * this.studentPageSize; return this.studentAssigns.slice(s, s + this.studentPageSize); }
  get studentAssignsTotalPages() { return Math.max(1, Math.ceil(this.studentAssigns.length / this.studentPageSize)); }

  get pagedStudentAbsences() { const s = (this.studentAbsencesPage - 1) * this.studentPageSize; return this.studentAbsences.slice(s, s + this.studentPageSize); }
  get studentAbsencesTotalPages() { return Math.max(1, Math.ceil(this.studentAbsences.length / this.studentPageSize)); }

  get pagedStudentAssessments() { const s = (this.studentAssessPage - 1) * this.studentPageSize; return this.studentAssessments.slice(s, s + this.studentPageSize); }
  get studentAssessTotalPages() { return Math.max(1, Math.ceil(this.studentAssessments.length / this.studentPageSize)); }


  exportStudentExcel() {
    if (!this.detailStudent) return;
    let rows: string[][] = [];
    
    // Add School Profile Header
    const p = this.schoolProfile();
    if (p) {
      rows.push([p.schoolName || '']);
      if (p.governorate || p.directorate) rows.push([`${p.governorate || ''} - ${p.directorate || ''}`]);
      if (p.educationalAdministration) rows.push([p.educationalAdministration]);
      rows.push(['']); // Empty row
    }

    rows.push(['تقرير الطالب الشامل']);
    rows.push(['الاسم:', this.detailStudent.studentName, 'الفصل:', this.detailStudent.className]);
    rows.push([]);
    rows.push(['#', 'القسم', 'المادة / العنصر', 'الدرجة / التقييم / النسبة', 'ملاحظات']);
    
    // Grades
    this.studentGrades.forEach((g: any, i: number) => {
      rows.push([String(i + 1), 'الدرجات النهائية', g.subjectName ?? '', `${g.total}/${g.maxTotal} (${g.percentage}%)`, this.getGradeBadge(g.percentage).label]);
    });
    // Assessments
    this.studentAssessments.forEach((a: any, i: number) => {
      rows.push([String(i + 1), 'التقييمات الدورية', a.subjectName ?? '', String(a.score), a.assessmentType]);
    });
    // Absences
    if (this.studentAbsences) {
      this.studentAbsences.forEach((a: any, i: number) => {
        rows.push([String(i + 1), 'سجل الغياب', a.subjectName ?? '', a.absenceDate, a.isAbsent ? 'غائب' : 'حاضر']);
      });
    }

    const csv = rows.map(r => r.join(',')).join('\n');
    const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `student-report-${this.detailStudent.studentName}.csv`;
    link.click();
  }

  exportStudentPDF() {
    if (!this.detailStudent) return;
    const p = this.schoolProfile();
    const schoolHeader = p ? `
      <div style="text-align: center; margin-bottom: 20px; padding-bottom: 10px; border-bottom: 2px solid #00236f;">
        <h1 style="color:#00236f; margin:0 0 5px;">${p.schoolName || ''}</h1>
        <p style="margin:2px 0;">${p.governorate || ''} - ${p.directorate || ''}</p>
        <p style="margin:2px 0;">${p.educationalAdministration || ''}</p>
      </div>
    ` : '';

    let html = `<html><head><meta charset="utf-8"><style>
      body{font-family:sans-serif;direction:rtl;padding:20px}
      h2{color:#00236f; text-align:center; margin-bottom: 5px;}
      h3{color:#333; border-bottom: 2px solid #00236f; padding-bottom: 5px; margin-top: 30px;}
      table{width:100%;border-collapse:collapse;margin-top:12px;page-break-inside:auto;}
      tr{page-break-inside:avoid;page-break-after:auto;}
      thead{display:table-header-group;}
      tfoot{display:table-footer-group;}
      th{background:#00236f;color:#fff;padding:8px;font-size:12px}
      td{padding:6px;border-bottom:1px solid #eee;font-size:12px;text-align:center}
      .badge{padding:2px 8px;border-radius:10px;font-size:10px;font-weight:700}
      .exc{background:#dcfce7;color:#15803d}.good{background:#e0f2fe;color:#0369a1}
      .pass{background:#fef3c7;color:#b45309}.fail-badge{background:#fee2e2;color:#b91c1c}
      .absent{background:#fee2e2;color:#b91c1c}.present{background:#dcfce7;color:#15803d}
    </style></head><body>
    ${schoolHeader}
    <h2>تقرير الأداء الأكاديمي الشامل</h2>
    <h3 style="text-align:center; border:none;">الطالب: ${this.detailStudent.studentName} — ${this.selectedYearName}</h3>`;

    if (this.studentGrades.length > 0) {
      html += `<h3>الدرجات النهائية</h3><table><thead><tr><th>#</th><th>المادة</th><th>الفصل</th><th>المجموع</th><th>النسبة</th><th>التقدير</th></tr></thead><tbody>`;
      this.studentGrades.forEach((g: any, i: number) => {
        const badge = this.getGradeBadge(g.percentage);
        const badgeClass = g.percentage >= 90 ? 'exc' : g.percentage >= 60 ? 'good' : g.percentage >= 50 ? 'pass' : 'fail-badge';
        html += `<tr><td>${i+1}</td><td>${g.subjectName ?? ''}</td><td>${this.termLabel(g.academicTerm)}</td><td>${g.total}/${g.maxTotal}</td><td>${g.percentage}%</td><td><span class="badge ${badgeClass}">${badge.label}</span></td></tr>`;
      });
      html += '</tbody></table>';
    }

    if (this.studentAssessments.length > 0) {
      html += `<h3>التقييمات الدورية</h3><table><thead><tr><th>#</th><th>المادة</th><th>النوع</th><th>الفصل</th><th>الدرجة</th><th>القصوى</th></tr></thead><tbody>`;
      this.studentAssessments.forEach((a: any, i: number) => {
        html += `<tr><td>${i+1}</td><td>${a.subjectName ?? ''}</td><td>${a.assessmentType}</td><td>${this.termLabel(a.term)}</td><td>${a.score}</td><td>${a.maxScore}</td></tr>`;
      });
      html += '</tbody></table>';
    }

    if (this.studentAbsences?.length > 0) {
      html += `<h3>سجل الغياب</h3><table><thead><tr><th>#</th><th>المادة</th><th>التاريخ</th><th>الحالة</th><th>السبب</th></tr></thead><tbody>`;
      this.studentAbsences.forEach((a: any, i: number) => {
        const statCls = a.isAbsent ? 'absent' : 'present';
        const statLbl = a.isAbsent ? 'غائب' : 'حاضر';
        html += `<tr><td>${i+1}</td><td>${a.subjectName ?? ''}</td><td>${a.absenceDate}</td><td><span class="badge ${statCls}">${statLbl}</span></td><td>${a.reason ?? ''}</td></tr>`;
      });
      html += '</tbody></table>';
    }

    html += '</body></html>';
    const win = window.open('', '_blank');
    if (win) { win.document.write(html); win.document.close(); win.print(); }
  }

  // ── Export ──────────────────────────────────

  exportExcel() {
    let rows: string[][] = [];

    // Add School Profile Header
    const p = this.schoolProfile();
    if (p) {
      rows.push([p.schoolName || '']);
      if (p.governorate || p.directorate) rows.push([`${p.governorate || ''} - ${p.directorate || ''}`]);
      if (p.educationalAdministration) rows.push([p.educationalAdministration]);
      rows.push(['']); // Empty row
    }

    if (this.activeTab === 'grades') {
      rows.push(['#', 'الطالب', 'المادة', 'الفصل', 'متوسط السنة', 'اختبار 1', 'اختبار 2', 'امتحان الترم', 'المجموع', 'النسبة', 'التقدير']);
      this.sortedGrades.forEach((g, i) => {
        rows.push([
          String(i + 1), g.studentName ?? '', g.subjectName ?? '', this.termLabel(g.academicTerm),
          String(g.periodAvgScore), String(g.assessment1Score), String(g.assessment2Score),
          String(g.finalExamScore), `${g.total}/${g.maxTotal}`, `${g.percentage}%`,
          this.getGradeBadge(g.percentage).label
        ]);
      });
    } else if (this.activeTab === 'evaluations') {
      rows.push(['#', 'الطالب', 'عنصر التقييم', 'الفترة', 'الدرجة', 'القصوى']);
      this.filteredEvaluations.forEach((e, i) => {
        rows.push([String(i + 1), e.studentName ?? '', e.itemName ?? '', e.periodName ?? '', String(e.score ?? ''), String(e.maxScore)]);
      });
    } else if (this.activeTab === 'assessments') {
      rows.push(['#', 'الطالب', 'المادة', 'النوع', 'الفصل', 'الدرجة', 'القصوى']);
      this.filteredAssessments.forEach((a, i) => {
        rows.push([String(i + 1), a.studentName ?? '', a.subjectName ?? '', a.assessmentType, this.termLabel(a.term), String(a.score), String(a.maxScore)]);
      });
    } else if (this.activeTab === 'absences') {
      rows.push(['#', 'الطالب', 'المادة', 'التاريخ', 'الحالة', 'السبب']);
      this.filteredAbsences.forEach((a, i) => {
        rows.push([String(i + 1), a.studentName ?? '', a.subjectName ?? '', a.absenceDate, a.isAbsent ? 'غائب' : 'حاضر', a.reason ?? '']);
      });
    }

    const csv = rows.map(r => r.join(',')).join('\n');
    const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `historical-${this.activeTab}-${this.selectedClassName}-${this.selectedYearName}.csv`;
    link.click();
  }

  exportPDF() {
    const p = this.schoolProfile();
    const schoolHeader = p ? `
      <div style="text-align: center; margin-bottom: 20px; padding-bottom: 10px; border-bottom: 2px solid #00236f;">
        <h1 style="color:#00236f; margin:0 0 5px;">${p.schoolName || ''}</h1>
        <p style="margin:2px 0;">${p.governorate || ''} - ${p.directorate || ''}</p>
        <p style="margin:2px 0;">${p.educationalAdministration || ''}</p>
      </div>
    ` : '';

    let html = `<html><head><meta charset="utf-8"><style>
      body{font-family:sans-serif;direction:rtl;padding:20px}
      h2{color:#00236f;text-align:center;}table{width:100%;border-collapse:collapse;margin-top:12px;page-break-inside:auto;}
      tr{page-break-inside:avoid;page-break-after:auto;}
      th{background:#00236f;color:#fff;padding:8px;font-size:12px}
      td{padding:6px;border-bottom:1px solid #eee;font-size:12px;text-align:center}
      .fail{background:#fff7f7}.badge{padding:2px 8px;border-radius:10px;font-size:10px;font-weight:700}
      .exc{background:#dcfce7;color:#15803d}.good{background:#e0f2fe;color:#0369a1}
      .pass{background:#fef3c7;color:#b45309}.fail-badge{background:#fee2e2;color:#b91c1c}
      .absent{background:#fee2e2;color:#b91c1c}.present{background:#dcfce7;color:#15803d}
    </style></head><body>
    ${schoolHeader}
    <h2>البيانات التاريخية — ${this.selectedClassName} — ${this.selectedYearName}</h2>`;

    if (this.activeTab === 'grades') {
      html += `<table><thead><tr><th>#</th><th>الطالب</th><th>المادة</th><th>الفصل</th><th>المجموع</th><th>النسبة</th><th>التقدير</th></tr></thead><tbody>`;
      this.sortedGrades.forEach((g, i) => {
        const badge = this.getGradeBadge(g.percentage);
        const badgeClass = g.percentage >= 90 ? 'exc' : g.percentage >= 60 ? 'good' : g.percentage >= 50 ? 'pass' : 'fail-badge';
        html += `<tr class="${g.percentage < 50 ? 'fail' : ''}"><td>${i+1}</td><td>${g.studentName ?? ''}</td><td>${g.subjectName ?? ''}</td><td>${this.termLabel(g.academicTerm)}</td><td>${g.total}/${g.maxTotal}</td><td>${g.percentage}%</td><td><span class="badge ${badgeClass}">${badge.label}</span></td></tr>`;
      });
      html += '</tbody></table>';
    } else if (this.activeTab === 'evaluations') {
      html += `<table><thead><tr><th>#</th><th>الطالب</th><th>عنصر التقييم</th><th>الفترة</th><th>الدرجة</th><th>القصوى</th></tr></thead><tbody>`;
      this.filteredEvaluations.forEach((e, i) => {
        html += `<tr><td>${i+1}</td><td>${e.studentName ?? ''}</td><td>${e.itemName ?? ''}</td><td>${e.periodName ?? ''}</td><td>${e.score ?? ''}</td><td>${e.maxScore}</td></tr>`;
      });
      html += '</tbody></table>';
    } else if (this.activeTab === 'assessments') {
      html += `<table><thead><tr><th>#</th><th>الطالب</th><th>المادة</th><th>النوع</th><th>الفصل</th><th>الدرجة</th><th>القصوى</th></tr></thead><tbody>`;
      this.filteredAssessments.forEach((a, i) => {
        html += `<tr><td>${i+1}</td><td>${a.studentName ?? ''}</td><td>${a.subjectName ?? ''}</td><td>${a.assessmentType}</td><td>${this.termLabel(a.term)}</td><td>${a.score}</td><td>${a.maxScore}</td></tr>`;
      });
      html += '</tbody></table>';
    } else if (this.activeTab === 'absences') {
      html += `<table><thead><tr><th>#</th><th>الطالب</th><th>المادة</th><th>التاريخ</th><th>الحالة</th><th>السبب</th></tr></thead><tbody>`;
      this.filteredAbsences.forEach((a, i) => {
        const statCls = a.isAbsent ? 'absent' : 'present';
        const statLbl = a.isAbsent ? 'غائب' : 'حاضر';
        html += `<tr><td>${i+1}</td><td>${a.studentName ?? ''}</td><td>${a.subjectName ?? ''}</td><td>${a.absenceDate}</td><td><span class="badge ${statCls}">${statLbl}</span></td><td>${a.reason ?? ''}</td></tr>`;
      });
      html += '</tbody></table>';
    }

    html += '</body></html>';
    const win = window.open('', '_blank');
    if (win) { win.document.write(html); win.document.close(); win.print(); }
  }

  // ── Helpers ─────────────────────────────────

  getGradeBadge(pct: number): { label: string; cls: string } {
    if (pct >= 90) return { label: 'ممتاز', cls: 'badge-excellent' };
    if (pct >= 75) return { label: 'جيد جداً', cls: 'badge-vgood' };
    if (pct >= 60) return { label: 'جيد', cls: 'badge-good' };
    if (pct >= 50) return { label: 'مقبول', cls: 'badge-pass' };
    return { label: 'ضعيف', cls: 'badge-fail' };
  }

  termLabel(term: number | string | undefined): string {
    const n = Number(term);
    if (n === 1) return 'الفصل الأول';
    if (n === 2) return 'الفصل الثاني';
    return 'السنة كاملة';
  }

  pctColor(pct: number): string {
    if (pct >= 75) return '#16a34a';
    if (pct >= 50) return '#2563eb';
    if (pct >= 40) return '#f59e0b';
    return '#dc2626';
  }

  get classTotalStudents(): number {
    return this.overview?.totalStudents ?? this.students.length;
  }

  // ── Filtered + Sorted grades ────────────────

  get sortedGrades(): HistoricalFinalGrade[] {
    let grades = [...this.finalGrades];
    if (this.studentSearch) {
      const q = this.studentSearch.toLowerCase();
      grades = grades.filter(g => (g.studentName ?? '').toLowerCase().includes(q));
    }
    if (this.selectedSubjectName) {
      grades = grades.filter(g => g.subjectName === this.selectedSubjectName);
    }
    if (this.selectedGradeFilter) {
      grades = grades.filter(g => {
        if (this.selectedGradeFilter === 'exc') return g.percentage >= 90;
        if (this.selectedGradeFilter === 'vgood') return g.percentage >= 75 && g.percentage < 90;
        if (this.selectedGradeFilter === 'good') return g.percentage >= 60 && g.percentage < 75;
        if (this.selectedGradeFilter === 'pass') return g.percentage >= 50 && g.percentage < 60;
        if (this.selectedGradeFilter === 'fail') return g.percentage < 50;
        return true;
      });
    }
    grades.sort((a, b) => {
      let aVal: any, bVal: any;
      switch (this.sortCol) {
        case 'studentName': aVal = a.studentName ?? ''; bVal = b.studentName ?? ''; break;
        case 'subjectName': aVal = a.subjectName ?? ''; bVal = b.subjectName ?? ''; break;
        case 'academicTerm': aVal = a.academicTerm; bVal = b.academicTerm; break;
        case 'percentage': default: aVal = a.percentage ?? 0; bVal = b.percentage ?? 0; break;
      }
      if (typeof aVal === 'string') return this.sortDir === 'asc' ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
      return this.sortDir === 'asc' ? aVal - bVal : bVal - aVal;
    });
    return grades;
  }

  get filteredStats() {
    const g = this.sortedGrades;
    const total = g.length;
    const pass = g.filter(x => (x.percentage ?? 0) >= 50).length;
    const fail = total - pass;
    const avg = total > 0 ? Math.round(g.reduce((s, x) => s + (x.percentage ?? 0), 0) / total) : 0;
    return { total, pass, fail, avg };
  }

  get filteredEvaluations(): HistoricalEvaluation[] {
    let evs = [...this.evaluations];
    if (this.studentSearch) {
      const q = this.studentSearch.toLowerCase();
      evs = evs.filter(e => (e.studentName ?? '').toLowerCase().includes(q));
    }
    return this.applySorting(evs);
  }

  get pagedEvaluations(): HistoricalEvaluation[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.filteredEvaluations.slice(start, start + this.pageSize);
  }

  get evalTotalPages(): number {
    return Math.max(1, Math.ceil(this.filteredEvaluations.length / this.pageSize));
  }

  get filteredAssessments(): HistoricalAssessment[] {
    let as = [...this.assessments];
    if (this.studentSearch) {
      const q = this.studentSearch.toLowerCase();
      as = as.filter(a => (a.studentName ?? '').toLowerCase().includes(q));
    }
    if (this.selectedSubjectName) {
      as = as.filter(a => a.subjectName === this.selectedSubjectName);
    }
    return this.applySorting(as);
  }

  get pagedAssessments(): HistoricalAssessment[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.filteredAssessments.slice(start, start + this.pageSize);
  }

  get assessTotalPages(): number {
    return Math.max(1, Math.ceil(this.filteredAssessments.length / this.pageSize));
  }

  get filteredAbsences(): HistoricalAbsence[] {
    let abs = this.absences;
    if (this.studentSearch) {
      const q = this.studentSearch.toLowerCase();
      abs = abs.filter(a => (a.studentName ?? '').toLowerCase().includes(q));
    }
    if (this.selectedSubjectName) {
      abs = abs.filter(a => a.subjectName === this.selectedSubjectName);
    }
    if (this.absenceDateFrom) {
      const from = new Date(this.absenceDateFrom).getTime();
      abs = abs.filter(a => new Date(a.absenceDate).getTime() >= from);
    }
    if (this.absenceDateTo) {
      const to = new Date(this.absenceDateTo).getTime();
      abs = abs.filter(a => new Date(a.absenceDate).getTime() <= to);
    }
    if (this.absenceThreshold != null && this.absenceThreshold > 0) {
      const counts: Record<string, number> = {};
      abs.forEach(a => {
        if (a.isAbsent) {
          counts[a.studentName ?? ''] = (counts[a.studentName ?? ''] || 0) + 1;
        }
      });
      abs = abs.filter(a => (counts[a.studentName ?? ''] || 0) >= this.absenceThreshold!);
    }
    return this.applySorting(abs);
  }

  private applySorting(array: any[]) {
    if (!this.sortCol || !array.length) return array;
    return array.sort((a, b) => {
      let aVal = a[this.sortCol];
      let bVal = b[this.sortCol];
      if (this.sortCol === 'absenceDate') {
        aVal = new Date(aVal).getTime();
        bVal = new Date(bVal).getTime();
      }
      if (typeof aVal === 'string') return this.sortDir === 'asc' ? aVal.localeCompare(bVal) : bVal.localeCompare(aVal);
      if (aVal < bVal) return this.sortDir === 'asc' ? -1 : 1;
      if (aVal > bVal) return this.sortDir === 'asc' ? 1 : -1;
      return 0;
    });
  }

  get pagedAbsences(): HistoricalAbsence[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.filteredAbsences.slice(start, start + this.pageSize);
  }

  get absencesTotalPages(): number {
    return Math.max(1, Math.ceil(this.filteredAbsences.length / this.pageSize));
  }

  get subjectNames(): string[] {
    return [...new Set(this.finalGrades.map(g => g.subjectName).filter(Boolean))] as string[];
  }

  get studentGradeSummary(): { studentId: number; studentName: string; grades: HistoricalFinalGrade[]; avg: number }[] {
    const grouped = new Map<number, { studentId: number; studentName: string; grades: HistoricalFinalGrade[] }>();
    for (const g of this.finalGrades) {
      const sid = g.studentId;
      if (!grouped.has(sid)) grouped.set(sid, { studentId: sid, studentName: g.studentName ?? '', grades: [] });
      grouped.get(sid)!.grades.push(g);
    }
    return Array.from(grouped.values()).map(entry => ({
      ...entry,
      avg: entry.grades.length > 0 ? Math.round(entry.grades.reduce((s, g) => s + (g.percentage ?? 0), 0) / entry.grades.length) : 0
    })).sort((a, b) => b.avg - a.avg);
  }
}
