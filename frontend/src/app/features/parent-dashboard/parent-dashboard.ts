import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal, ViewChild, ElementRef, AfterViewInit, effect } from '@angular/core';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { PdfExportService, PdfDashboardData } from '../../core/services/pdf-export.service';
import {
  ParentDashboardChild, ParentDashboardData, ParentDashboardService,
  ParentChildStats, ChildSubject, UpcomingExam, WeeklyPerformance, RecSection, FinalExamResult
} from '../../core/services/parent-dashboard.service';
import { AuthService } from '../../core/services/auth.service';
import { Chart, BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend, ArcElement, DoughnutController, LineController, LineElement, PointElement, Filler } from 'chart.js';

Chart.register(BarController, BarElement, CategoryScale, LinearScale, Tooltip, Legend, ArcElement, DoughnutController, LineController, LineElement, PointElement, Filler);

@Component({
  selector: 'app-parent-dashboard',
  standalone: true,
  imports: [CommonModule, Sidebar],
  templateUrl: './parent-dashboard.html',
  styleUrl: './parent-dashboard.css'
})
export class ParentDashboard implements OnInit, AfterViewInit {
  private parentDashboardService = inject(ParentDashboardService);
  private authService = inject(AuthService);
  private pdfExport = inject(PdfExportService);

  @ViewChild('absencesChart') absencesChartCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('weeklyChart') weeklyChartCanvas!: ElementRef<HTMLCanvasElement>;

  sidebarOpen = signal(false);
  displayUserName = this.authService.user()?.fullName || 'ولي الأمر';

  children = signal<ParentDashboardChild[]>([]);
  dashboardData = signal<ParentDashboardData | null>(null);
  isLoading = signal(true);
  errorMessage = signal<string | null>(null);

  totalChildren = signal(0);
  activeChildren = signal(0);
  inactiveChildren = signal(0);

  selectedChild = signal<ParentChildStats | null>(null);
  selectedWeekDetail = signal<WeeklyPerformance | null>(null);
  selectedWeek = signal<WeeklyPerformance | null>(null);
  selectedTerm = signal<number | null>(null);

  // Chart info popup
  chartInfo = signal<{
    name: string;
    performance: number;
    weeks: { name: string; pct: number; score: number; max: number }[];
    avgPct: number;
  } | null>(null);
  readonly terms = [
    { value: null, label: 'الترم الحالي' },
    { value: 1, label: 'الترم الأول' },
    { value: 2, label: 'الترم الثاني' },
  ];

  private performanceCharts: Chart[] = [];
  private absencesChart: Chart | null = null;
  private weeklyChart: Chart | null = null;

  ngOnInit(): void {
    this.loadData();
  }

  ngAfterViewInit() {
    setTimeout(() => this.buildCharts(), 400);
  }

  loadData(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.parentDashboardService.getMyChildren().subscribe({
      next: (res) => {
        const data = res?.data ?? res ?? [];
        this.children.set(Array.isArray(data) ? data : []);
        this.totalChildren.set(this.children().length);
        this.activeChildren.set(this.children().filter(c => c.isActive).length);
        this.inactiveChildren.set(this.children().filter(c => !c.isActive).length);

        this.parentDashboardService.getDashboard(this.selectedTerm() ?? undefined).subscribe({
          next: (dashRes) => {
            const dashData = dashRes?.data ?? null;
            this.dashboardData.set(dashData);
            if (dashData?.children?.length) {
              this.selectedChild.set(dashData.children[0]);
              this.selectedWeek.set(null);
            }
            this.isLoading.set(false);
            setTimeout(() => this.buildCharts(), 200);
          },
          error: () => {
            this.isLoading.set(false);
          }
        });
      },
      error: (err) => {
        this.children.set([]);
        this.errorMessage.set(err?.message || 'تعذر تحميل البيانات');
        this.isLoading.set(false);
      }
    });
  }

  selectChild(child: ParentChildStats): void {
    this.selectedChild.set(child);
    this.selectedWeek.set(null);
    setTimeout(() => this.buildCharts(), 100);
  }

  getGradeClass(score: number, max: number): string {
    const pct = max > 0 ? (score / max) * 100 : 0;
    if (pct >= 85) return 'grade-excellent';
    if (pct >= 70) return 'grade-good';
    if (pct >= 50) return 'grade-pass';
    return 'grade-fail';
  }

  getPerformanceColor(pct: number): string {
    if (pct >= 85) return '#10b981';
    if (pct >= 70) return '#f59e0b';
    if (pct >= 50) return '#f97316';
    return '#ef4444';
  }

