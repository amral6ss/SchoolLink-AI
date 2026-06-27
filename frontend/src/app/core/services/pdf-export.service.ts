import { Injectable } from '@angular/core';

// pdfmake-rtl — enhanced fork with full RTL / Arabic support.
import pdfMake from 'pdfmake-rtl/build/pdfmake';
// vfs_fonts registers Cairo + Roboto into the VFS on import.
import pdfFonts from 'pdfmake-rtl/build/vfs_fonts';
import { amiriVfs } from './vfs_amiri';

// Also register Amiri — our custom Arabic font — into the VFS.
(pdfMake as any).addVirtualFileSystem(amiriVfs);

// Register the Amiri font family for use in document definitions.
(pdfMake as any).addFonts({
  Amiri: {
    normal: 'Amiri-Regular.ttf',
    bold: 'Amiri-Bold.ttf',
    italics: 'Amiri-Regular.ttf',
    bolditalics: 'Amiri-Bold.ttf',
  },
});

// ── Document content interfaces ──

export interface PdfSubjectGrade {
  subjectName: string;
  score: number;
  maxScore: number;
  percentage: number;
}

export interface PdfMetric {
  label: string;
  value: number;
  max: number;
  trend: string;
}

export interface PdfPeriodComparison {
  periodName: string;
  overallScore: number;
  overallMax: number;
  subjectGrades: PdfSubjectGrade[];
  metrics: PdfMetric[];
}

export interface PdfStudentReportData {
  studentName: string;
  childName?: string;
  periodName?: string;
  term?: number;
  overallScore: number;
  overallMax: number;
  finalGradeAverage: number;
  finalGradeMax: number;
  overallTrend: string;
  overallChange: number;
  subjectGrades: PdfSubjectGrade[];
  metrics: PdfMetric[];
  reportText?: string | null;
  recommendationsText?: string | null;
  recSections?: { title: string; items: string[] }[];
  recItems?: string[];
  previousMonth?: PdfPeriodComparison | null;
}

export interface AcademicStudentRow {
  name: string;
  enrollmentId: number;
  assessment1: number;
  assessment2: number;
  totalMonthly: number;
  finalTotal: number;
  maxTotal: number;
  percentage: number;
  weeklyScores: { periodId: number; periodName: string; avg: number; max: number; rawScore: number; rawMax: number }[];
}

export interface AcademicMonthlyExamEntry {
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

export interface PdfAcademicMonthGroup {
  monthName: string;
  periodIds: number[];
}

export interface PdfAcademicReportData {
  className: string;
  termLabel: string;
  subjectName: string;
  avgPercent: number;
  avgAssessment1: number;
  avgAssessment2: number;
  avgFinal: number;
  students: AcademicStudentRow[];
  monthlyExams: AcademicMonthlyExamEntry[];
  monthGroups: PdfAcademicMonthGroup[];
  studentCount: number;
}

export interface PdfDashboardChildData {
  name: string;
  grade: string;
  class: string;
  performance: number;
  grades: { last: string; total: string };
  absences: number;
  attendanceRate: number;
  excusedAbsences: number;
  unexcusedAbsences: number;
  subjectPerformances: PdfSubjectGrade[];
  monthlyExams: { subjectName: string; title: string; score: number; maxScore: number }[];
  finalExams: { subjectName: string; title: string; score: number; maxScore: number }[];
  weeklyPerformances: { periodName: string; weekNumber: number; avgScore: number; maxScore: number }[];
  recommendationSections: { title: string; items: string[] }[];
  recommendationsText?: string | null;
  upcomingExams: { title: string; subjectName: string; startTime: string | null }[];
}

export interface PdfDashboardData {
  recentActivities: string[];
  children: PdfDashboardChildData[];
}

@Injectable({ providedIn: 'root' })
export class PdfExportService {

  constructor() {
    // Fonts are pre-initialized at module level — no async loading needed.
    // Amiri Arabic font data is embedded directly in vfs_amiri.ts.
  }

  // ────────────────────────────────────────────────────────────────
  //  1. Student Report (AI Reports Page)
  // ────────────────────────────────────────────────────────────────

