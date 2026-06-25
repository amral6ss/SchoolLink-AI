import {
  Component, signal, computed, OnInit, AfterViewInit,
  OnDestroy, inject, ElementRef, ViewChild, PLATFORM_ID
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { AuthService } from '../../core/services/auth.service';
import { AcademicYearService } from '../../core/services/academic-year.service';
import {
  StudentDashboardService,
  StudentDashboardData,
  WeeklyPerformance,
  SubjectPerformance
} from './student-dashboard.service';
import { switchMap, of, forkJoin } from 'rxjs';
import { Chart, registerables } from 'chart.js';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

Chart.register(...registerables);

@Component({
  selector: 'app-student-dashboard',
  imports: [Sidebar, CommonModule],
  templateUrl: './student-dashboard.html',
  styleUrl: './student-dashboard.css'
})
export class StudentDashboard implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('evolutionCanvas') evolutionCanvasRef!: ElementRef<HTMLCanvasElement>;

  private platformId = inject(PLATFORM_ID);
  auth = inject(AuthService);
  private service = inject(StudentDashboardService);
  private academicYearService = inject(AcademicYearService);
  private router = inject(Router);

  sidebarOpen = signal(false);
  loading = signal(true);
  data = signal<StudentDashboardData | null>(null);
  selectedTerm = signal<number>(1);
  selectedWeekIndex = signal<number>(-1); // -1 = latest

  private chart: Chart | null = null;
  private enrollmentId: number | null = null;

  // ── Computed helpers ───────────────────────────────────────────────────
  selectedWeek = computed<WeeklyPerformance | null>(() => {
    const d = this.data();
    if (!d || !d.weeklyPerformances?.length) return null;
    const idx = this.selectedWeekIndex();
    return idx >= 0 ? d.weeklyPerformances[idx] : d.weeklyPerformances[d.weeklyPerformances.length - 1];
  });

  displayedSubjects = computed<SubjectPerformance[]>(() => {
    const week = this.selectedWeek();
    if (week?.subjectPerformances?.length) return week.subjectPerformances;
    return this.data()?.subjectPerformances ?? [];
  });

  get levelText(): string {
    const pct = this.data()?.performance ?? 0;
    if (pct >= 90) return 'ممتاز';
    if (pct >= 75) return 'جيد جداً';
    if (pct >= 60) return 'جيد';
    if (pct >= 50) return 'مقبول';
    return 'ضعيف';
  }

  get levelColor(): string {
    const pct = this.data()?.performance ?? 0;
    if (pct >= 90) return '#22c55e';
    if (pct >= 75) return '#3b82f6';
    if (pct >= 60) return '#f59e0b';
    if (pct >= 50) return '#f97316';
    return '#ef4444';
  }

  subjectGrade(s: SubjectPerformance): string {
    const pct = s.maxScore > 0 ? (s.score / s.maxScore) * 100 : 0;
    if (pct >= 90) return 'ممتاز';
    if (pct >= 75) return 'جيد جداً';
    if (pct >= 60) return 'جيد';
    if (pct >= 50) return 'مقبول';
    return 'ضعيف';
  }

  subjectColor(s: SubjectPerformance): string {
    const pct = s.maxScore > 0 ? (s.score / s.maxScore) * 100 : 0;
    if (pct >= 90) return '#22c55e';
    if (pct >= 75) return '#3b82f6';
    if (pct >= 60) return '#f59e0b';
    if (pct >= 50) return '#f97316';
    return '#ef4444';
  }

  subjectPct(s: SubjectPerformance): number {
    return s.maxScore > 0 ? Math.round((s.score / s.maxScore) * 100) : 0;
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────
  ngOnInit() {
    this.academicYearService.getCurrentTerm().subscribe({
      next: (res) => {
        if (res?.data != null) {
          // Backend may serialise AcademicTerm as string name ("SecondSemester")
          // or as integer — normalise to int before storing
          this.selectedTerm.set(this.termToInt(res.data));
        }
        this.loadData();
      },
      error: () => this.loadData()
    });
  }

  /** Convert an AcademicTerm value (int OR enum-name string) to an integer */
  private termToInt(val: any): number {
    if (typeof val === 'number') return val;
    if (typeof val === 'string') {
      if (val === 'FirstSemester')  return 1;
      if (val === 'SecondSemester') return 2;
      if (val === 'Final')          return 3;
      const n = parseInt(val, 10);
      return isNaN(n) ? 1 : n;
    }
    return 1;
  }

  ngAfterViewInit() {
    // Chart is built after data loads
  }

  ngOnDestroy() {
    this.chart?.destroy();
  }

  loadData() {
    this.loading.set(true);
    this.chart?.destroy();
    this.chart = null;

    this.service.loadDashboard(this.selectedTerm()).pipe(
      switchMap(raw => {
        if (!raw) { this.loading.set(false); return of(null); }

        // Map backend camelCase to our typed interface
        const absences = raw.absences ?? 0;
        // If no absence records exist at all, backend may return 0 instead of 100 —
        // correct this: a student with 0 absences has 100% attendance by definition.
        const rawRate = raw.attendanceRate ?? 100;
        const attendanceRate = (rawRate === 0 && absences === 0) ? 100 : rawRate;

        const d: StudentDashboardData = {
          name: raw.name ?? '',
          grade: raw.grade ?? '',
          class: raw.class ?? '',
          performance: raw.performance ?? 0,
          grades: raw.grades ?? { last: '—', total: '—' },
          absences,
          attendanceRate,
          excusedAbsences: raw.excusedAbsences ?? 0,
          unexcusedAbsences: raw.unexcusedAbsences ?? 0,
          currentTermName: raw.currentTermName ?? '',
          subjectPerformances: raw.subjectPerformances ?? [],
          weeklyPerformances: raw.weeklyPerformances ?? [],
          monthlyExams: raw.monthlyExams ?? [],
          finalExams: raw.finalExams ?? [],
          upcomingExams: raw.upcomingExams ?? [],
          recommendationSections: raw.recommendationSections ?? [],
          assignments: [],
          sessions: [],
        };
        this.data.set(d);
        this.selectedWeekIndex.set(-1);

        // Now load the student context to get enrollment for side data
        return this.service.getStudentContext();
      }),
      switchMap(ctx => {
        if (!ctx) { this.loading.set(false); return of(null); }
        return this.service.getEnrollmentId(ctx.studentId, ctx.academicYearId).pipe(
          switchMap(enrollId => {
            if (!enrollId) { this.loading.set(false); return of(null); }
            this.enrollmentId = enrollId;
            return this.service.loadSideData(enrollId);
          })
        );
      })
    ).subscribe({
      next: sideData => {
        this.loading.set(false);
        if (sideData) {
          const cur = this.data();
          if (cur) {
            this.data.set({ ...cur, ...sideData });
          }
        }
        setTimeout(() => this.buildChart(), 50);
      },
      error: () => this.loading.set(false)
    });
  }

  onTermChange(event: Event) {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedTerm.set(Number(value));
    this.loadData();
  }

  onWeekChange(event: Event) {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedWeekIndex.set(Number(value));
  }

  // ── Chart ──────────────────────────────────────────────────────────────
  buildChart() {
    if (!isPlatformBrowser(this.platformId)) return;
    const canvas = this.evolutionCanvasRef?.nativeElement;
    if (!canvas) return;
    const weeks = this.data()?.weeklyPerformances ?? [];
    if (!weeks.length) return;

    this.chart?.destroy();

    const labels = weeks.map(w => w.periodName || `الأسبوع ${w.weekNumber}`);
    const scores = weeks.map(w => w.avgScore);

    this.chart = new Chart(canvas, {
      type: 'line',
      data: {
        labels,
        datasets: [{
          label: 'الأداء الأسبوعي %',
          data: scores,
          borderColor: '#6366f1',
          backgroundColor: 'rgba(99,102,241,0.08)',
          borderWidth: 2.5,
          pointBackgroundColor: '#6366f1',
          pointBorderColor: '#ffffff',
          pointBorderWidth: 2,
          pointRadius: 5,
          pointHoverRadius: 8,
          fill: true,
          tension: 0.35,
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        onClick: (_evt, elements) => {
          if (elements.length > 0) {
            this.selectedWeekIndex.set(elements[0].index);
          }
        },
        scales: {
          y: {
            min: 0, max: 100,
            grid: { color: 'rgba(0,0,0,0.05)' },
            ticks: { color: '#94a3b8', font: { family: 'Cairo' }, callback: (v: any) => v + '%' }
          },
          x: {
            grid: { display: false },
            ticks: { color: '#94a3b8', font: { family: 'Cairo' } }
          }
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: '#1e293b',
            titleColor: '#e2e8f0',
            bodyColor: '#94a3b8',
            padding: 10,
            cornerRadius: 8,
            rtl: true,
            callbacks: {
              label: ctx => ` ${ctx.parsed.y}%`
            }
          }
        }
      }
    });
  }

  trackBySubject(_: number, s: SubjectPerformance) { return s.subjectName; }
  trackByWeek(_: number, w: WeeklyPerformance) { return w.weekNumber; }

  navigateToChatAi() {
    this.router.navigate(['/chat-ai']);
  }

  navigateToLibrary() {
    this.router.navigate(['/digital-library']);
  }
}