  private buildCharts() {
    const d = this.dashboardData();
    if (!d || !d.children.length) return;
    this.buildPerformanceCharts(d.children);
    this.buildAbsencesChart(d.children);
    this.buildWeeklyChart();
  }

  private buildPerformanceCharts(children: ParentChildStats[]) {
    // Destroy old charts
    this.performanceCharts.forEach(c => c.destroy());
    this.performanceCharts = [];

    const mainColors = ['#6366f1', '#10b981', '#f59e0b', '#ec4899', '#06b6d4'];

    children.forEach((child, i) => {
      const canvas = document.getElementById('perfChart_' + i) as HTMLCanvasElement | null;
      if (!canvas) return;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;

      const perf = child.performance || 0;
      const rem = Math.max(100 - perf, 0);
      const color = mainColors[i % mainColors.length];

      const chart = new Chart(ctx, {
        type: 'doughnut',
        data: {
          labels: ['الأداء', 'المتبقي'],
          datasets: [{
            data: [perf, rem],
            backgroundColor: [color, '#e2e8f0'],
            borderWidth: 0,
            hoverOffset: 4,
          }]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          cutout: '72%',
          onClick: () => this.showChartInfo(child),
          plugins: {
            legend: { display: false },
            tooltip: {
              rtl: true,
              callbacks: {
                label: (c: any) => `${c.label}: ${c.raw}%`
              }
            }
          }
        },
        plugins: [{
          id: 'centerText_' + i,
          afterDraw(chart: any) {
            const { ctx: c2, chartArea: { top, left, width, height } } = chart;
            c2.save();
            c2.font = 'bold 18px Inter, sans-serif';
            c2.fillStyle = color;
            c2.textAlign = 'center';
            c2.textBaseline = 'middle';
            c2.fillText(perf + '%', left + width / 2, top + height / 2);
            c2.restore();
          }
        }]
      });

      this.performanceCharts.push(chart);
    });
  }

  private showChartInfo(child: ParentChildStats) {
    const weeks = (child.weeklyPerformances || []).map(w => ({
      name: w.periodName,
      pct: w.maxScore > 0 ? Math.round((w.avgScore / w.maxScore) * 100) : 0,
      score: Math.round(w.totalScore),
      max: Math.round(w.totalMaxScore)
    }));
    this.chartInfo.set({
      name: child.name,
      performance: child.performance,
      weeks,
      avgPct: child.performance
    });
  }

  closeChartInfo() {
    this.chartInfo.set(null);
  }

