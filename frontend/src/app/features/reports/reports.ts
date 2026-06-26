import { Component, signal, computed, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { AuthService } from '../../core/services/auth.service';
import { buildApiUrl } from '../../core/utils/api-url';
import { ParentDashboardService, ParentDashboardChild } from '../../core/services/parent-dashboard.service';
import { PdfExportService, PdfStudentReportData } from '../../core/services/pdf-export.service';
import { map } from 'rxjs';

// ---- DTO Interfaces matching backend ----

/** Recursively convert PascalCase object keys to camelCase */
function normalizeKeys(obj: any): any {
  if (obj === null || obj === undefined) return obj;
  if (Array.isArray(obj)) return obj.map(normalizeKeys);
  if (typeof obj !== 'object') return obj;
  const result: Record<string, any> = {};
  for (const key of Object.keys(obj)) {
    const camelKey = key.charAt(0).toLowerCase() + key.slice(1);
    result[camelKey] = normalizeKeys(obj[key]);
  }
  return result;
}

interface SubjectGradeDto {
  subjectName: string;
  score: number;
  maxScore: number;
  percentage: number;
}

interface MetricDto {
  label: string;
  value: number;
  max: number;
  trend: string;
}

interface PeriodComparisonDto {
  periodId: number;
  periodName: string;
  overallScore: number;
  overallMax: number;
  finalGradeAverage: number;
  finalGradeMax: number;
  subjectGrades: SubjectGradeDto[];
  metrics: MetricDto[];
}

interface StudentReportDto {
  studentId: number;
  studentName: string;
  periodId?: number;
  periodName?: string;
  term?: number;
  overallScore: number;
  overallMax: number;
  overallTrend: string;
  overallChange: number;
  finalGradeAverage: number;
  finalGradeMax: number;
  subjectGrades: SubjectGradeDto[];
  metrics: MetricDto[];
  reportText: string | null;
  recommendationsText: string | null;
  previousMonth?: PeriodComparisonDto;
}

interface RecommendationSection {
  title: string;
  items: string[];
}

interface RecommendationsDto {
  studentId: number;
  recommendationsText: string;
  recommendationItems: string[];
  sections: RecommendationSection[];
}

interface AIReportResult {
  id: number;
  studentId: number;
  periodId?: number;
  classId?: number;
  term?: number;
  reportType: string;
  content: string;
  summary?: string;
  createdAt: string;
}

// ---- Component ----

@Component({
  selector: 'app-reports',
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './reports.html',
  styleUrl: './reports.css',
})
export class Reports implements OnInit {
  private authService = inject(AuthService);
  private http = inject(HttpClient);
  private parentDashboardService = inject(ParentDashboardService);
  private pdfExport = inject(PdfExportService);

  sidebarOpen = signal(false);
  loading = signal(false);
  generatingReport = signal(false);
  generatingRecs = signal(false);
  error = signal<string | null>(null);
  Math = Math; // expose Math for template
  compareToggle = signal(false);

  userName = computed(() => this.authService.user()?.fullName ?? 'ولي الأمر');

  // Children
  children = signal<ParentDashboardChild[]>([]);
  selectedChildId = signal<number | null>(null);
  selectedChildName = signal('');

  // Structured report data (from structured endpoints)
  reportData = signal<StudentReportDto | null>(null);
  recsData = signal<RecommendationsDto | null>(null);

  // History
  reportHistory = signal<AIReportResult[]>([]);

  // Convenience computed for template
  subjectGrades = computed(() => this.reportData()?.subjectGrades ?? []);
  metrics = computed(() => this.reportData()?.metrics ?? []);
  overallScore = computed(() => this.reportData()?.overallScore ?? 0);
  overallMax = computed(() => this.reportData()?.overallMax ?? 100);
  overallTrend = computed(() => (this.reportData()?.overallTrend ?? 'stable') as 'up' | 'down' | 'stable');
  overallChange = computed(() => this.reportData()?.overallChange ?? 0);
  reportText = computed(() => this.reportData()?.reportText ?? null);
  recommendationsText = computed(() => this.recsData()?.recommendationsText ?? null);
  recommendationItems = computed(() => this.recsData()?.recommendationItems ?? []);
  recSections = computed(() => this.recsData()?.sections ?? []);
  finalGradeAverage = computed(() => this.reportData()?.finalGradeAverage ?? 0);
  finalGradeMax = computed(() => this.reportData()?.finalGradeMax ?? 100);
  previousMonth = computed(() => this.reportData()?.previousMonth);
  hasPreviousMonth = computed(() => !!this.previousMonth());

  // Computed arrays for template @for loops (Angular doesn't support for (let i = 0; ...))
  subjectComparisonItems = computed(() => {
    const prev = this.previousMonth()?.subjectGrades ?? [];
    const curr = this.subjectGrades();
    const allSubjects = [...new Set([...prev.map(s => s.subjectName), ...curr.map(s => s.subjectName)])];
    return allSubjects.map((name, i) => ({
      index: i,
      subjectName: name,
      prevGrade: prev.find(s => s.subjectName === name) ?? null,
      currGrade: curr.find(s => s.subjectName === name) ?? null,
    }));
  });

  metricsComparisonItems = computed(() => {
    const prev = this.previousMonth()?.metrics ?? [];
    const curr = this.metrics();
    const allLabels = [...new Set([...prev.map(m => m.label), ...curr.map(m => m.label)])];
    return allLabels.map((label, i) => ({
      index: i,
      label,
      prevMetric: prev.find(m => m.label === label) ?? null,
      currMetric: curr.find(m => m.label === label) ?? null,
    }));
  });

  // Circle info popup
  circleInfo = signal<{ title: string; score: number; max: number; lines: string[] } | null>(null);

  /** Computed breakdown for the evaluations circle */
  evalBreakdown = computed(() => {
    const grades = this.subjectGrades();
    const totalScore = grades.reduce((s, g) => s + g.score, 0);
    const totalMax = grades.reduce((s, g) => s + g.maxScore, 0);
    const n = grades.length;
    return { totalScore: Math.round(totalScore * 10) / 10, totalMax: Math.round(totalMax * 10) / 10, subjectCount: n };
  });

  private aiBase = buildApiUrl('ai/reports');

  ngOnInit() {
    this.loadChildren();
  }

  loadChildren() {
    this.loading.set(true);
    this.parentDashboardService.getMyChildren().pipe(
      map((res: any) => res?.data ?? res ?? [])
    ).subscribe({
      next: (data: ParentDashboardChild[]) => {
        const items = Array.isArray(data) ? data : [];
        this.children.set(items);
        if (items.length > 0) {
          this.selectedChildId.set(items[0].studentId);
          this.selectedChildName.set(items[0].studentName);
          this.loadReports(items[0].studentId);
        }
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  selectChild(studentId: number, name: string) {
    this.selectedChildId.set(studentId);
    this.selectedChildName.set(name);
    this.loadReports(studentId);
  }

  loadReports(studentId: number) {
    this.loading.set(true);
    this.error.set(null);
    this.reportData.set(null);
    this.recsData.set(null);

    // Load history
    this.http.get<any>(`${this.aiBase}/student/${studentId}/history`).pipe(
      map((res: any) => res?.data ?? [])
    ).subscribe({
      next: (data: AIReportResult[]) => {
        const reports = Array.isArray(data) ? data : [];
        // Filter to only Student and Recommendations types (not Class)
        this.reportHistory.set(reports.filter(r => r.reportType === 'Student' || r.reportType === 'Recommendations'));
        this.loading.set(false);

        // Auto-load latest structured report if summary exists AND has valid fields
        const studentReports = reports.filter(r => r.reportType === 'Student');
        if (studentReports.length > 0 && studentReports[0].summary) {
          let parsed: any = null;
          try {
            parsed = normalizeKeys(JSON.parse(studentReports[0].summary));
          } catch { /* summary is plain text (old format) */ }
          if (parsed && typeof parsed.overallScore === 'number') {
            // Fallback: if summary doesn't have reportText, use raw content
            if (!parsed.reportText && studentReports[0].content) {
              parsed.reportText = studentReports[0].content;
            }
            this.reportData.set(parsed as StudentReportDto);
          } else {
            // Old format — use content/summary as plain report text
            const fallback: StudentReportDto = {
              studentId: studentReports[0].studentId,
              studentName: '',
              overallScore: 0,
              overallMax: 100,
              overallTrend: 'stable',
              overallChange: 0,
              finalGradeAverage: 0,
              finalGradeMax: 100,
              subjectGrades: [],
              metrics: [],
              reportText: studentReports[0].content || studentReports[0].summary || '',
              recommendationsText: null,
            };
            this.reportData.set(fallback);
          }
        }

        // Auto-load latest recommendations from stored content (NO AI call)
        const recReports = reports.filter(r => r.reportType === 'Recommendations');
        if (recReports.length > 0 && !this.recsData()) {
          this.parseRecommendationsFromContent(recReports[0]);
        }
      },
      error: () => this.loading.set(false)
    });
  }

  /** Parse recommendations content text into structured sections (same logic as backend) */
  private parseRecommendationsFromContent(report: AIReportResult) {
    const content = report.content || '';
    const sections: RecommendationSection[] = [];
    let currentSection: RecommendationSection | null = null;

    for (const rawLine of content.split('\n')) {
      const line = rawLine.trim();
      if (!line.length) continue;

      // Section header: **Title**
      if (line.startsWith('**') && line.endsWith('**')) {
        currentSection = { title: line.replace(/\*/g, '').trim(), items: [] };
        sections.push(currentSection);
        continue;
      }
      // Section header with text after **
      if (line.startsWith('**') && line.includes('**', 2)) {
        const endIdx = line.lastIndexOf('**');
        const title = line.substring(2, endIdx).trim();
        if (title.length > 0) {
          currentSection = { title, items: [] };
          sections.push(currentSection);
          continue;
        }
      }
      // Bullet item
      if (line.startsWith('-') || line.startsWith('•') || line.startsWith('*')) {
        const item = line.replace(/^[-•*\s]+/, '').trim();
        if (item.length > 0) {
          if (currentSection) {
            currentSection.items.push(item);
          } else {
            currentSection = { title: 'توصيات عامة', items: [item] };
            sections.push(currentSection);
          }
        }
        continue;
      }
    }

    // Fallback: flat items if no sections
    const recItems = content.split('\n')
      .map(l => l.trim())
      .filter(l => l.startsWith('-') || l.startsWith('•') || l.startsWith('*'))
      .map(l => l.replace(/^[-•*\s]+/, '').trim())
      .filter(l => l.length > 0);

    this.recsData.set({
      studentId: report.studentId,
      recommendationsText: content,
      recommendationItems: recItems.length > 0 ? recItems : [content.trim()],
      sections: sections.length > 0 ? sections : []
    });
  }

  generateReport(studentId: number) {
    this.generatingReport.set(true);
    this.error.set(null);

    this.http.get<any>(`${this.aiBase}/student/${studentId}/period/0/structured`).subscribe({
      next: (res) => {
        // Extract data safely: OperationResult wraps in { data: DTO }
        const dto: StudentReportDto | null = res?.data?.overallScore != null ? res.data
                                      : res?.overallScore != null ? res
                                      : null;
        if (dto) {
          this.reportData.set(dto);
          // Refresh history only (don't clear reportData)
          this.http.get<any>(`${this.aiBase}/student/${studentId}/history`).subscribe({
            next: (hres) => {
              const hist = Array.isArray(hres?.data) ? hres.data
                         : Array.isArray(hres) ? hres
                         : [];
              this.reportHistory.set(hist);
            }
          });
        } else {
          this.error.set('تعذر قراءة بيانات التقرير');
        }
        this.generatingReport.set(false);
      },
      error: () => {
        this.error.set('حدث خطأ أثناء توليد التقرير');
        this.generatingReport.set(false);
      }
    });
  }

  generateRecommendations(studentId: number) {
    this.generatingRecs.set(true);

    this.http.get<any>(`${this.aiBase}/recommendations/${studentId}/structured`).subscribe({
      next: (res) => {
        const dto: RecommendationsDto | null = res?.data?.recommendationItems != null ? res.data
                                             : res?.recommendationItems != null ? res
                                             : null;
        if (dto) this.recsData.set(dto);
        this.generatingRecs.set(false);
      },
      error: () => {
        this.generatingRecs.set(false);
      }
    });
  }

  viewReport(id: number) {
    this.loading.set(true);
    this.http.get<any>(`${this.aiBase}/${id}`).subscribe({
      next: (res) => {
        const data: AIReportResult | null = res?.data?.id != null ? res.data
                                          : res?.id != null ? res
                                          : null;
        if (data) {
          if (data.reportType === 'Student') {
            let parsed: any = null;
            // Try to parse summary as JSON (new format)
            if (data.summary) {
              try {
                parsed = normalizeKeys(JSON.parse(data.summary));
              } catch {
                // Summary is plain text (old seed format), ignore
              }
            }
            if (parsed && typeof parsed.overallScore === 'number') {
              if (!parsed.reportText && data.content) {
                parsed.reportText = data.content;
              }
              this.reportData.set(parsed as StudentReportDto);
              this.recsData.set(null);
            } else {
              // Old format or plain text summary — show content as report text
              const fallbackReport: StudentReportDto = {
                studentId: data.studentId,
                studentName: '',
                overallScore: 0,
                overallMax: 100,
                overallTrend: 'stable',
                overallChange: 0,
                finalGradeAverage: 0,
                finalGradeMax: 100,
                subjectGrades: [],
                metrics: [],
                reportText: data.content || data.summary || '',
                recommendationsText: null,
              };
              this.reportData.set(fallbackReport);
              this.recsData.set(null);
            }
          } else if (data.reportType === 'Recommendations') {
            this.reportData.set(null);
            this.parseRecommendationsFromContent(data);
          } else {
            // Unknown type — show raw content if available
            if (data.content || data.summary) {
              const fallback: StudentReportDto = {
                studentId: data.studentId,
                studentName: '',
                overallScore: 0,
                overallMax: 100,
                overallTrend: 'stable',
                overallChange: 0,
                finalGradeAverage: 0,
                finalGradeMax: 100,
                subjectGrades: [],
                metrics: [],
                reportText: data.content || data.summary || '',
                recommendationsText: null,
              };
              this.reportData.set(fallback);
              this.recsData.set(null);
            } else {
              this.reportData.set(null);
              this.recsData.set(null);
            }
          }
        }
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  deleteReport(id: number, event: Event) {
    event.stopPropagation();
    if (!confirm('هل أنت متأكد من حذف هذا التقرير؟')) return;

    this.http.delete<any>(`${this.aiBase}/${id}`).subscribe({
      next: () => {
        this.reportHistory.set(this.reportHistory().filter(r => r.id !== id));
        // If it was the active report, clear it
        if (!this.reportHistory().some(r => r.reportType === 'Student')) {
          this.reportData.set(null);
        }
        if (!this.reportHistory().some(r => r.reportType === 'Recommendations')) {
          this.recsData.set(null);
        }
      },
      error: () => { /* silent */ }
    });
  }

  /** Convert markdown-style formatting to rich HTML with colors */
  formatReportText(text: string | null): string {
    if (!text) return '';

    // First, extract and convert markdown tables (blocks of | ... | lines)
    let html = text.replace(/^((?:[^\n]*\|[^\n]*\n?)+)/gm, (tableBlock: string) => {
      const lines = tableBlock.split('\n').map(l => l.trim()).filter(l => l.startsWith('|'));
      if (lines.length < 2) return tableBlock; // not a real table

      // Second line is usually the separator (|---|---|)
      const separatorLine = lines[1]?.replace(/[^\-|:]/g, '').includes('---');
      const headerLine = separatorLine ? lines[0] : null;
      const bodyLines = separatorLine ? lines.slice(2) : lines;

      let tbl = '<table class="report-table">';

      // Header row
      if (headerLine) {
        const cells = headerLine.split('|').filter(c => c.trim().length > 0);
        tbl += '<thead><tr>' + cells.map(c => `<th>${c.trim()}</th>`).join('') + '</tr></thead>';
      }

      // Body rows
      if (bodyLines.length > 0) {
        tbl += '<tbody>';
        for (const row of bodyLines) {
          const cells = row.split('|').filter(c => c.trim().length > 0);
          if (cells.length > 0) {
            tbl += '<tr>' + cells.map(c => `<td>${c.trim()}</td>`).join('') + '</tr>';
          }
        }
        tbl += '</tbody>';
      }

      tbl += '</table>';
      return tbl;
    });

    // Then apply all inline formatting
    return html
      // Headers (lines that are bold and act as section titles)
      .replace(/^(\*\*[^*]+\*\*)$/gm, '<h4 class="report-h4">$1</h4>')
      // Bold with inline label pattern: - **label:** rest
      .replace(/^- (\*\*(.+?):\*\*)/gm, '<span class="report-label">$2:</span>')
      // Bold markers → strong with accent color
      .replace(/\*\*(.+?)\*\*/g, '<strong class="report-strong">$1</strong>')
      // Italic → em with color
      .replace(/\*(.+?)\*/g, '<em class="report-em">$1</em>')
      // Scores/percentages: e.g. "83.0 / 100.0", "88.75%", "35.5 / 40.0"
      .replace(/(\d+[.,]?\d*)\s*\/\s*(\d+[.,]?\d*)/g, '<span class="report-score">$1 / $2</span>')
      .replace(/(\d+[.,]?\d*)%/g, '<span class="report-pct">$1%</span>')
      // Horizontal rules
      .replace(/---+/g, '<hr class="report-hr">')
      // Remove #### heading markers (keep the text)
      .replace(/^#{1,4}\s*/gm, '')
      // Bullet items at start of line
      .replace(/^- /gm, '<span class="report-bullet">•</span> ')
      // Line breaks
      .replace(/\n/g, '<br>');
  }

  getSubjectBarColor(pct: number): string {
    if (pct >= 85) return '#16a34a';
    if (pct >= 70) return '#2563eb';
    if (pct >= 50) return '#f59e0b';
    return '#dc2626';
  }

  getGradeColor(score: number, max: number): string {
    const pct = max > 0 ? (score / max) * 100 : 0;
    if (pct >= 85) return '#16a34a';
    if (pct >= 70) return '#2563eb';
    if (pct >= 50) return '#f59e0b';
    return '#dc2626';
  }

  getMetricColor(pct: number): string {
    if (pct >= 80) return '#16a34a';
    if (pct >= 60) return '#2563eb';
    if (pct >= 40) return '#f59e0b';
    return '#dc2626';
  }

  showEvalInfo() {
    const bd = this.evalBreakdown();
    this.circleInfo.set({
      title: 'التقييمات',
      score: this.overallScore(),
      max: this.overallMax(),
      lines: [
        `محسوبة من درجات ${bd.subjectCount} مواد في التقييمات الأسبوعية`,
        `كل مادة من ${Math.round(bd.totalMax / bd.subjectCount)} درجات`,
        `مجموع درجات الطالب: ${bd.totalScore} / ${bd.totalMax}`,
        `النسبة: ${bd.totalScore} ÷ ${bd.totalMax} × 100 = ${this.overallScore().toFixed(1)}%`,
      ],
    });
  }

  showFinalInfo() {
    const bd = this.evalBreakdown();
    const n = bd.subjectCount;
    // Estimate total final grade max = n * 100
    const totalFinalMax = n * 100;
    // Derive total earned from the average
    const totalFinalEarned = this.finalGradeAverage() * n;
    this.circleInfo.set({
      title: 'الدرجات النهائية',
      score: this.finalGradeAverage(),
      max: this.finalGradeMax(),
      lines: [
        `محسوبة من متوسط الدرجات النهائية لـ ${n} مواد`,
        `كل مادة من 100 درجة (امتحان الترم + أعمال السنة)`,
        `مجموع درجات الطالب: ${totalFinalEarned.toFixed(1)} / ${totalFinalMax}`,
        `المتوسط: ${totalFinalEarned.toFixed(1)} ÷ ${n} = ${this.finalGradeAverage().toFixed(1)}%`,
      ],
    });
  }

  closeCircleInfo() {
    this.circleInfo.set(null);
  }

  /** Export the current report as a PDF */
  exportingPdf = signal(false);

  async exportPdf() {
    if (this.exportingPdf()) return;
    this.exportingPdf.set(true);
    try {
      const report = this.reportData();
      if (!report) return;

      const data: PdfStudentReportData = {
        studentName: report.studentName || this.selectedChildName(),
        childName: this.selectedChildName(),
        periodName: report.periodName,
        term: report.term,
        overallScore: report.overallScore,
        overallMax: report.overallMax,
        finalGradeAverage: report.finalGradeAverage,
        finalGradeMax: report.finalGradeMax,
        overallTrend: report.overallTrend,
        overallChange: report.overallChange,
        subjectGrades: report.subjectGrades.map(s => ({
          subjectName: s.subjectName,
          score: s.score,
          maxScore: s.maxScore,
          percentage: s.percentage,
        })),
        metrics: report.metrics.map(m => ({
          label: m.label,
          value: m.value,
          max: m.max,
          trend: m.trend,
        })),
        reportText: report.reportText,
        recommendationsText: this.recommendationsText(),
        recSections: this.recSections(),
        recItems: this.recommendationItems(),
        previousMonth: report.previousMonth
          ? {
              periodName: report.previousMonth.periodName,
              overallScore: report.previousMonth.overallScore,
              overallMax: report.previousMonth.overallMax,
              subjectGrades: report.previousMonth.subjectGrades.map(s => ({
                subjectName: s.subjectName,
                score: s.score,
                maxScore: s.maxScore,
                percentage: s.percentage,
              })),
              metrics: report.previousMonth.metrics.map(m => ({
                label: m.label,
                value: m.value,
                max: m.max,
                trend: m.trend,
              })),
            }
          : null,
      };

      await this.pdfExport.exportStudentReport(data);
    } finally {
      this.exportingPdf.set(false);
    }
  }
}