  async exportStudentReport(data: PdfStudentReportData): Promise<void> {

    const content: any[] = [];

    // ── Cover / Header ──
    content.push(
      { text: 'التقرير الأكاديمي', style: 'mainHeader', alignment: 'center' as const },
      { text: 'تقارير الأداء المدعومة بالذكاء الاصطناعي', style: 'subHeader', alignment: 'center' as const },
      { text: '', margin: [0, 0, 0, 8] },
    );

    // ── Info Table (Student + Period) ──
    const infoRows: any[][] = [
      [
        { text: 'اسم الطالب', style: 'labelCell' },
        { text: data.studentName || data.childName || '', style: 'valueCell' },
        { text: 'فترة التقييم', style: 'labelCell' },
        { text: data.periodName || '—', style: 'valueCell' },
      ],
    ];
    if (data.term) {
      infoRows.push([
        { text: 'الفصل الدراسي', style: 'labelCell' },
        { text: data.term === 1 ? 'الفصل الدراسي الأول' : 'الفصل الدراسي الثاني', style: 'valueCell' },
        { text: '', style: 'labelCell' },
        { text: '', style: 'valueCell' },
      ]);
    }

    content.push({
      table: {
        headerRows: 0,
        widths: ['*', '*', '*', '*'],
        body: infoRows,
      },
      layout: 'noBorders',
      margin: [0, 0, 0, 12],
    });

    // ── Overall Score Box ──
    const scorePct = data.overallMax > 0
      ? Math.round((data.overallScore / data.overallMax) * 100)
      : 0;
    content.push({
      stack: [
        { text: 'الأداء العام', style: 'sectionHeader', alignment: 'center' as const },
        { text: `${scorePct}%`, style: 'bigScore', alignment: 'center' as const },
      ],
      margin: [0, 0, 0, 14],
    });

    // ── Metrics / Indicators ──
    if (data.metrics && data.metrics.length > 0) {
      content.push(
        { text: 'المؤشرات', style: 'sectionHeader' },
        { text: '', margin: [0, 0, 0, 4] },
      );

      const metricRows: any[][] = [['النسبة', 'القيمة', 'المؤشر']];
      for (const m of data.metrics) {
        const pct = m.max > 0 ? Math.round((m.value / m.max) * 100) : 0;
        metricRows.push([
          { text: `${pct}%`, alignment: 'center' as const },
          { text: `${m.value} / ${m.max}`, alignment: 'center' as const },
          { text: m.label, alignment: 'right' as const },
        ]);
      }

      content.push({
        table: {
          headerRows: 1,
          widths: ['auto', 'auto', '*'],
          body: metricRows,
        },
        layout: this.tableLayout('#00236f'),
        margin: [0, 0, 0, 14],
      });
    }

    // ── Subject Grades ──
    if (data.subjectGrades && data.subjectGrades.length > 0) {
      content.push(
        { text: 'المواد الدراسية', style: 'sectionHeader' },
        { text: '', margin: [0, 0, 0, 4] },
      );

      const subjRows: any[][] = [['التقييم', 'النسبة', 'الدرجة', 'المادة']];
      for (const s of data.subjectGrades) {
        const pct = s.maxScore > 0 ? (s.score / s.maxScore) * 100 : 0;
        subjRows.push([
          { text: this.getGradeLabel(pct), alignment: 'center' as const },
          { text: `${pct.toFixed(1)}%`, alignment: 'center' as const },
          { text: `${Math.round(s.score * 10) / 10} / ${Math.round(s.maxScore * 10) / 10}`, alignment: 'center' as const },
          { text: s.subjectName, alignment: 'right' as const },
        ]);
      }

      content.push({
        table: {
          headerRows: 1,
          widths: ['auto', 'auto', 'auto', '*'],
          body: subjRows,
        },
        layout: this.tableLayout('#00236f'),
        margin: [0, 0, 0, 14],
      });
    }

    // ── Previous Month Comparison ──
    if (data.previousMonth) {
      const prev = data.previousMonth;
      const prevPct = prev.overallMax > 0
        ? Math.round((prev.overallScore / prev.overallMax) * 100)
        : 0;
      const currPct = scorePct;
      const diff = currPct - prevPct;

      content.push(
        { text: `مقارنة مع ${prev.periodName}`, style: 'sectionHeader' },
        { text: '', margin: [0, 0, 0, 4] },
      );

      // Overall comparison bar
      content.push({
        columns: [
          {
            width: '45%',
            stack: [
              { text: prev.periodName, style: 'labelSmall' },
              { text: `${prevPct}%`, style: 'scoreValue', alignment: 'center' as const },
            ],
            alignment: 'center' as const,
          },
          {
            width: '10%',
            stack: [
              { text: 'VS', style: 'vsBadge', alignment: 'center' as const },
              {
                text: `${diff > 0 ? '+' : ''}${diff.toFixed(1)}%`,
                color: diff >= 0 ? '#16a34a' : '#dc2626',
                bold: true,
                alignment: 'center' as const,
                fontSize: 12,
              },
            ],
            alignment: 'center' as const,
          },
          {
            width: '45%',
            stack: [
              { text: 'الحالي', style: 'labelSmall' },
              { text: `${currPct}%`, style: 'scoreValue', alignment: 'center' as const },
            ],
            alignment: 'center' as const,
          },
        ],
        margin: [0, 0, 0, 10],
      });

      // Comparison table
      const allSubjects = [
        ...new Set([
          ...(prev.subjectGrades || []).map((s: PdfSubjectGrade) => s.subjectName),
          ...(data.subjectGrades || []).map((s: PdfSubjectGrade) => s.subjectName),
        ]),
      ];

      if (allSubjects.length > 0) {
        const compRows: any[][] = [
          [
            { text: 'الفرق', alignment: 'center' as const },
            { text: 'الحالي', alignment: 'center' as const },
            { text: prev.periodName, alignment: 'center' as const },
            { text: 'المادة', alignment: 'right' as const },
          ],
        ];

        for (const subjName of allSubjects) {
          const prevS = (prev.subjectGrades || []).find((s: PdfSubjectGrade) => s.subjectName === subjName);
          const currS = (data.subjectGrades || []).find((s: PdfSubjectGrade) => s.subjectName === subjName);

          const prevPctVal = prevS && prevS.maxScore > 0
            ? (prevS.score / prevS.maxScore) * 100 : 0;
          const currPctVal = currS && currS.maxScore > 0
            ? (currS.score / currS.maxScore) * 100 : 0;
          const d = currPctVal - prevPctVal;

          compRows.push([
            {
              text: d === 0 ? '—' : `${d > 0 ? '+' : ''}${d.toFixed(1)}%`,
              color: d > 0 ? '#16a34a' : d < 0 ? '#dc2626' : '#666',
              bold: true,
              alignment: 'center' as const,
            },
            {
              text: currS
                ? `${Math.round(currS.score * 10) / 10} / ${Math.round(currS.maxScore * 10) / 10}`
                : '—',
              alignment: 'center' as const,
            },
            {
              text: prevS
                ? `${Math.round(prevS.score * 10) / 10} / ${Math.round(prevS.maxScore * 10) / 10}`
                : '—',
              alignment: 'center' as const,
            },
            { text: subjName, alignment: 'right' as const },
          ]);
        }

        content.push({
          table: {
            headerRows: 1,
            widths: ['auto', 'auto', 'auto', '*'],
            body: compRows,
          },
          layout: this.tableLayout('#00236f', '#f9f9f9'),
          margin: [0, 0, 0, 14],
        });
      }
    }

    // ── AI Report Text ──
    if (data.reportText) {
      content.push(
        { text: 'التقرير الأكاديمي', style: 'sectionHeader' },
        { text: '', margin: [0, 0, 0, 4] },
        {
          text: this.stripMarkdown(data.reportText),
          style: 'bodyText',
          margin: [0, 0, 0, 14],
        },
      );
    }

    // ── Recommendations ──
    const hasSections = data.recSections && data.recSections.length > 0;
    const hasItems = data.recItems && data.recItems.length > 0;
    const hasRecText = data.recommendationsText;

    if (hasSections || hasItems || hasRecText) {
      content.push(
        { text: 'التوصيات الذكية', style: 'sectionHeader' },
        { text: '', margin: [0, 0, 0, 4] },
      );

      if (hasSections) {
        for (const section of data.recSections!) {
          content.push(
            { text: section.title, style: 'subsectionHeader' },
            {
              ul: section.items.map((item: string) => ({
                text: item,
                style: 'listItem',
                alignment: 'right' as const,
              })),
              margin: [0, 0, 8, 8],
            },
          );
        }
      } else if (hasItems) {
        content.push({
          ul: data.recItems!.map((item: string) => ({
            text: item,
            style: 'listItem',
            alignment: 'right' as const,
          })),
          margin: [0, 0, 8, 8],
        });
      } else if (hasRecText) {
        content.push({
          text: data.recommendationsText!,
          style: 'bodyText',
          margin: [0, 0, 0, 8],
        });
      }
    }

    // ── Footer / Date ──
    content.push(
      { text: '', margin: [0, 10, 0, 0] },
      {
        text: `تم إنشاء هذا التقرير في ${this.formatArabicDate(new Date())}`,
        style: 'footerNote',
        alignment: 'center' as const,
      },
    );

    const docDef: any = {
      pageSize: 'A4',
      pageMargins: [40, 40, 40, 40],
      rtl: true,
      defaultStyle: { font: 'Amiri', fontSize: 12 },
      info: {
        title: `التقرير الأكاديمي - ${data.studentName || data.childName || ''}`,
        author: 'SchoolLink AI',
        subject: 'Academic Report',
      },
      footer: (currentPage: number, pageCount: number) => ({
        text: `${currentPage} / ${pageCount}`,
        alignment: 'center' as const,
        fontSize: 10,
        color: '#aaa',
        margin: [0, 10, 0, 0],
      }),
      content,
      styles: {
        mainHeader: { fontSize: 22, bold: true, color: '#00236f', margin: [0, 0, 0, 4] },
        subHeader: { fontSize: 14, color: '#666', margin: [0, 0, 0, 8] },
        labelCell: { fontSize: 12, bold: true, color: '#00236f', alignment: 'right' as const, margin: [0, 2, 8, 2] },
        valueCell: { fontSize: 12, alignment: 'right' as const, margin: [0, 2, 0, 2] },
        sectionHeader: { fontSize: 16, bold: true, color: '#00236f', margin: [0, 8, 0, 2], decoration: 'underline' as const, decorationColor: '#00236f' },
        subsectionHeader: { fontSize: 13, bold: true, margin: [0, 6, 0, 2] },
        bodyText: { fontSize: 12, lineHeight: 1.6, alignment: 'right' as const },
        listItem: { fontSize: 12, margin: [0, 1, 0, 1] },
        bigScore: { fontSize: 32, bold: true, color: '#16a34a' },
        labelSmall: { fontSize: 11, color: '#666', alignment: 'center' as const, margin: [0, 0, 0, -4] },
        scoreValue: { fontSize: 18, bold: true, color: '#00236f', margin: [0, -6, 0, 0] },
        vsBadge: { fontSize: 12, bold: true, color: '#999', margin: [0, 4, 0, 4] },
        footerNote: { fontSize: 11, color: '#999', italics: true },
      },
    };

    pdfMake.createPdf(docDef).download(
      `التقرير_الأكاديمي_${data.studentName || data.childName || ''}.pdf`
    );
  }