  private buildAbsencesChart(children: { name: string; absences: number }[]) {
    if (!this.absencesChartCanvas) return;
    const ctx = this.absencesChartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;
    if (this.absencesChart) this.absencesChart.destroy();

    const gradient = ctx.createLinearGradient(0, 0, 0, 200);
    gradient.addColorStop(0, '#f59e0b');
    gradient.addColorStop(1, '#fbbf24');

    this.absencesChart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels: children.map(c => c.name),
        datasets: [{
          label: 'الغيابات',
          data: children.map(c => c.absences),
          backgroundColor: gradient,
          borderRadius: 8,
          borderSkipped: false,
          barPercentage: 0.35,
          categoryPercentage: 0.6,
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            rtl: true,
            backgroundColor: '#1e1b4b',
            padding: 10,
            cornerRadius: 8,
          }
        },
        scales: {
          y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.05)' }, ticks: { stepSize: 1 } },
          x: { grid: { display: false } }
        }
      }
    });
  }

  private buildWeeklyChart() {
    if (!this.weeklyChartCanvas) return;
    const child = this.selectedChild();
    if (!child || !child.weeklyPerformances?.length) return;

    const ctx = this.weeklyChartCanvas.nativeElement.getContext('2d');
    if (!ctx) return;
    if (this.weeklyChart) this.weeklyChart.destroy();

    const weeks = child.weeklyPerformances.map(w => w.periodName);
    const scores = child.weeklyPerformances.map(w =>
      w.maxScore > 0 ? Math.round((w.avgScore / w.maxScore) * 100) : 0
    );

    const gradient = ctx.createLinearGradient(0, 0, 0, 250);
    gradient.addColorStop(0, 'rgba(99, 102, 241, 0.3)');
    gradient.addColorStop(1, 'rgba(99, 102, 241, 0.01)');

    this.weeklyChart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: weeks,
        datasets: [{
          label: 'نسبة الأداء %',
          data: scores,
          borderColor: '#6366f1',
          backgroundColor: gradient,
          fill: true,
          tension: 0.3,
          pointBackgroundColor: '#6366f1',
          pointBorderColor: '#fff',
          pointBorderWidth: 2,
          pointRadius: 4,
          pointHoverRadius: 6,
          borderWidth: 2,
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        onClick: (_: any, elements: any[]) => {
          if (elements.length > 0) {
            const idx = elements[0].index;
            const week = child.weeklyPerformances[idx];
            this.selectedWeekDetail.set(week);
            this.selectedWeek.set(week);
          }
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            rtl: true,
            backgroundColor: '#1e1b4b',
            padding: 12,
            cornerRadius: 8,
            callbacks: {
              title: (ctx: any) => ctx[0]?.label || '',
              label: (ctx: any) => {
                const i = ctx.dataIndex;
                const perf = child.weeklyPerformances[i];
                return [
                  `المجموع: ${perf.totalScore}/${perf.totalMaxScore}`,
                  `النسبة: ${ctx.parsed.y}%`
                ];
              }
            }
          }
        },
        scales: {
          y: {
            beginAtZero: true,
            max: 100,
            grid: { color: 'rgba(0,0,0,0.05)' },
            ticks: { callback: (v: any) => v + '%' }
          },
          x: { grid: { display: false } }
        }
      },
      plugins: [{
        id: 'pctLabels',
        afterDraw(chart: any) {
          const ctx2 = chart.ctx;
          chart.data.datasets.forEach((dataset: any, i: number) => {
            const meta = chart.getDatasetMeta(i);
            meta.data.forEach((bar: any, index: number) => {
              const val = dataset.data[index];
              ctx2.fillStyle = '#6366f1';
              ctx2.font = 'bold 11px sans-serif';
              ctx2.textAlign = 'center';
              ctx2.fillText(val + '%', bar.x, bar.y - 10);
            });
          });
        }
      }]
    });
  }

  childStats(name: string) {
    return this.dashboardData()?.children.find(c => c.name === name);
  }

  switchTerm(termValue: number | null): void {
    this.selectedTerm.set(termValue);
    this.selectedWeek.set(null);
    this.loadData();
  }

  closeWeekDetail() {
    this.selectedWeekDetail.set(null);
  }

  onWeekChange(event: Event): void {
    const val = (event.target as HTMLSelectElement).value;
    if (val === 'latest') {
      this.selectedWeek.set(null);
    } else {
      const weekNum = parseInt(val, 10);
      const child = this.selectedChild();
      const foundWeek = child?.weeklyPerformances?.find(w => w.weekNumber === weekNum) || null;
      this.selectedWeek.set(foundWeek);
    }
  }

  exportingPdf = signal(false);

  async exportPdf() {
    if (this.exportingPdf()) return;
    this.exportingPdf.set(true);
    try {
      const dashData = this.dashboardData();
      if (!dashData) return;

      const data: PdfDashboardData = {
        recentActivities: dashData.recentActivities || [],
        children: dashData.children.map(child => ({
          name: child.name,
          grade: child.grade,
          class: child.class,
          performance: child.performance,
          grades: child.grades,
          absences: child.absences,
          attendanceRate: child.attendanceRate,
          excusedAbsences: child.excusedAbsences,
          unexcusedAbsences: child.unexcusedAbsences,
          subjectPerformances: child.subjectPerformances.map(s => ({
            subjectName: s.subjectName,
            score: s.score,
            maxScore: s.maxScore,
            percentage: s.maxScore > 0 ? (s.score / s.maxScore) * 100 : 0,
          })),
          monthlyExams: child.monthlyExams.map(e => ({
            subjectName: e.subjectName,
            title: e.title,
            score: e.score,
            maxScore: e.maxScore,
          })),
          finalExams: child.finalExams.map(e => ({
            subjectName: e.subjectName,
            title: e.title,
            score: e.score,
            maxScore: e.maxScore,
          })),
          weeklyPerformances: child.weeklyPerformances.map(w => ({
            periodName: w.periodName,
            weekNumber: w.weekNumber,
            avgScore: w.avgScore,
            maxScore: w.maxScore,
          })),
          recommendationSections: child.recommendationSections.map(s => ({
            title: s.title,
            items: s.items,
          })),
          recommendationsText: child.recommendationsText,
          upcomingExams: child.upcomingExams.map(e => ({
            title: e.title,
            subjectName: e.subjectName,
            startTime: e.startTime,
          })),
        })),
      };

      await this.pdfExport.exportDashboard(data, this.displayUserName);
    } finally {
      this.exportingPdf.set(false);
    }
  }
}
