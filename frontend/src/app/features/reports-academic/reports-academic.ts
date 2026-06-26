import {
  Component,
  signal,
  AfterViewInit,
  OnDestroy,
  ElementRef,
  ViewChild,
  inject,
  OnInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { HttpClient } from '@angular/common/http';
import { buildApiUrl } from '../../core/utils/api-url';
import { PdfExportService, PdfAcademicReportData, PdfAcademicMonthGroup } from '../../core/services/pdf-export.service';
import { Chart, registerables } from 'chart.js';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

Chart.register(...registerables);

// ── API response types (mirrors backend AcademicReportDto) ──

interface ClassItem {
  id: number;
  name: string;
  gradeLevelId?: number;
  gradeLevelName?: string;
  academicYearId: number;
}

interface EvaluationPeriod {
  id: number;
  name: string;
  periodType: string;
  orderNum: number;
  monthName?: string;
  semesterNumber?: number | null;
}

interface MonthGroup {
  monthName: string;
  periods: EvaluationPeriod[];
}

interface StudentRow {
  name: string;
  enrollmentId: number;
  weeklyScores: { periodId: number; periodName: string; avg: number; max: number; rawScore: number; rawMax: number }[];
  assessment1: number;
  assessment2: number;
  totalMonthly: number;
  finalTotal: number;
  maxTotal: number;
  percentage: number;
}

interface MonthlyExamEntry {
  enrollmentId: number;
  studentName: string;
  exam1Score: number;
  exam1Max: number;
  exam1Month: string;
  exam2Score: number;
  exam2Max: number;
  exam2Month: string;
  semesterScore: number;
  semesterMax: number;
}

interface AcademicReportResponse {
  className: string;
  termLabel: string;
  studentCount: number;
  subjectName: string;
  avgPercent: number;
  avgAssessment1: number;
  avgAssessment2: number;
  avgFinal: number;
  weeklyPeriods: EvaluationPeriod[];
  monthGroups: MonthGroup[];
  students: StudentRow[];
  monthlyExams: MonthlyExamEntry[];
}

interface ApiResult<T> {
  isSuccess: boolean;
  data: T;
  message: string;
}

@Component({
  selector: 'app-reports-academic',
  imports: [Sidebar, CommonModule, FormsModule],
  templateUrl: './reports-academic.html',
  styleUrl: './reports-academic.css',
})
export class ReportsAcademic implements AfterViewInit, OnDestroy, OnInit {
  @ViewChild('weeklyChart') weeklyChartCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('monthlyChart') monthlyChartCanvas!: ElementRef<HTMLCanvasElement>;

  private http = inject(HttpClient);
  private route = inject(ActivatedRoute);
  private base = buildApiUrl();
  private pdfExport = inject(PdfExportService);

  sidebarOpen = signal(false);
  chart1: Chart | null = null;
  chart2: Chart | null = null;

  // State
  loading = signal(false);
  classes: ClassItem[] = [];
  selectedClassId: number | null = null;
  selectedTerm: number = 1;
  subjects: { id: number; name: string }[] = [];
  selectedSubjectId: number | null = null;

  // Grade-level aggregation
  gradeLevelMode = false;
  gradeLevels: { id: number; name: string }[] = [];
  selectedGradeLevelId: number | null = null;

  // Data
  studentRows: StudentRow[] = [];
  weeklyPeriods: EvaluationPeriod[] = [];
  monthlyExamData: MonthlyExamEntry[] = [];

  // Summary stats
  avgPercent = 0;
  avgAssessment1 = 0;
  avgAssessment2 = 0;
  avgFinal = 0;

  // Pagination
  currentPage = 1;
  pageSize = 10;

  // Expanded rows
  expandedRows = new Set<number>();

  toggleRow(enrollmentId: number) {
    if (this.expandedRows.has(enrollmentId)) {
      this.expandedRows.delete(enrollmentId);
    } else {
      this.expandedRows.add(enrollmentId);
    }
  }

  isExpanded(enrollmentId: number): boolean {
    return this.expandedRows.has(enrollmentId);
  }

  get paginatedRows(): StudentRow[] {
    const start = (this.currentPage - 1) * this.pageSize;
    return this.studentRows.slice(start, start + this.pageSize);
  }

  get totalPages(): number {
    return Math.ceil(this.studentRows.length / this.pageSize);
  }

  get paginationInfo(): string {
    const start = (this.currentPage - 1) * this.pageSize + 1;
    const end = Math.min(this.currentPage * this.pageSize, this.studentRows.length);
    return `عرض ${start} - ${end} من أصل ${this.studentRows.length} طالب`;
  }

  get topStudents(): StudentRow[] {
    return [...this.studentRows].sort((a, b) => b.percentage - a.percentage);
  }

  get monthGroups(): MonthGroup[] {
    const map = new Map<string, EvaluationPeriod[]>();
    for (const p of this.weeklyPeriods) {
      const key = p.monthName ?? 'غير محدد';
      if (!map.has(key)) map.set(key, []);
      map.get(key)!.push(p);
    }
    return Array.from(map.entries()).map(([monthName, periods]) => ({ monthName, periods }));
  }

  getMonthRaw(row: StudentRow, monthName: string): number {
    const group = this.monthGroups.find(g => g.monthName === monthName);
    if (!group) return 0;
    const scores = row.weeklyScores.filter(ws => group.periods.some(p => p.id === ws.periodId));
    if (scores.length === 0) return 0;
    const avg = scores.reduce((s, ws) => s + ws.rawScore, 0) / scores.length;
    return Math.round(avg * 10) / 10;
  }

  getMonthRawMax(row: StudentRow, monthName: string): number {
    const group = this.monthGroups.find(g => g.monthName === monthName);
    if (!group) return 0;
    const scores = row.weeklyScores.filter(ws => group.periods.some(p => p.id === ws.periodId));
    if (scores.length === 0) return 0;
    const avgMax = scores.reduce((s, ws) => s + ws.rawMax, 0) / scores.length;
    return Math.round(avgMax * 10) / 10;
  }

  getMonthPct(row: StudentRow, monthName: string): number {
    const raw = this.getMonthRaw(row, monthName);
    const max = this.getMonthRawMax(row, monthName);
    return max > 0 ? Math.round((raw / max) * 100) : 0;
  }

  /** Returns the max value for a month column (from the first available student) */
  getMonthGlobalMax(monthName: string): number {
    for (const row of this.studentRows) {
      const max = this.getMonthRawMax(row, monthName);
      if (max > 0) return max;
    }
    return 0;
  }

  getWeeksForMonth(row: StudentRow, monthName: string): { periodName: string; rawScore: number; rawMax: number }[] {
    const group = this.monthGroups.find(g => g.monthName === monthName);
    if (!group) return [];
    return row.weeklyScores
      .filter(ws => group.periods.some(p => p.id === ws.periodId))
      .map(ws => ({ periodName: ws.periodName, rawScore: ws.rawScore, rawMax: ws.rawMax }));
  }

  get totalColCount(): number {
    return 2 + this.monthGroups.length + 5;
  }

  nextPage() {
    if (this.currentPage < this.totalPages) this.currentPage++;
  }

  prevPage() {
    if (this.currentPage > 1) this.currentPage--;
  }

  goToPage(page: number) {
    if (page >= 1 && page <= this.totalPages) this.currentPage = page;
  }

  getPageNumbers(): number[] {
    const pages = [];
    const total = this.totalPages;
    const current = this.currentPage;
    let start = Math.max(1, current - 2);
    let end = Math.min(total, start + 4);
    if (end - start < 4) start = Math.max(1, end - 4);
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  }

  getRowIndex(localIndex: number): number {
    return (this.currentPage - 1) * this.pageSize + localIndex;
  }

  ngOnInit() {
    this.loadInitialData();
  }

  ngAfterViewInit() {
    // charts created after data loads
  }

  ngOnDestroy() {
    this.chart1?.destroy();
    this.chart2?.destroy();
  }

  toggleGradeLevelMode() {
    this.gradeLevelMode = !this.gradeLevelMode;
    if (this.gradeLevelMode) {
      // Sync selected grade level from current class's grade level
      const currentClass = this.classes.find(c => c.id === this.selectedClassId);
      if (currentClass?.gradeLevelId) {
        this.selectedGradeLevelId = currentClass.gradeLevelId;
      }
    }
    this.loadReport();
  }

  loadInitialData() {
    this.loading.set(true);

    // Read query params if navigated from dashboard
    const qpClassId = this.route.snapshot.queryParamMap.get('classId');
    const qpSubjectId = this.route.snapshot.queryParamMap.get('subjectId');

    forkJoin({
      classRes: this.http.get<any>(`${this.base}/class-management`).pipe(catchError(() => of([]))),
      subjectsRes: this.http.get<any>(`${this.base}/subjects`).pipe(catchError(() => of({ data: [] })))
    }).subscribe({
      next: ({ classRes, subjectsRes }) => {
        const rawClasses = Array.isArray(classRes) ? classRes : (classRes?.data ?? []);
        this.classes = rawClasses;

        // Build distinct grade levels from classes
        const glMap = new Map<number, string>();
        for (const c of rawClasses) {
          if (c.gradeLevelId && c.gradeLevelName) {
            glMap.set(c.gradeLevelId, c.gradeLevelName);
          }
        }
        this.gradeLevels = Array.from(glMap.entries()).map(([id, name]) => ({ id, name }));

        this.subjects = subjectsRes?.isSuccess ? (subjectsRes.data || []) : (subjectsRes ?? []);

        // Try query params first, fallback to defaults
        const qpClassIdNum = qpClassId ? Number(qpClassId) : null;
        const qpSubjectIdNum = qpSubjectId ? Number(qpSubjectId) : null;

        if (qpClassIdNum && this.classes.some(c => c.id === qpClassIdNum)) {
          this.selectedClassId = qpClassIdNum;
        } else if (this.classes.length > 0) {
          this.selectedClassId = this.classes[0].id;
        }

        if (this.gradeLevels.length > 0) {
          this.selectedGradeLevelId = this.gradeLevels[0].id;
        }

        if (qpSubjectIdNum && this.subjects.some(s => s.id === qpSubjectIdNum)) {
          this.selectedSubjectId = qpSubjectIdNum;
        } else if (!this.selectedSubjectId && this.subjects.length > 0) {
          this.selectedSubjectId = this.subjects[0].id;
        }

        if (this.selectedClassId && this.selectedSubjectId) {
          this.loadReport();
        } else {
          this.loading.set(false);
        }
      },
      error: () => this.loading.set(false)
    });
  }

  loadReport() {
    if (!this.selectedClassId || !this.selectedSubjectId) return;
    this.loading.set(true);
    this.studentRows = [];
    this.monthlyExamData = [];
    this.currentPage = 1;

    let url = `${this.base}/reports/academic?classId=${this.selectedClassId}&term=${this.selectedTerm}&subjectId=${this.selectedSubjectId}`;
    if (this.gradeLevelMode && this.selectedGradeLevelId) {
      url += `&gradeLevelId=${this.selectedGradeLevelId}`;
    }

    // ── Single consolidated API call ──
    this.http.get<ApiResult<AcademicReportResponse>>(url).pipe(
      catchError(err => {
        console.error('Academic report API error', err);
        return of({
          isSuccess: false,
          data: null as any,
          message: 'فشل تحميل التقرير'
        });
      })
    ).subscribe({
      next: (res) => {
        if (res.isSuccess && res.data) {
          const d = res.data;

          // Summary stats (already computed server-side)
          this.avgPercent = d.avgPercent;
          this.avgAssessment1 = d.avgAssessment1;
          this.avgAssessment2 = d.avgAssessment2;
          this.avgFinal = d.avgFinal;

          // Weekly periods (needed by monthGroups getter & chart)
          this.weeklyPeriods = d.weeklyPeriods;

          // Student rows (same shape the template expects)
          this.studentRows = d.students;

          // Monthly exam details
          this.monthlyExamData = d.monthlyExams;

          // Charts
          this.buildCharts();
        } else {
          this.weeklyPeriods = [];
          this.studentRows = [];
          this.monthlyExamData = [];
        }
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  buildCharts() {
    setTimeout(() => {
      this.chart1?.destroy();
      this.chart2?.destroy();
      this.createWeeklyChart();
      this.createMonthlyChart();
    }, 100);
  }

  private createWeeklyChart() {
    if (!this.weeklyChartCanvas) return;
    const ctx = this.weeklyChartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;

    const monthLabels = this.monthGroups.map(mg => mg.monthName);
    if (monthLabels.length === 0) {
      monthLabels.push('لا توجد بيانات');
    }

    // Compute class average per month
    const classAvgData = monthLabels.map(month => {
      if (this.studentRows.length === 0) return 0;
      const totalPct = this.studentRows.reduce((s, row) => s + this.getMonthPct(row, month), 0);
      return Math.round(totalPct / this.studentRows.length);
    });

    this.chart1 = new Chart(ctx, {
      type: 'bar',
      data: {
        labels: monthLabels,
        datasets: [
          {
            label: 'متوسط الفصل',
            data: classAvgData,
            backgroundColor: ['#4F46E5', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6'],
            borderRadius: 6,
            borderSkipped: false,
          },
        ],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            callbacks: {
              label: (ctx) => `${ctx.parsed.y}%`,
            },
          },
        },
        scales: {
          y: {
            beginAtZero: true,
            max: 100,
            ticks: { stepSize: 20, callback: (v) => v + '%' },
          },
        },
      },
    });
  }

  private createMonthlyChart() {
    if (!this.monthlyChartCanvas) return;
    const ctx = this.monthlyChartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;

    const colors = ['#4F46E5', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#06B6D4'];
    const labels = ['الاختبار الشهري 1', 'الاختبار الشهري 2', 'أعمال السنة', 'المجموع النهائي'];

    const datasets = this.studentRows.slice(0, 6).map((row, i) => ({
      label: row.name,
      data: [row.assessment1, row.assessment2, row.totalMonthly, row.finalTotal],
      backgroundColor: colors[i % colors.length],
      borderRadius: 4
    }));

    if (datasets.length === 0) {
      datasets.push({ label: 'لا توجد بيانات', data: [0, 0, 0, 0], backgroundColor: '#E5E7EB', borderRadius: 4 });
    }

    this.chart2 = new Chart(ctx, {
      type: 'bar',
      data: { labels, datasets },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { position: 'top', labels: { usePointStyle: true, font: { size: 11 } } },
        },
        scales: {
          y: { beginAtZero: true, ticks: { stepSize: 10 } },
        },
      },
    });
  }

  onClassChange() { this.loadReport(); }
  onTermChange() { this.loadReport(); }
  onSubjectChange() { this.loadReport(); }
  onGradeLevelChange() { this.loadReport(); }

  exportingPdf = signal(false);

  async exportPdf() {
    if (this.exportingPdf() || this.studentRows.length === 0) return;
    this.exportingPdf.set(true);
    try {
      const monthGroups: PdfAcademicMonthGroup[] = this.monthGroups.map(mg => ({
        monthName: mg.monthName,
        periodIds: mg.periods.map(p => p.id),
      }));

      // Calculate total student count
      const classData: PdfAcademicReportData = {
        className: this.selectedClassName,
        termLabel: this.termLabel,
        subjectName: this.subjects.find(s => s.id === this.selectedSubjectId)?.name || '',
        avgPercent: this.avgPercent,
        avgAssessment1: this.avgAssessment1,
        avgAssessment2: this.avgAssessment2,
        avgFinal: this.avgFinal,
        students: this.studentRows,
        monthlyExams: this.monthlyExamData,
        monthGroups,
        studentCount: this.studentRows.length,
      };

      await this.pdfExport.exportAcademicReport(classData);
    } finally {
      this.exportingPdf.set(false);
    }
  }

  getGradeBadge(pct: number): { label: string; cls: string } {
    if (pct >= 90) return { label: 'ممتاز', cls: 'badge-excellent' };
    if (pct >= 75) return { label: 'جيد جداً', cls: 'badge-vgood' };
    if (pct >= 60) return { label: 'جيد', cls: 'badge-good' };
    if (pct >= 50) return { label: 'مقبول', cls: 'badge-pass' };
    return { label: 'ضعيف', cls: 'badge-fail' };
  }

  get selectedClassName(): string {
    if (this.gradeLevelMode && this.selectedGradeLevelId) {
      const gl = this.gradeLevels.find(g => g.id === this.selectedGradeLevelId);
      return gl ? gl.name : '';
    }
    const c = this.classes.find(c => c.id === this.selectedClassId);
    return c ? (c.gradeLevelName ? `${c.gradeLevelName} - ${c.name}` : c.name) : '';
  }

  get termLabel(): string {
    return this.selectedTerm === 1 ? 'الفصل الدراسي الأول' : 'الفصل الدراسي الثاني';
  }

  get displayStudentCount(): string {
    return this.gradeLevelMode ? `${this.studentRows.length} طالب (كل الفصول)` : `${this.studentRows.length} طالب`;
  }
}