  // ────────────────────────────────────────────────────────────────
  //  2. Academic Report (Reports-Academic Page)
  // ────────────────────────────────────────────────────────────────

  async exportAcademicReport(data: PdfAcademicReportData): Promise<void> {

    const content: any[] = [];

    // ── Header ──
    content.push(
      { text: 'التقرير الدراسي – سجل الرصد', style: 'mainHeader', alignment: 'center' as const },
      { text: 'بيانات مبنية على درجات سجل الرصد الحقيقية للفصل المختار', style: 'subHeader', alignment: 'center' as const },
      { text: '', margin: [0, 0, 0, 8] },
    );

    // ── Info Table ──
    content.push({
      table: {
        headerRows: 0,
        widths: ['*', '*', '*', '*'],
        body: [
          [
            { text: 'الفصل', style: 'labelCell' },
            { text: data.className, style: 'valueCell' },
            { text: 'الترم', style: 'labelCell' },
            { text: data.termLabel, style: 'valueCell' },
          ],
          [
            { text: 'المادة', style: 'labelCell' },
            { text: data.subjectName, style: 'valueCell' },
            { text: 'عدد الطلاب', style: 'labelCell' },
            { text: String(data.studentCount), style: 'valueCell' },
          ],
        ],
      },
      layout: 'noBorders',
      margin: [0, 0, 0, 12],
    });

    // ── KPI Cards ──
    content.push({ text: 'ملخص الأداء', style: 'sectionHeader' });
    content.push({ text: '', margin: [0, 0, 0, 4] });

    content.push({
      columns: [
        {
          width: '25%',
          stack: [
            { text: 'متوسط النهائي', style: 'kpiLabel' },
            { text: String(data.avgFinal), style: 'kpiValue2' },
          ],
          alignment: 'center' as const,
        },
        {
          width: '25%',
          stack: [
            { text: 'متوسط اختبار 2', style: 'kpiLabel' },
            { text: String(data.avgAssessment2), style: 'kpiValue2' },
          ],
          alignment: 'center' as const,
        },
        {
          width: '25%',
          stack: [
            { text: 'متوسط اختبار 1', style: 'kpiLabel' },
            { text: String(data.avgAssessment1), style: 'kpiValue2' },
          ],
          alignment: 'center' as const,
        },
        {
          width: '25%',
          stack: [
            { text: 'المعدل العام', style: 'kpiLabel' },
            { text: `${data.avgPercent}%`, style: 'kpiValue' },
          ],
          alignment: 'center' as const,
        },
      ],
      columnGap: 10,
      margin: [0, 0, 0, 14],
    });

    // ── Monthly Performance Summary ──
    if (data.monthGroups && data.monthGroups.length > 0) {
      content.push({ text: 'متوسط الأداء الشهري', style: 'sectionHeader' });
      content.push({ text: '', margin: [0, 0, 0, 4] });

      const monthRows: any[][] = [['الشهر', 'المتوسط']];
      for (const mg of data.monthGroups) {
        let totalPct = 0;
        let validCount = 0;
        for (const row of data.students) {
          const scores = row.weeklyScores.filter(ws => mg.periodIds.includes(ws.periodId));
          if (scores.length > 0) {
            const avg = scores.reduce((s, ws) => s + (ws.rawMax > 0 ? (ws.rawScore / ws.rawMax) * 100 : 0), 0) / scores.length;
            totalPct += avg;
            validCount++;
          }
        }
        const avgPct = validCount > 0 ? Math.round(totalPct / validCount) : 0;
        monthRows.push([
          { text: mg.monthName, alignment: 'right' as const },
          { text: `${avgPct}%`, alignment: 'center' as const },
        ]);
      }

      content.push({
        table: {
          headerRows: 1,
          widths: ['*', 'auto'],
          body: monthRows,
        },
        layout: this.tableLayout('#00236f'),
        margin: [0, 0, 0, 14],
      });
    }

    // ── Student Grade Table ──
    if (data.students && data.students.length > 0) {
      content.push({ text: 'سجل الرصد التفصيلي', style: 'sectionHeader' });
      content.push(
        { text: `${data.className} – ${data.termLabel} – ${data.studentCount} طالب`, style: 'tableMeta' },
        { text: '', margin: [0, 0, 0, 4] },
      );

      const monthCols = data.monthGroups.map(mg => mg.monthName);
      // For RTL visual order: columns are rendered LTR in code but
      // with supportRTL:false the first code column = leftmost visually.
      // Arabic readers start from the right, so the LAST code column (م)
      // appears rightmost (first thing read). Reverse the entire logical order.
      const logicalOrder = [
        { text: 'م', alignment: 'center' as const, bold: true, color: '#fff' },
        { text: 'الطالب', alignment: 'right' as const, bold: true, color: '#fff' },
      ];
      for (const mn of monthCols) {
        logicalOrder.push({ text: mn, alignment: 'center' as const, bold: true, color: '#fff' });
      }
      logicalOrder.push(
        { text: 'اختبار 1', alignment: 'center' as const, bold: true, color: '#fff' },
        { text: 'اختبار 2', alignment: 'center' as const, bold: true, color: '#fff' },
        { text: 'أعمال السنة', alignment: 'center' as const, bold: true, color: '#fff' },
        { text: 'المجموع', alignment: 'center' as const, bold: true, color: '#fff' },
        { text: 'النسبة', alignment: 'center' as const, bold: true, color: '#fff' },
        { text: 'التقدير', alignment: 'center' as const, bold: true, color: '#fff' },
      );
      // Reverse the whole array so code[0] = التقدير (leftmost visually, last read by Arabic reader)
      // and code[N-1] = م (rightmost visually, first read by Arabic reader ✓).
      const tableHeaderRow = [...logicalOrder].reverse();

      const logicalWidths = ['auto', '*', ...monthCols.map(() => 'auto'), 'auto', 'auto', 'auto', 'auto', 'auto', 'auto'];
      const widths = [...logicalWidths].reverse();

      const studentBody: any[][] = [tableHeaderRow];
      for (let i = 0; i < data.students.length; i++) {
        const row = data.students[i];
        const gradeBadge = this.getGradeLabel(row.percentage);
        const logicalRow: any[] = [
          { text: String(i + 1), alignment: 'center' as const },
          { text: row.name, alignment: 'right' as const, bold: true },
        ];

        for (const mn of monthCols) {
          const group = data.monthGroups.find(g => g.monthName === mn);
          if (group && group.periodIds.length > 0) {
            const scores = row.weeklyScores.filter(ws => group.periodIds.includes(ws.periodId));
            if (scores.length > 0) {
              const rawAvg = scores.reduce((s, ws) => s + ws.rawScore, 0) / scores.length;
              logicalRow.push({ text: Math.round(rawAvg * 10) / 10, alignment: 'center' as const });
            } else {
              logicalRow.push({ text: '—', alignment: 'center' as const });
            }
          } else {
            logicalRow.push({ text: '—', alignment: 'center' as const });
          }
        }

        logicalRow.push(
          { text: row.assessment1, alignment: 'center' as const },
          { text: row.assessment2, alignment: 'center' as const },
          { text: row.totalMonthly, alignment: 'center' as const },
          { text: `${row.finalTotal}${row.maxTotal > 0 ? ' / ' + row.maxTotal : ''}`, alignment: 'center' as const },
          { text: `${row.percentage}%`, alignment: 'center' as const },
          { text: gradeBadge, alignment: 'center' as const },
        );

        studentBody.push(logicalRow.reverse());
      }

      content.push({
        table: {
          headerRows: 1,
          widths,
          body: studentBody,
        },
        layout: this.tableLayout('#00236f'),
        margin: [0, 0, 0, 14],
      });
    }

    // ── Monthly Exam Details ──
    if (data.monthlyExams && data.monthlyExams.length > 0) {
      content.push(
        { text: 'تفاصيل الامتحانات الشهرية', style: 'sectionHeader' },
        { text: '', margin: [0, 0, 0, 4] },
      );

      const examRows: any[][] = [
        [
          { text: 'المجموع', alignment: 'center' as const, bold: true, color: '#fff' },
          { text: 'نهاية الترم', alignment: 'center' as const, bold: true, color: '#fff' },
          { text: 'الشهري الثاني', alignment: 'center' as const, bold: true, color: '#fff' },
          { text: 'الشهري الأول', alignment: 'center' as const, bold: true, color: '#fff' },
          { text: 'الطالب', alignment: 'right' as const, bold: true, color: '#fff' },
        ],
      ];

      for (const entry of data.monthlyExams) {
        const total = entry.exam1Score + entry.exam2Score + entry.semesterScore;
        const totalMax = entry.exam1Max + entry.exam2Max + entry.semesterMax;
        examRows.push([
          {
            text: totalMax > 0 ? `${total} / ${totalMax} (${Math.round((total / totalMax) * 100)}%)` : `${total}`,
            alignment: 'center' as const,
          },
          { text: `${entry.semesterScore}${entry.semesterMax > 0 ? ' / ' + entry.semesterMax : ''}`, alignment: 'center' as const },
          { text: `${entry.exam2Score}${entry.exam2Max > 0 ? ' / ' + entry.exam2Max : ''}`, alignment: 'center' as const },
          { text: `${entry.exam1Score}${entry.exam1Max > 0 ? ' / ' + entry.exam1Max : ''}`, alignment: 'center' as const },
          { text: entry.studentName, alignment: 'right' as const },
        ]);
      }

      content.push({
        table: {
          headerRows: 1,
          widths: ['auto', 'auto', 'auto', 'auto', '*'],
          body: examRows,
        },
        layout: this.tableLayout('#00236f', '#f9f9f9'),
        margin: [0, 0, 0, 14],
      });
    }

    // ── Top Students ──
    const sorted = [...data.students].sort((a, b) => b.percentage - a.percentage);
    const top5 = sorted.slice(0, 5);

    content.push(
      { text: 'أعلى الطلاب أداءً', style: 'sectionHeader' },
      { text: '', margin: [0, 0, 0, 4] },
    );

    const topRows: any[][] = [['النسبة', 'الاسم', 'الترتيب']];
    for (let i = 0; i < top5.length; i++) {
      topRows.push([
        { text: `${top5[i].percentage}%`, alignment: 'center' as const },
        { text: top5[i].name, alignment: 'right' as const },
        { text: String(i + 1), alignment: 'center' as const, bold: true },
      ]);
    }

    content.push({
      table: {
        headerRows: 1,
        widths: ['auto', '*', 'auto'],
        body: topRows,
      },
      layout: this.tableLayout('#f59e0b'),
      margin: [0, 0, 0, 14],
    });

    // ── Footer ──
    content.push(
      { text: '', margin: [0, 10, 0, 0] },
      {
        text: `تم إنشاء هذا التقرير في ${this.formatArabicDate(new Date())}`,
        style: 'footerNote',
        alignment: 'center' as const,
      },
    );

    const docDef: any = {
      pageSize: 'A4',
      pageOrientation: 'landscape' as const,
      pageMargins: [30, 30, 30, 30],
      rtl: true,
      defaultStyle: { font: 'Amiri', fontSize: 12 },
      info: {
        title: `التقرير الدراسي - ${data.className}`,
        author: 'SchoolLink',
        subject: 'Academic Report',
      },
      footer: (currentPage: number, pageCount: number) => ({
        text: `${currentPage} / ${pageCount}`,
        alignment: 'center' as const,
        fontSize: 10,
        color: '#aaa',
        margin: [0, 10, 0, 0],
      }),
      content,
      styles: {
        mainHeader: { fontSize: 22, bold: true, color: '#00236f', margin: [0, 0, 0, 4] },
        subHeader: { fontSize: 14, color: '#666', margin: [0, 0, 0, 8] },
        labelCell: { fontSize: 12, bold: true, color: '#00236f', alignment: 'right' as const, margin: [0, 2, 8, 2] },
        valueCell: { fontSize: 12, alignment: 'right' as const, margin: [0, 2, 0, 2] },
        sectionHeader: { fontSize: 15, bold: true, color: '#00236f', margin: [0, 8, 0, 2] },
        tableMeta: { fontSize: 10, color: '#666', italics: true },
        kpiLabel: { fontSize: 10, color: '#666', alignment: 'center' as const },
        kpiValue: { fontSize: 24, bold: true, color: '#00236f', alignment: 'center' as const, margin: [0, 2, 0, 0] },
        kpiValue2: { fontSize: 18, bold: true, color: '#16a34a', alignment: 'center' as const, margin: [0, 2, 0, 0] },
        footerNote: { fontSize: 10, color: '#999', italics: true },
      },
    };

    pdfMake.createPdf(docDef).download(`التقرير_الدراسي_${data.className}.pdf`);
  }

  // ────────────────────────────────────────────────────────────────
  //  3. Parent Dashboard Page
  // ────────────────────────────────────────────────────────────────

  async exportDashboard(data: PdfDashboardData, parentName?: string): Promise<void> {

    const content: any[] = [];

    // ── Header ──
    content.push(
      { text: 'لوحة ولي الأمر', style: 'mainHeader', alignment: 'center' as const },
      { text: parentName ? `ولي الأمر: ${parentName}` : '', style: 'subHeader', alignment: 'center' as const },
      { text: '', margin: [0, 0, 0, 8] },
    );

    // ── Summary Stats ──
    content.push({
      columns: [
        {
          width: '*',
          stack: [
            { text: 'الأداء العام', style: 'kpiLabel' },
            {
              text: data.children.length > 0
                ? `${Math.round(data.children.reduce((s, c) => s + c.performance, 0) / data.children.length)}%`
                : '—',
              style: 'statNumber3',
            },
          ],
          alignment: 'center' as const,
        },
        {
          width: '*',
          stack: [
            { text: 'النشطون', style: 'kpiLabel' },
            { text: String(data.children.filter(c => c.performance >= 50).length), style: 'statNumber2' },
          ],
          alignment: 'center' as const,
        },
        {
          width: '*',
          stack: [
            { text: 'الإجمالي', style: 'kpiLabel' },
            { text: String(data.children.length), style: 'statNumber' },
          ],
          alignment: 'center' as const,
        },
      ],
      columnGap: 10,
      margin: [0, 0, 0, 16],
    });

    // ── Per-Child Sections ──
    for (let ci = 0; ci < data.children.length; ci++) {
      const child = data.children[ci];

      // Child Header
      content.push({
        stack: [
          { text: child.name, style: 'childHeader', margin: [0, 0, 0, 4] },
          { text: `${child.grade}${child.class ? ' - ' + child.class : ''}`, style: 'childSub', margin: [0, 0, 0, 4] },
        ],
        margin: [0, 0, 0, 10],
      });

      // Quick Stats
      content.push({
        columns: [
          { width: '25%', stack: [{ text: 'الأداء العام', style: 'statLabel' }, { text: child.grades.total, style: 'statValue' }], alignment: 'center' as const },
          { width: '25%', stack: [{ text: 'آخر تقييم', style: 'statLabel' }, { text: child.grades.last, style: 'statValue' }], alignment: 'center' as const },
          { width: '25%', stack: [{ text: 'نسبة الحضور', style: 'statLabel' }, { text: `${child.attendanceRate}%`, style: 'statValue' }], alignment: 'center' as const },
          { width: '25%', stack: [{ text: 'الغيابات', style: 'statLabel' }, { text: `${child.absences} (بعذر ${child.excusedAbsences} - بدون ${child.unexcusedAbsences})`, style: 'statValueSmall' }], alignment: 'center' as const },
        ],
        margin: [0, 0, 0, 12],
      });

      // Subject Performances
      if (child.subjectPerformances.length > 0) {
        content.push(
          { text: 'أداء المواد الدراسية', style: 'sectionHeader' },
          { text: '', margin: [0, 0, 0, 4] },
        );

        const subjRows: any[][] = [
          [{ text: 'التقييم', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'النسبة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'الدرجة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'المادة', alignment: 'right' as const, bold: true, color: '#fff' }],
        ];

        for (const subj of child.subjectPerformances) {
          const pct = subj.maxScore > 0 ? (subj.score / subj.maxScore) * 100 : 0;
          subjRows.push([
            { text: this.getGradeLabel(pct), alignment: 'center' as const },
            { text: `${pct.toFixed(1)}%`, alignment: 'center' as const },
            { text: `${subj.score} / ${subj.maxScore}`, alignment: 'center' as const },
            { text: subj.subjectName, alignment: 'right' as const },
          ]);
        }

        content.push({
          table: { headerRows: 1, widths: ['auto', 'auto', 'auto', '*'], body: subjRows },
          layout: this.tableLayout('#6366f1'),
          margin: [0, 0, 0, 10],
        });
      }

      // Monthly Exams
      if (child.monthlyExams.length > 0) {
        content.push(
          { text: 'نتائج الامتحانات الشهرية', style: 'sectionHeader' },
          { text: '', margin: [0, 0, 0, 4] },
        );

        const examRows: any[][] = [
          [{ text: 'التقييم', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'النسبة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'الدرجة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'الامتحان', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'المادة', alignment: 'right' as const, bold: true, color: '#fff' }],
        ];

        for (const exam of child.monthlyExams) {
          const pct = exam.maxScore > 0 ? (exam.score / exam.maxScore) * 100 : 0;
          examRows.push([
            { text: this.getGradeLabel(pct), alignment: 'center' as const },
            { text: `${pct.toFixed(1)}%`, alignment: 'center' as const },
            { text: `${exam.score} / ${exam.maxScore}`, alignment: 'center' as const },
            { text: exam.title, alignment: 'center' as const },
            { text: exam.subjectName, alignment: 'right' as const },
          ]);
        }

        content.push({
          table: { headerRows: 1, widths: ['auto', 'auto', 'auto', 'auto', '*'], body: examRows },
          layout: this.tableLayout('#0ea5e9'),
          margin: [0, 0, 0, 10],
        });
      }

      // Final Exams
      if (child.finalExams.length > 0) {
        content.push(
          { text: 'نتائج امتحانات نهاية الترم', style: 'sectionHeader' },
          { text: '', margin: [0, 0, 0, 4] },
        );

        const finalRows: any[][] = [
          [{ text: 'التقييم', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'النسبة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'الدرجة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'الامتحان', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'المادة', alignment: 'right' as const, bold: true, color: '#fff' }],
        ];

        for (const exam of child.finalExams) {
          const pct = exam.maxScore > 0 ? (exam.score / exam.maxScore) * 100 : 0;
          finalRows.push([
            { text: this.getGradeLabel(pct), alignment: 'center' as const },
            { text: `${pct.toFixed(1)}%`, alignment: 'center' as const },
            { text: `${exam.score} / ${exam.maxScore}`, alignment: 'center' as const },
            { text: exam.title, alignment: 'center' as const },
            { text: exam.subjectName, alignment: 'right' as const },
          ]);
        }

        content.push({
          table: { headerRows: 1, widths: ['auto', 'auto', 'auto', 'auto', '*'], body: finalRows },
          layout: this.tableLayout('#8b5cf6'),
          margin: [0, 0, 0, 10],
        });
      }

      // Upcoming Exams
      if (child.upcomingExams.length > 0) {
        content.push(
          { text: 'الامتحانات القادمة', style: 'sectionHeader' },
          { text: '', margin: [0, 0, 0, 4] },
        );

        const upcRows: any[][] = [
          [{ text: 'التاريخ', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'المادة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'الامتحان', alignment: 'right' as const, bold: true, color: '#fff' }],
        ];

        for (const exam of child.upcomingExams) {
          const dateStr = exam.startTime
            ? this.formatShortArabicDate(new Date(exam.startTime))
            : '—';
          upcRows.push([
            { text: dateStr, alignment: 'center' as const },
            { text: exam.subjectName, alignment: 'center' as const },
            { text: exam.title, alignment: 'right' as const },
          ]);
        }

        content.push({
          table: { headerRows: 1, widths: ['auto', 'auto', '*'], body: upcRows },
          layout: this.tableLayout('#d97706'),
          margin: [0, 0, 0, 10],
        });
      }

      // Weekly Performance Summary
      if (child.weeklyPerformances.length > 0) {
        content.push(
          { text: 'تطور الأداء الأسبوعي', style: 'sectionHeader' },
          { text: '', margin: [0, 0, 0, 4] },
        );

        const weekRows: any[][] = [
          [{ text: 'النسبة', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'المجموع', alignment: 'center' as const, bold: true, color: '#fff' },
           { text: 'الأسبوع', alignment: 'right' as const, bold: true, color: '#fff' }],
        ];

        for (const wp of child.weeklyPerformances) {
          const pct = wp.maxScore > 0 ? Math.round((wp.avgScore / wp.maxScore) * 100) : 0;
          weekRows.push([
            { text: `${pct}%`, alignment: 'center' as const },
            { text: `${Math.round(wp.avgScore * 10) / 10} / ${wp.maxScore}`, alignment: 'center' as const },
            { text: wp.periodName, alignment: 'right' as const },
          ]);
        }

        content.push({
          table: { headerRows: 1, widths: ['auto', 'auto', '*'], body: weekRows },
          layout: this.tableLayout('#6366f1'),
          margin: [0, 0, 0, 10],
        });
      }

      // Recommendations
      const hasRecSections = child.recommendationSections && child.recommendationSections.length > 0;
      const hasRecText = child.recommendationsText;
      if (hasRecSections || hasRecText) {
        content.push(
          { text: 'توصيات ذكية', style: 'sectionHeader' },
          { text: '', margin: [0, 0, 0, 4] },
        );

        if (hasRecSections) {
          for (const section of child.recommendationSections) {
            content.push(
              { text: section.title, style: 'subsectionHeader' },
              {
                ul: section.items.map((item: string) => ({
                  text: item,
                  style: 'listItem',
                  alignment: 'right' as const,
                })),
                margin: [0, 0, 8, 6],
              },
            );
          }
        } else if (hasRecText) {
          content.push({ text: child.recommendationsText!, style: 'bodyText', margin: [0, 0, 0, 6] });
        }
      }

      // Separator between children
      if (ci < data.children.length - 1) {
        content.push({
          canvas: [{ type: 'line', x1: 0, y1: 0, x2: 515, y2: 0, lineWidth: 1, lineColor: '#ddd' }],
          margin: [0, 10, 0, 10],
        });
      }
    }

    // ── Recent Activities ──
    if (data.recentActivities && data.recentActivities.length > 0) {
      content.push(
        { text: 'أحدث الأنشطة', style: 'sectionHeader' },
        { text: '', margin: [0, 0, 0, 4] },
        {
          ul: data.recentActivities.map((activity: string) => ({
            text: activity,
            style: 'listItem',
            alignment: 'right' as const,
          })),
          margin: [0, 0, 8, 10],
        },
      );
    }

    // ── Footer ──
    content.push(
      { text: '', margin: [0, 10, 0, 0] },
      {
        text: `تم إنشاء هذا التقرير في ${this.formatArabicDate(new Date())}`,
        style: 'footerNote',
        alignment: 'center' as const,
      },
    );

    const docDef: any = {
      pageSize: 'A4',
      pageMargins: [35, 35, 35, 35],
      rtl: true,
      defaultStyle: { font: 'Amiri', fontSize: 13 },
      info: {
        title: 'لوحة ولي الأمر',
        author: 'SchoolLink',
        subject: 'Parent Dashboard Report',
      },
      footer: (currentPage: number, pageCount: number) => ({
        text: `${currentPage} / ${pageCount}`,
        alignment: 'center' as const,
        fontSize: 10,
        color: '#aaa',
        margin: [0, 10, 0, 0],
      }),
      content,
      styles: {
        mainHeader: { fontSize: 24, bold: true, color: '#00236f', margin: [0, 0, 0, 4] },
        subHeader: { fontSize: 14, color: '#666', margin: [0, 0, 0, 8] },
        sectionHeader: { fontSize: 15, bold: true, color: '#00236f', margin: [0, 8, 0, 2] },
        subsectionHeader: { fontSize: 13, bold: true, margin: [0, 4, 0, 2] },
        bodyText: { fontSize: 12, lineHeight: 1.5, alignment: 'right' as const },
        listItem: { fontSize: 12, margin: [0, 1, 0, 1] },
        childHeader: { fontSize: 17, bold: true, color: '#6366f1' },
        childSub: { fontSize: 12, color: '#666' },
        kpiLabel: { fontSize: 10, color: '#666', alignment: 'center' as const, margin: [0, 0, 0, 0] },
        statNumber: { fontSize: 24, bold: true, color: '#00236f', alignment: 'center' as const, margin: [0, -2, 0, 4] },
        statNumber2: { fontSize: 24, bold: true, color: '#16a34a', alignment: 'center' as const, margin: [0, -2, 0, 4] },
        statNumber3: { fontSize: 24, bold: true, color: '#6366f1', alignment: 'center' as const, margin: [0, -2, 0, 4] },
        statLabel: { fontSize: 10, color: '#666', alignment: 'center' as const, margin: [0, 0, 0, 2] },
        statValue: { fontSize: 14, bold: true, alignment: 'center' as const },
        statValueSmall: { fontSize: 10, alignment: 'center' as const },
        footerNote: { fontSize: 10, color: '#999', italics: true },
      },
    };

    pdfMake.createPdf(docDef).download(`لوحة_ولي_الأمر_${parentName || ''}.pdf`);
  }

  // ── Helpers ──

  /**
   * Formats a date in Arabic with Western digits (0‑9) to avoid BIDI
   * reversal of Eastern Arabic numerals in pdfmake‑rtl RTL mode.
   *
   * Produces e.g. "27 يونيو 2026" instead of "٢٧ يونيو ٢٠٢٦".
   */
  private formatArabicDate(date: Date): string {
    const months = [
      'يناير', 'فبراير', 'مارس', 'إبريل', 'مايو', 'يونيو',
      'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر',
    ];
    return `${date.getDate()} ${months[date.getMonth()]} ${date.getFullYear()}`;
  }

  /** Short form, e.g. "27 يونيو" (day + short month name). */
  private formatShortArabicDate(date: Date): string {
    const shortMonths = [
      'يناير', 'فبراير', 'مارس', 'إبريل', 'مايو', 'يونيو',
      'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر',
    ];
    return `${date.getDate()} ${shortMonths[date.getMonth()]}`;
  }

  private tableLayout(headerColor: string, altRowColor = '#f5f5f5') {
    return {
      hLineWidth: () => 0.5,
      vLineWidth: () => 0.5,
      hLineColor: () => '#ccc',
      vLineColor: () => '#ccc',
      paddingLeft: () => 6,
      paddingRight: () => 6,
      paddingTop: () => 3,
      paddingBottom: () => 3,
      fillColor: (rowIdx: number) => {
        if (rowIdx === 0) return headerColor;
        if (rowIdx % 2 === 0) return altRowColor;
        return null;
      },
    };
  }

  private stripMarkdown(text: string): string {
    return text
      .replace(/\*\*(.+?)\*\*/g, '$1')
      .replace(/\*(.+?)\*/g, '$1')
      .replace(/^[-•*]\s*/gm, '')
      .replace(/^#+\s*/gm, '')
      .replace(/---+/g, '')
      .replace(/\|/g, '')
      .replace(/\n{3,}/g, '\n\n')
      .trim();
  }

  private getGradeLabel(pct: number): string {
    if (pct >= 90) return 'ممتاز';
    if (pct >= 75) return 'جيد جداً';
    if (pct >= 60) return 'جيد';
    if (pct >= 50) return 'مقبول';
    return 'ضعيف';
  }
}
