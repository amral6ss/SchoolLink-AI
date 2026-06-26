import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { CertificateService, Certificate, CertificateSubject } from '../../core/services/certificate.service';
import { SubjectService } from '../../core/services/subject.service';
import { ClassService } from '../../core/services/class.service';
import { GradeLevelService, GradeLevel } from '../../core/services/grade-level.service';

@Component({
  selector: 'app-certificate-management',
  standalone: true,
  imports: [CommonModule, FormsModule, Sidebar],
  templateUrl: './certificate-management.html',
  styleUrl: './certificate-management.css',
})
export class CertificateManagement implements OnInit {
  sidebarOpen = signal(false);
  displayUserName = localStorage.getItem('fullName') || localStorage.getItem('username') || 'المشرف';

  private certService = inject(CertificateService);
  private subjectService = inject(SubjectService);
  private classService = inject(ClassService);
  private gradeLevelService = inject(GradeLevelService);

  certificates = signal<Certificate[]>([]);
  allClasses = signal<any[]>([]);
  allGradeLevels = signal<GradeLevel[]>([]);
  allSubjects = signal<any[]>([]);
  showSubjectPicker = signal(false);
  pickerTargetRow: number | null = null;
  pickerSearch = '';
  pickerFilteredSubjects = computed(() => {
    const all = this.allSubjects();
    const q = this.pickerSearch.toLowerCase().trim();
    if (!q) return all;
    return all.filter((s: any) => s.name?.toLowerCase().includes(q));
  });
  editingCertId = signal<number | null>(null);
  errorMessage = signal('');
  successMessage = signal('');
  deleteCertConfirmId = signal<number | null>(null);

  // ── Form ──
  newCert: Partial<Certificate> & { subjects: CertificateSubject[] } = {
    name: '', gradeLevel: '', term: '', examRole: '', year: '', subjects: [],
  };

  get totalMax(): number {
    return this.newCert.subjects.filter(s => s.isCountedInTotal).reduce((sum, s) => sum + s.maxScore, 0);
  }
  get totalMin(): number {
    return this.newCert.subjects.filter(s => s.isCountedInTotal).reduce((sum, s) => sum + s.minScore, 0);
  }
  get countedSubjects(): number {
    return this.newCert.subjects.filter(s => s.isCountedInTotal).length;
  }

  // ── Pagination ──
  currentPage = signal(1);
  itemsPerPage = signal(10);
  paginatedCerts = computed(() => {
    const start = (this.currentPage() - 1) * this.itemsPerPage();
    return this.certificates().slice(start, start + this.itemsPerPage());
  });
  totalPages = computed(() => Math.max(1, Math.ceil(this.certificates().length / this.itemsPerPage())));
  rangeStart = computed(() => this.certificates().length === 0 ? 0 : (this.currentPage() - 1) * this.itemsPerPage() + 1);
  rangeEnd = computed(() => Math.min(this.currentPage() * this.itemsPerPage(), this.certificates().length));
  pages = computed<(number | string)[]>(() => {
    const total = this.totalPages(), current = this.currentPage(), res: (number | string)[] = [];
    res.push(1); if (current > 3) res.push('...');
    for (let i = Math.max(2, current - 1); i <= Math.min(total - 1, current + 1); i++) res.push(i);
    if (current < total - 2) res.push('...'); if (total > 1) res.push(total); return res;
  });
  trackByPageIndex = (_: number, item: number | string) => typeof item === 'string' ? `dot-${_}` : `page-${item}`;
  nextPage() { if (this.currentPage() < this.totalPages()) this.currentPage.update(p => p + 1); }
  prevPage() { if (this.currentPage() > 1) this.currentPage.update(p => p - 1); }
  goToPage(p: number) { this.currentPage.set(p); }
  searchTerm = '';

  // ── Extract section ──
  extractCertId: number | null = null;
  extractClassId: number | null = null;
  extractGradeLevelId: number | null = null;
  extractTerm: number = 1;
  extractFormat: 'word' = 'word';
  extractAllClasses: boolean = false;
  extracting: boolean = false;
  extractMessage: string = '';

  // Classes filtered by selected grade level
  filteredClasses: any[] = [];

  onGradeLevelChange() {
    if (!this.extractGradeLevelId) {
      this.filteredClasses = [];
      this.extractClassId = null;
      return;
    }
    this.classService.getByGradeLevel(this.extractGradeLevelId).subscribe({
      next: (data) => {
        this.filteredClasses = data.data ?? data;
        if (this.filteredClasses.length === 1) {
          this.extractClassId = this.filteredClasses[0].id;
          this.extractAllClasses = false;
        } else {
          this.extractClassId = null;
        }
      },
      error: () => { this.filteredClasses = []; },
    });
  }

  get selectedClassIds(): string {
    if (this.extractAllClasses && this.filteredClasses.length > 0) {
      return this.filteredClasses.map(c => c.id).join(',');
    }
    return this.extractClassId?.toString() || '';
  }

  hasFormData(): boolean {
    return !!(this.newCert.name || this.newCert.gradeLevel || this.newCert.subjects.length > 0);
  }

  ngOnInit() {
    this.loadCertificates();
    this.loadClasses();
    this.loadGradeLevels();
  }

  loadCertificates() {
    this.certService.getAll().subscribe({
      next: (data) => { this.certificates.set(data.data ?? data); this.currentPage.set(1); },
      error: () => this.showError('فشل في تحميل الشهادات. تأكد من الاتصال بالخادم.'),
    });
  }

  loadClasses() {
    this.classService.getAll().subscribe({
      next: (data) => { this.allClasses.set(data.data ?? data); },
      error: () => {},
    });
  }

  loadGradeLevels() {
    this.gradeLevelService.getAll().subscribe({
      next: (data) => { this.allGradeLevels.set(data.data ?? data); },
      error: () => {},
    });
  }

  onSearch() {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) { this.loadCertificates(); return; }
    this.certService.getAll().subscribe({
      next: (data) => {
        this.certificates.set((data.data ?? data).filter((c: any) =>
          c.name.toLowerCase().includes(term) || c.gradeLevel.toLowerCase().includes(term)));
        this.currentPage.set(1);
      },
      error: () => this.showError('فشل في البحث.'),
    });
  }
  clearSearch() { this.searchTerm = ''; this.currentPage.set(1); this.loadCertificates(); }

  // ── Subject management ──
  addSubject() {
    // Load subjects from API if not loaded yet
    if (this.allSubjects().length === 0) {
      this.subjectService.getAll().subscribe({
        next: (res: any) => {
          const data = res.data ?? res;
          this.allSubjects.set(Array.isArray(data) ? data : []);
          this.showSubjectPicker.set(true);
        },
        error: () => {
          // If API fails, just add empty row
          this.pushEmptySubject();
        }
      });
    } else {
      this.showSubjectPicker.set(true);
    }
  }

  pickSubject(subj: any) {
    if (this.pickerTargetRow != null && this.pickerTargetRow < this.newCert.subjects.length) {
      // Replace subject name in existing row
      const updated = [...this.newCert.subjects];
      updated[this.pickerTargetRow] = { ...updated[this.pickerTargetRow], subjectName: subj.name };
      this.newCert.subjects = updated;
    } else {
      // Add new row
      this.newCert.subjects = [...this.newCert.subjects, {
        subjectName: subj.name,
        maxScore: 100, minScore: 50, isCountedInTotal: true,
        sortOrder: this.newCert.subjects.length + 1,
      }];
    }
    this.showSubjectPicker.set(false);
    this.pickerTargetRow = null;
    this.pickerSearch = '';
  }

  /** Open picker for a specific row (replace that row's subject name) */
  openPickerForRow(idx: number) {
    if (this.allSubjects().length === 0) {
      this.subjectService.getAll().subscribe({
        next: (res: any) => {
          const data = res.data ?? res;
          this.allSubjects.set(Array.isArray(data) ? data : []);
          this.pickerTargetRow = idx;
          this.showSubjectPicker.set(true);
        },
        error: () => {}
      });
    } else {
      this.pickerTargetRow = idx;
      this.showSubjectPicker.set(true);
    }
  }

  private pushEmptySubject() {
    this.newCert.subjects = [...this.newCert.subjects, {
      subjectName: '', maxScore: 100, minScore: 50, isCountedInTotal: true,
      sortOrder: this.newCert.subjects.length + 1,
    }];
  }

  closeSubjectPicker() { this.showSubjectPicker.set(false); this.pickerTargetRow = null; this.pickerSearch = ''; }
  removeSubject(i: number) {
    this.newCert.subjects = this.newCert.subjects.filter((_, idx) => idx !== i).map((s, idx) => ({ ...s, sortOrder: idx + 1 }));
  }
  toggleCounted(i: number) {
    this.newCert.subjects = this.newCert.subjects.map((s, idx) => idx === i ? { ...s, isCountedInTotal: !s.isCountedInTotal } : s);
  }
  moveSubjectUp(i: number) {
    if (i === 0) return;
    const arr = [...this.newCert.subjects];
    [arr[i - 1], arr[i]] = [arr[i], arr[i - 1]];
    this.newCert.subjects = arr.map((s, idx) => ({ ...s, sortOrder: idx + 1 }));
  }
  moveSubjectDown(i: number) {
    if (i >= this.newCert.subjects.length - 1) return;
    const arr = [...this.newCert.subjects];
    [arr[i], arr[i + 1]] = [arr[i + 1], arr[i]];
    this.newCert.subjects = arr.map((s, idx) => ({ ...s, sortOrder: idx + 1 }));
  }

  loadPresetHalfYear() {
    this.newCert = {
      name: 'شهادة نصف العام', gradeLevel: 'الصف الأول الإعدادى', term: 'الفصل الدراسى الأول',
      examRole: 'الدور الأول', year: '2025/2026',
      subjects: [
        { subjectName: 'اللغة العربية والخط العربى', maxScore: 100, minScore: 50, isCountedInTotal: true, sortOrder: 1 },
        { subjectName: 'اللغة الإنجليزية', maxScore: 100, minScore: 50, isCountedInTotal: true, sortOrder: 2 },
        { subjectName: 'الدراسات الإجتماعية', maxScore: 100, minScore: 50, isCountedInTotal: true, sortOrder: 3 },
        { subjectName: 'الرياضيات', maxScore: 100, minScore: 50, isCountedInTotal: true, sortOrder: 4 },
        { subjectName: 'العلوم', maxScore: 100, minScore: 50, isCountedInTotal: true, sortOrder: 5 },
        { subjectName: 'نشاط اختيارى 1', maxScore: 100, minScore: 50, isCountedInTotal: false, sortOrder: 6 },
        { subjectName: 'نشاط اختيارى 2', maxScore: 100, minScore: 50, isCountedInTotal: false, sortOrder: 7 },
        { subjectName: 'التربية الفنية', maxScore: 100, minScore: 50, isCountedInTotal: false, sortOrder: 8 },
        { subjectName: 'الكمبيوتر وتكنولوجيا المعلومات', maxScore: 100, minScore: 50, isCountedInTotal: false, sortOrder: 9 },
        { subjectName: 'التربية الدينية', maxScore: 100, minScore: 50, isCountedInTotal: false, sortOrder: 10 },
        { subjectName: 'التربية الرياضية', maxScore: 100, minScore: 50, isCountedInTotal: false, sortOrder: 11 },
        { subjectName: 'التربية الموسيقية', maxScore: 100, minScore: 50, isCountedInTotal: false, sortOrder: 12 },
      ],
    };
    this.editingCertId.set(null);
  }

  // ── CRUD ──
  editCert(cert: Certificate) {
    this.editingCertId.set(cert.id);
    this.newCert = { name: cert.name, gradeLevel: cert.gradeLevel, term: cert.term, examRole: cert.examRole, year: cert.year, subjects: (cert.subjects || []).map(s => ({ ...s })) };
  }
  cancelEdit() {
    this.editingCertId.set(null);
    this.newCert = { name: '', gradeLevel: '', term: '', examRole: '', year: '', subjects: [] };
  }
  saveCert() {
    if (!this.newCert.name?.trim() || !this.newCert.gradeLevel?.trim()) return;
    const payload: any = {
      name: this.newCert.name, gradeLevel: this.newCert.gradeLevel, term: this.newCert.term,
      examRole: this.newCert.examRole, year: this.newCert.year,
      subjects: this.newCert.subjects.map((s, i) => ({
        ...(s.id ? { id: s.id } : {}), subjectName: s.subjectName, maxScore: s.maxScore,
        minScore: s.minScore, isCountedInTotal: s.isCountedInTotal, sortOrder: i + 1,
      })),
    };
    if (this.editingCertId()) {
      this.certService.update(this.editingCertId()!, payload).subscribe({
        next: () => { this.loadCertificates(); this.cancelEdit(); this.showSuccess('تم تحديث الشهادة!'); },
        error: () => this.showError('فشل في تحديث الشهادة.'),
      });
    } else {
      this.certService.create(payload).subscribe({
        next: () => { this.loadCertificates(); this.cancelEdit(); this.showSuccess('تم إضافة الشهادة!'); },
        error: () => this.showError('فشل في إضافة الشهادة.'),
      });
    }
  }
  deleteCert(id: number) { this.deleteCertConfirmId.set(id); }
  cancelDeleteCert() { this.deleteCertConfirmId.set(null); }
  confirmDeleteCert() {
    const id = this.deleteCertConfirmId(); if (!id) return;
    this.deleteCertConfirmId.set(null);
    this.certService.delete(id).subscribe({ next: () => { this.loadCertificates(); this.showSuccess('تم حذف الشهادة!'); }, error: () => this.showError('فشل في الحذف.') });
  }

  // ══════════════════════════════════════════════════════════════
  //  PRINT / EXTRACT
  // ══════════════════════════════════════════════════════════════

  printCertificate(id: number | null) {
    const cert = this.certificates().find(c => c.id === id);
    if (!cert) return;
    this.renderCertificateHtml(cert, null, null, null, null);
  }

  async extractCertificates() {
    const certId = this.extractCertId;
    const classIds = this.selectedClassIds;
    const term = this.extractTerm;
    if (!certId || !classIds) { this.extractMessage = 'اختر الشهادة والفصل أولاً'; return; }

    this.extracting = true;
    this.extractMessage = 'جاري تجهيز بيانات الشهادات...';

    try {
      const res: any = await this.certService.generate(certId, classIds, term).toPromise();
      const data = res?.data ?? res;
      if (!data || !data.students?.length) {
        this.extractMessage = 'لم يتم العثور على بيانات للطلاب';
        this.extracting = false;
        return;
      }

      const cert = data.certificate;
      const clsName = data.className;
      const students = data.students;

      this.downloadWordDoc(cert, clsName, students);
      this.extractMessage = `تم تصدير ${students.length} شهادة إلى Word ✓`;
    } catch (e: any) {
      this.extractMessage = e?.error?.message || 'حدث خطأ أثناء استخراج الشهادات';
    }
    this.extracting = false;
  }

  async printGradeSheet() {
    const certId = this.extractCertId;
    const classIds = this.selectedClassIds;
    const term = this.extractTerm;
    if (!certId || !classIds) { this.showError('اختر الشهادة والفصل أولاً'); return; }

    this.extracting = true;
    try {
      const res: any = await this.certService.gradeSheet(certId, classIds, term).toPromise();
      const data = res?.data ?? res;
      if (!data || !data.students?.length) {
        this.showError('لا توجد بيانات للطلاب');
        this.extracting = false;
        return;
      }
      this.renderGradeSheetHtml(data);
    } catch {
      this.showError('حدث خطأ أثناء تجهيز كشف الدرجات');
    }
    this.extracting = false;
  }

  async printHonorRoll() {
    const certId = this.extractCertId;
    const classIds = this.selectedClassIds;
    const term = this.extractTerm;
    if (!certId || !classIds) { this.showError('اختر الشهادة والفصل أولاً'); return; }

    this.extracting = true;
    try {
      const res: any = await this.certService.honorRoll(certId, classIds, term, 10).toPromise();
      const data = res?.data ?? res;
      if (!data || !data.students?.length) {
        this.showError('لا توجد بيانات');
        this.extracting = false;
        return;
      }
      this.downloadHonorRollDoc(data);
    } catch {
      this.showError('حدث خطأ أثناء تجهيز كشف الأوائل');
    }
    this.extracting = false;
  }

  // ── Word Export (compact, same design as PDF) ──
  private downloadWordDoc(cert: any, clsName: string, students: any[]) {
    {
      const esc = (value: any) => String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
      const subjectTitle = (value: any) => esc(value).split(/\s+/).filter(Boolean).join('<br>');
      const scoreText = (value: any) => value === null || value === undefined ? '' : esc(value);
      const totalScore = (st: any) => {
        const subjects = Array.isArray(st.subjects) ? st.subjects : [];
        const sum = subjects
          .filter((s: any) => s.score !== null && s.score !== undefined && s.score !== '')
          .reduce((acc: number, s: any) => acc + Number(s.score || 0), 0);
        return sum || '';
      };

      const dateStr = new Date().toISOString().slice(0, 10).replace(/-/g, '');
      const safeFile = (value: any) => String(value ?? '').replace(/[\\/:*?"<>|]+/g, '-').trim() || 'certificates';
      const fileName = `${safeFile(cert.name)}_${safeFile(clsName)}_${dateStr}.doc`;

      const certBlocks: string[] = [];
      for (let studentIndex = 0; studentIndex < students.length; studentIndex++) {
        const st = students[studentIndex];
        const subjects = Array.isArray(st.subjects) ? st.subjects : [];
        const subjectCols = subjects.map((s: any) =>
          `<th class="${s.isCountedInTotal ? 'counted' : 'activity'}">${subjectTitle(s.subjectName)}</th>`
        ).join('');
        const maxCols = subjects.map((s: any) => `<td>${scoreText(s.maxScore)}</td>`).join('');
        const minCols = subjects.map((s: any) => `<td>${scoreText(s.minScore)}</td>`).join('');
        const scoreCols = subjects.map((s: any) => {
          const isLow = s.score != null && s.score !== '' && s.minScore != null && Number(s.score) < Number(s.minScore || 0);
          return `<td class="${isLow ? 'low' : ''}">${scoreText(s.score)}</td>`;
        }).join('');
        const grandScore = totalScore(st);
        const totalMax = st.totalMaxWithActivities ?? subjects.reduce((acc: number, s: any) => acc + Number(s.maxScore || 0), 0);
        const totalMin = subjects.reduce((acc: number, s: any) => acc + Number(s.minScore || 0), 0);

        certBlocks.push(`
<tr class="cert-row">
  <td class="cert-slot">
  <table class="cert-frame">
    <tr>
      <td class="cert-inner">
        <table class="top">
          <tr>
            <td class="spacer">&nbsp;</td>
            <td class="title">${esc(cert.name)}</td>
            <td class="grade">${esc(cert.gradeLevel)}</td>
          </tr>
          <tr>
            <td colspan="3" class="exam">امتحان ${esc(cert.examRole)} ${esc(cert.term)} لعام ${esc(cert.year)}</td>
          </tr>
          <tr>
            <td class="student">اسم الطالب : ${esc(st.studentName)}</td>
            <td colspan="2" class="seat">رقم الجلوس : ${esc(st.seatNumber || st.enrollmentId || '')}</td>
          </tr>
        </table>

        <table class="marks">
          <thead>
            <tr>
              <th class="row-head">المادة</th>
              ${subjectCols}
              <th class="total-head">المجموع</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td class="row-head">العظمى</td>
              ${maxCols}
              <td class="total-cell">${scoreText(totalMax)}</td>
            </tr>
            <tr>
              <td class="row-head">الصغرى</td>
              ${minCols}
              <td class="total-cell">${scoreText(totalMin)}</td>
            </tr>
            <tr class="score-row">
              <td class="row-head">تقويمات</td>
              ${scoreCols}
              <td class="total-cell">${scoreText(grandScore)}</td>
            </tr>
          </tbody>
        </table>

        <table class="signatures">
          <tr>
            <td class="committee">لجنة النظام والمراقبة<br><span>&nbsp;</span></td>
            <td class="manager">مدير المدرسة<br><span>&nbsp;</span></td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
  </td>
</tr>`);
      }

      let pagesHtml = '';
      for (let i = 0; i < certBlocks.length; i += 4) {
        const pageClass = i + 4 < certBlocks.length ? ' cert-page-break' : '';
        pagesHtml += `<table class="cert-page${pageClass}">${certBlocks.slice(i, i + 4).join('')}</table>`;
      }

      const fullHtml = `<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head>
<meta charset="UTF-8">
<title>${esc(cert.name)} - ${esc(clsName)}</title>
<style>
  @page WordSection1 { size: 16.4cm 29.7cm; margin: .28cm .55cm .28cm .55cm; }
  body { margin: 0; padding: 0; color: #000; direction: rtl; font-family: "Traditional Arabic", "Arial", "Tahoma", sans-serif; }
  .cert-page { page: WordSection1; width: 15.6cm; height: 29.05cm; margin: 0 auto; border-collapse: collapse; table-layout: fixed; page-break-inside: avoid; }
  .cert-page-break { page-break-after: always; }
  .cert-row { height: 7.2cm; page-break-inside: avoid; }
  .cert-slot { height: 7.2cm; border: 0; padding: 0 0 .12cm 0; vertical-align: top; text-align: center; page-break-inside: avoid; }
  .cert-frame { width: 15.2cm; margin: .03cm auto 0 auto; border-collapse: separate; border-spacing: 0; border: 3px double #000; page-break-inside: avoid; }
  .cert-inner { padding: .1cm .14cm .08cm .14cm; border: 0; vertical-align: top; }
  .top { width: 100%; border-collapse: collapse; table-layout: fixed; }
  .top td { border: 0; padding: 1px 3px; text-align: center; font-size: 8.8pt; line-height: 1.05; font-weight: bold; height: .34cm; }
  .top .spacer { width: 18%; }
  .top .title { width: 38%; background: #d9d9d9; border: 1.5px solid #000; mso-border-alt: solid #000 1pt; font-size: 9.4pt; padding: 1px 8px; }
  .top .grade { width: 44%; text-align: center; }
  .top .exam { font-size: 8.4pt; height: .38cm; }
  .top .student { text-align: right; width: 50%; }
  .top .seat { text-align: center; width: 50%; }
  .marks { width: 100%; margin-top: .05cm; border-collapse: collapse; table-layout: fixed; border: 1px solid #000; }
  .marks th, .marks td { border: 1px solid #000; padding: 1px 1px; text-align: center; vertical-align: middle; font-size: 7.7pt; line-height: 1.02; height: .48cm; }
  .marks th { font-size: 6.6pt; font-weight: bold; }
  .marks th.counted, .marks th.activity { background: #fff; font-weight: bold; }
  .marks tbody td { background: #d9d9d9; font-weight: bold; font-size: 8pt; }
  .marks .score-row td { background: #fff; font-size: 8pt; }
  .marks .score-row td.low { color: #b00020; }
  .marks th.counted, .marks th.activity, .marks tbody td:not(.row-head):not(.total-cell) { width: .78cm; }
  .marks .row-head { width: 1.22cm; font-size: 7.5pt; background: #fff; }
  .marks .total-head, .marks .total-cell { width: .88cm; }
  .signatures { width: 72%; border-collapse: collapse; table-layout: fixed; margin: .46cm auto 0 auto; }
  .signatures td { border: 0; text-align: center; font-size: 8pt; line-height: 1.05; font-weight: bold; padding: 0 .3cm; }
  .signatures .committee { width: 55%; text-align: center; }
  .signatures .manager { width: 45%; text-align: center; }
  .signatures span { display: inline-block; width: 2.45cm; height: .28cm; margin-top: 6px; border-bottom: 1px solid #000; font-size: 1pt; line-height: .28cm; }
</style>
</head>
<body>${pagesHtml}</body>
</html>`;

      const blob = new Blob([fullHtml], { type: 'application/msword;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      a.click();
      URL.revokeObjectURL(url);
      return;
    }
    const dateStr = new Date().toISOString().slice(0, 10).replace(/-/g, '');
    const fileName = `${cert.name}_${clsName}_${dateStr}.doc`;

    let certsHtml = '';
    for (let si = 0; si < students.length; si++) {
      const st = students[si];

      const subsHtml = st.subjects.map((s: any) =>
        `<tr>
          <td style="text-align:right;font-weight:${s.isCountedInTotal ? '700' : '400'};border:1px solid #000;padding:0.4mm 0.6mm">${s.subjectName}</td>
          <td style="text-align:center;border:1px solid #000;padding:0.4mm 0.6mm">${s.maxScore}</td>
          <td style="text-align:center;border:1px solid #000;padding:0.4mm 0.6mm">${s.minScore}</td>
          <td style="text-align:center;border:1px solid #000;padding:0.4mm 0.6mm">${s.score != null ? s.score : ''}</td>
        </tr>`
      ).join('');

      const countedScore = st.subjects.filter((s: any) => s.isCountedInTotal && s.score != null)
        .reduce((sum: number, s: any) => sum + (+s.score), 0);
      const activitiesScore = st.subjects.filter((s: any) => !s.isCountedInTotal && s.score != null)
        .reduce((sum: number, s: any) => sum + (+s.score), 0);
      const grandScore = (countedScore || activitiesScore) ? (countedScore || 0) + (activitiesScore || 0) : null;

      certsHtml += `
    <div style="border:1.5px double #000;padding:2mm 2mm;margin:0 auto 1.5mm auto;page-break-inside:avoid;width:100mm">
  <div style="text-align:center;margin-bottom:1mm">
    <div style="font-size:8pt;font-weight:bold;color:#000">${cert.gradeLevel}</div>
    <div style="font-size:7pt;font-weight:bold;color:#000">${cert.name}</div>
    <div style="font-size:5.5pt;color:#000">امتحان ${cert.examRole} ${cert.term} لعام ${cert.year}</div>
  </div>
  <table style="width:100%;border-collapse:collapse;border-top:1px solid #000;border-bottom:1px solid #000;margin:0.5mm 0;font-size:6pt">
    <tr>
      <td style="border:none;padding:0.5mm 0;text-align:right;width:60%"><b>اسم الطالب :‏</b> ${st.studentName}</td>
      <td style="border:none;padding:0.5mm 0;text-align:left;width:40%"><b>رقم الجلوس :‏</b>‏ ${st.seatNumber || st.enrollmentId || '—'}</td>
    </tr>
  </table>
  <table style="width:100%;border-collapse:collapse;margin:0.5mm 0;font-size:5.5pt;table-layout:fixed">
    <colgroup>
      <col style="width:38%">
      <col style="width:22%">
      <col style="width:22%">
      <col style="width:18%">
    </colgroup>
    <thead>
      <tr>
        <th style="border:1px solid #000;padding:0.5mm 0.8mm;text-align:center;font-weight:bold;color:#000">المادة</th>
        <th style="border:1px solid #000;padding:0.5mm 0.8mm;text-align:center;font-weight:bold;color:#000">العظمى</th>
        <th style="border:1px solid #000;padding:0.5mm 0.8mm;text-align:center;font-weight:bold;color:#000">الصغرى</th>
        <th style="border:1px solid #000;padding:0.5mm 0.8mm;text-align:center;font-weight:bold;color:#000">الدرجة</th>
      </tr>
    </thead>
    <tbody>
      ${subsHtml}
      <tr style="font-weight:bold">
        <td style="border:1px solid #000;padding:0.4mm 0.6mm;text-align:right">المجموع الكلي</td>
        <td style="border:1px solid #000;padding:0.4mm 0.6mm;text-align:center">${st.totalMaxWithActivities}</td>
        <td style="border:1px solid #000;padding:0.4mm 0.6mm;text-align:center;color:#999">—</td>
        <td style="border:1px solid #000;padding:0.4mm 0.6mm;text-align:center">${grandScore != null ? grandScore : ''}</td>
      </tr>
    </tbody>
  </table>
  <table style="width:100%;border-collapse:collapse;margin-top:1mm;font-size:5pt">
    <tr>
      <td style="border:none;text-align:center;width:50%;padding:0"><div>لجنة النظام والمراقبة</div><div style="border-top:1px solid #000;padding-top:0.3mm;margin-top:1.5mm;font-size:5pt">التوقيع</div></td>
      <td style="border:none;text-align:center;width:50%;padding:0"><div>مدير المدرسة</div><div style="border-top:1px solid #000;padding-top:0.3mm;margin-top:1.5mm;font-size:5pt">التوقيع</div></td>
    </tr>
  </table>
</div>`;
    }

    const fullHtml = `<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head><meta charset="UTF-8">
<title>${cert.name} - ${clsName}</title>
<style>body{font-family:'Traditional Arabic','Amiri',Tahoma,Arial;margin:0;padding:4mm;color:#000}</style>
</head>
<body>${certsHtml}</body>
</html>`;

    const blob = new Blob([fullHtml], { type: 'application/msword' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  // ── Render single certificate (full page) ──
  private renderCertificateHtml(cert: any, clsName: string | null, stName: string | null, seatNum: string | null, subjects: any[] | null) {
    const w = window.open('', '_blank', 'width=900,height=700');
    if (!w) { this.showError('الرجاء السماح للنوافذ المنبثقة'); return; }

    const sName = stName || '______________________________';
    const sSeat = seatNum || '________';
    const useSubs = subjects || cert.subjects || [];

    const subjectsHtml = useSubs.map((s: any) => `
      <tr>
        <td style="text-align:right;font-weight:${s.isCountedInTotal ? '700' : '400'};padding:5px 8px;border:1px solid #444">${s.subjectName}</td>
        <td style="text-align:center;padding:5px 8px;border:1px solid #444">${s.maxScore}</td>
        <td style="text-align:center;padding:5px 8px;border:1px solid #444">${s.minScore}</td>
        <td style="text-align:center;padding:5px 8px;border:1px solid #444">${s.score != null ? s.score : ''}</td>
      </tr>`).join('');

    const countedMax = useSubs.filter((s: any) => s.isCountedInTotal).reduce((sum: number, s: any) => sum + (+s.maxScore), 0);
    const countedMin = useSubs.filter((s: any) => s.isCountedInTotal).reduce((sum: number, s: any) => sum + (+s.minScore), 0);
    const countedScore = useSubs.filter((s: any) => s.isCountedInTotal && s.score != null).reduce((sum: number, s: any) => sum + (+s.score), 0);
    const activitiesMax = useSubs.filter((s: any) => !s.isCountedInTotal).reduce((sum: number, s: any) => sum + (+s.maxScore), 0);
    const activitiesScore = useSubs.filter((s: any) => !s.isCountedInTotal && s.score != null).reduce((sum: number, s: any) => sum + (+s.score), 0);
    const grandMax = countedMax + activitiesMax;
    const grandScore = (countedScore || activitiesScore) ? (countedScore || 0) + (activitiesScore || 0) : null;

    // Full-page single certificate
    w.document.write(`<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head><meta charset="UTF-8">
<title>${cert.name}</title>
<style>
  @page { margin: 1.5cm; size: A4 portrait; }
  body { font-family: 'Traditional Arabic', 'Amiri', 'Segoe UI', Tahoma, sans-serif; margin: 0; padding: 20px; color: #000; }
  .cert { max-width: 680px; margin: 0 auto; border: 3px double #000; padding: 25px 30px; }
  .header { text-align: center; margin-bottom: 15px; }
  .header .grade { font-size: 20px; font-weight: bold; color: #000; }
  .header .name { font-size: 18px; font-weight: bold; color: #000; margin-top: 4px; }
  .header .exam { font-size: 13px; color: #000; margin-top: 6px; }
  .student-info { display: flex; justify-content: space-between; padding: 10px 0; margin: 10px 0; border-top: 1px solid #000; border-bottom: 1px solid #000; font-size: 14px; }
  table { width: 100%; border-collapse: collapse; margin: 12px 0; font-size: 12px; }
  th { padding: 6px 8px; border: 1px solid #000; font-size: 11px; text-align: center; font-weight: bold; color: #000; }
  td { padding: 4px 7px; border: 1px solid #000; }
  .total-row td { font-weight: bold; }
</style>
</head>
<body>
<div class="cert">
  <div class="header">
    <div class="grade">${cert.gradeLevel}</div>
    <div class="name">${cert.name}</div>
    <div class="exam">امتحان ${cert.examRole} ${cert.term} لعام ${cert.year}</div>
  </div>
  <table style="width:100%;border-collapse:collapse;margin:10px 0;border-top:1px solid #000;border-bottom:1px solid #000">
    <tr>
      <td style="border:none;padding:10px 0;font-size:14px"><b>اسم الطالب :‏</b> ${sName}</td>
      <td style="border:none;padding:10px 0;font-size:14px;text-align:left"><b>رقم الجلوس :‏</b>‏ ${sSeat}</td>
    </tr>
  </table>
  <table>
    <thead><tr><th>المادة</th><th>النهاية العظمى</th><th>النهاية الصغرى</th><th>درجة الطالب</th></tr></thead>
    <tbody>
      ${subjectsHtml}
      <tr class="total-row">
        <td style="text-align:right">المجموع الكلي (مع الأنشطة)</td>
        <td style="text-align:center">${grandMax}</td>
        <td style="text-align:center">—</td>
        <td style="text-align:center">${grandScore != null ? grandScore : ''}</td>
      </tr>
    </tbody>
  </table>
  <table style="width:100%;border-collapse:collapse;margin-top:30px">
    <tr>
      <td style="border:none;text-align:center;width:50%"><div>لجنة النظام والمراقبة</div><div style="border-top:1px solid #000;padding-top:4px;margin-top:30px">التوقيع</div></td>
      <td style="border:none;text-align:center;width:50%"><div>مدير المدرسة</div><div style="border-top:1px solid #000;padding-top:4px;margin-top:30px">التوقيع</div></td>
    </tr>
  </table>
</div>
</body>
</html>`);
    w.document.close();
  }

  // ── Render batch certificates (3 per page, like Word doc) ──
  private renderCertificatesBatchHtml(cert: any, clsName: string, students: any[]) {
    const w = window.open('', '_blank', 'width=900,height=700');
    if (!w) { this.showError('الرجاء السماح للنوافذ المنبثقة'); return; }

    let allHtml = '';
    for (let si = 0; si < students.length; si++) {
      const st = students[si];

      const subjectRows = st.subjects.map((s: any) => `
        <tr>
          <td style="font-weight:${s.isCountedInTotal ? 'bold' : 'normal'};text-align:right">${s.subjectName}</td>
          <td style="text-align:center">${s.maxScore}</td>
          <td style="text-align:center">${s.minScore}</td>
          <td style="text-align:center">${s.score != null ? s.score : ''}</td>
        </tr>`).join('');

      const countedScore = st.subjects.filter((s: any) => s.isCountedInTotal && s.score != null).reduce((sum: number, s: any) => sum + (+s.score), 0);
      const activitiesScore = st.subjects.filter((s: any) => !s.isCountedInTotal && s.score != null).reduce((sum: number, s: any) => sum + (+s.score), 0);
      const grandScore = (countedScore || activitiesScore) ? (countedScore || 0) + (activitiesScore || 0) : null;

      allHtml += `
<div class="cert">
  <div class="c-header">
    <div class="c-grade">${cert.gradeLevel}</div>
    <div class="c-name">${cert.name}</div>
    <div class="c-exam">امتحان ${cert.examRole} ${cert.term} لعام ${cert.year}</div>
  </div>
  <table class="c-student-table">
    <tr>
      <td class="c-stu-right"><b>اسم الطالب :‏</b> ${st.studentName}</td>
      <td class="c-stu-left"><b>رقم الجلوس :‏</b>‏ ${st.seatNumber || st.enrollmentId || '—'}</td>
    </tr>
  </table>
  <table class="c-table">
    <colgroup>
      <col style="width:50%">
      <col style="width:18%">
      <col style="width:16%">
      <col style="width:16%">
    </colgroup>
    <thead>
      <tr><th>المادة</th><th>العظمى</th><th>الصغرى</th><th>الدرجة</th></tr>
    </thead>
    <tbody>
      ${subjectRows}
      <tr class="c-total">
        <td style="text-align:right">المجموع الكلي</td>
        <td style="text-align:center">${st.totalMaxWithActivities}</td>
        <td style="text-align:center"><span style="color:#999">—</span></td>
        <td style="text-align:center;font-weight:bold">${grandScore != null ? grandScore : ''}</td>
      </tr>
    </tbody>
  </table>
  <table class="c-footer-table">
    <tr>
      <td class="c-sig-td"><div>لجنة النظام والمراقبة</div><div class="c-sig-line">التوقيع</div></td>
      <td class="c-sig-td"><div>مدير المدرسة</div><div class="c-sig-line">التوقيع</div></td>
    </tr>
  </table>
</div>`;
    }

    w.document.write(`<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head><meta charset="UTF-8">
<title>شهادات ${cert.name} - ${clsName}</title>
<style>
  /* ═══ Page: 3 certs per A4 portrait ═══ */
  @page { size: A4 portrait; margin: 3mm 5mm; }
  body {
    font-family: 'Traditional Arabic', 'Amiri', 'Segoe UI', Tahoma, Arial, sans-serif;
    margin: 0; padding: 0; color: #000;
  }

  /* ═══ Print toolbar ═══ */
  .print-bar { text-align: center; margin: 4px 0; }
  .print-bar button {
    padding: 4px 12px; font-size: 11px; font-family: inherit;
    background: #000; color: #fff; border: none; border-radius: 3px;
    cursor: pointer; margin: 0 2px;
  }
  .print-bar button.sec { background: #555; }
  @media print { .print-bar { display: none; } }

  /* ═══ Compact certificate (Word doc style) ═══ */
    .cert {
    border: 1.5px double #000;
    padding: 1.5mm 2.5mm;
    margin: 0 auto 1.5mm auto;
    page-break-inside: avoid;
    max-width: 95mm;
  }

  .c-header { text-align: center; margin-bottom: 1mm; }
  .c-grade { font-size: 8pt; font-weight: bold; color: #000; }
  .c-name  { font-size: 7pt; font-weight: bold; color: #000; }
  .c-exam  { font-size: 5.5pt; color: #000; }

  /* Student info as table (works in PDF + browser) */
  .c-student-table { width: 100%; border-collapse: collapse; margin: 1mm 0; border-top: 0.75px solid #000; border-bottom: 0.75px solid #000; }
  .c-student-table td { padding: 0.8mm 0; border: none; font-size: 6pt; }
  .c-stu-right { text-align: right; }
  .c-stu-left  { text-align: left; }

  /* Main marks table */
  .c-table {
    width: 100%; border-collapse: collapse;
    margin: 0.8mm 0; font-size: 5.5pt; table-layout: fixed;
  }
  .c-table th {
    padding: 0.8mm 1mm;
    border: 0.5px solid #000;
    font-size: 5pt; text-align: center; font-weight: bold; color: #000;
  }
  .c-table td {
    padding: 0.5mm 0.8mm;
    border: 0.5px solid #000;
    font-size: 5.5pt;
  }
  .c-total td {
    font-weight: bold;
  }
  .c-total td:first-child { text-align: right; }
  .c-total td:not(:first-child) { text-align: center; }

  /* Footer as table */
  .c-footer-table { width: 100%; border-collapse: collapse; margin-top: 1mm; }
  .c-footer-table td { border: none; padding: 0; }
  .c-sig-td { text-align: center; width: 50%; }
  .c-sig-td:first-child { padding-left: 5mm; }
  .c-sig-td:last-child  { padding-right: 5mm; }
  .c-sig-line {
    border-top: 0.5px solid #000;
    padding-top: 0.5mm; margin-top: 1.5mm;
    font-size: 4.5pt; color: #555;
  }

  @media print {
    body { padding: 0; margin: 0; }
  }
</style>
</head>
<body>
<div class="print-bar">
  <button onclick="window.print()">🖨️ طباعة الكل</button>
  <button class="sec" onclick="window.close()">إغلاق</button>
</div>
${allHtml}
</body>
</html>`);
    w.document.close();
  }

  // ── Render Grade Sheet (كشف بالدرجات) ──
  private renderGradeSheetHtml(data: any) {
    const w = window.open('', '_blank', 'width=1100,height=750');
    if (!w) { this.showError('الرجاء السماح للنوافذ المنبثقة'); return; }

    let rows = '';
    for (const st of data.students) {
      rows += `<tr>
        <td style="text-align:center;padding:6px 8px;border:1px solid #000">${st.rowNumber}</td>
        <td style="text-align:center;padding:6px 8px;border:1px solid #000">${st.seatNumber || '—'}</td>
        <td style="text-align:right;padding:6px 8px;border:1px solid #000;font-weight:700">${st.studentName}</td>
        <td style="text-align:center;padding:6px 8px;border:1px solid #000">${st.birthDate || '—'}</td>
        <td style="text-align:center;padding:6px 8px;border:1px solid #000;font-weight:700">${st.totalScore}</td>
        <td style="text-align:center;padding:6px 8px;border:1px solid #000">${st.rank}</td>
        <td style="text-align:center;padding:6px 8px;border:1px solid #000">${st.className}</td>
      </tr>`;
    }

    w.document.write(`<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head><meta charset="UTF-8">
<title>كشف بالدرجات</title>
<style>
  @page { margin: 1.5cm; size: A4 landscape; }
  body { font-family: 'Amiri', 'Traditional Arabic', 'Segoe UI', Tahoma, sans-serif; margin: 0; padding: 20px; color: #000; }
  .sheet-header { text-align: center; margin-bottom: 20px; }
  .sheet-header h1 { font-size: 20px; color: #000; margin: 4px 0; }
  .sheet-header h2 { font-size: 16px; color: #000; margin: 4px 0; }
  .sheet-header .sub { font-size: 13px; color: #000; margin: 4px 0; }
  table { width: 100%; border-collapse: collapse; margin: 10px 0; }
  th { padding: 8px 6px; border: 1px solid #000; font-size: 12px; text-align:center; font-weight: bold; color: #000; }
  td { padding: 6px 8px; border: 1px solid #000; font-size: 13px; }
  tr:nth-child(even) td { background: #f5f5f5; }
  .print-btn { text-align: center; margin: 15px 0; }
  .print-btn button { padding: 10px 24px; font-size: 15px; background: #000; color: #fff; border: none; border-radius: 8px; cursor: pointer; margin: 0 5px; }
  .print-btn button:hover { background: #333; }
  .print-btn .sec { background: #555; }
  @media print { body { padding: 0; } .print-btn { display: none; } }
</style>
</head>
<body>
<div class="print-btn">
  <button onclick="window.print()">🖨️ طباعة الكشف</button>
  <button class="sec" onclick="window.close()">إغلاق</button>
</div>
<div class="sheet-header">
  <h1>${data.gradeLevelName || data.certificate?.gradeLevel || ''}</h1>
  <h2>${data.certificate?.name || ''}</h2>
  <div class="sub">امتحان ${data.certificate?.examRole || ''} ${data.certificate?.term || ''} لعام ${data.certificate?.year || ''}</div>
  <div class="sub">${data.academicYearName || ''}</div>
  <div class="sub">إجمالي الطلاب: ${data.students.length} | المجموع الكلي: ${data.students[0]?.maxTotal || ''}</div>
</div>
<table>
  <thead>
    <tr>
      <th style="width:40px">م</th>
      <th style="width:70px">رقم الجلوس</th>
      <th>اسم الطالب</th>
      <th style="width:90px">تاريخ الميلاد</th>
      <th style="width:80px">المجموع</th>
      <th style="width:60px">الترتيب</th>
      <th>الفصل</th>
    </tr>
  </thead>
  <tbody>
    ${rows || '<tr><td colspan="7" style="text-align:center;color:#888">لا توجد بيانات</td></tr>'}
  </tbody>
</table>
</body>
</html>`);
    w.document.close();
  }

  // ── Export Honor Roll as Word ──
  private downloadHonorRollDoc(data: any) {
    {
      const esc = (value: any) => String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
      const dateStr = new Date().toISOString().slice(0, 10).replace(/-/g, '');
      const safeFile = (value: any) => String(value ?? '').replace(/[\\/:*?"<>|]+/g, '-').trim() || 'honor-roll';
      const selectedGradeName = this.allGradeLevels().find(g => g.id === this.extractGradeLevelId)?.name || '';
      const gradeName = data.gradeLevelName || data.gradeLevel || data.certificate?.gradeLevel || selectedGradeName || 'المرحلة';
      const fileName = `اوائل_الطلاب_${safeFile(gradeName)}_${dateStr}.doc`;

      const rows = (data.students || []).map((st: any) => `
        <tr>
          <td class="c">${esc(st.rowNumber)}</td>
          <td class="c">${esc(st.seatNumber || '-')}</td>
          <td class="name">${esc(st.studentName)}</td>
          <td class="c">${esc(st.birthDate || '-')}</td>
          <td class="score">${esc(st.totalScore)}</td>
          <td class="c">${esc(st.rank)}</td>
          <td class="c">${esc(st.className || '')}</td>
        </tr>`).join('');

      const fullHtml = `<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head>
<meta charset="UTF-8">
<title>أوائل الطلاب - ${esc(gradeName)}</title>
<style>
  @page WordSection1 { size: 17cm 29.7cm; margin: .65cm .7cm .65cm .7cm; }
  body { margin: 0; padding: 0; color: #000; direction: rtl; font-family: "Traditional Arabic", "Arial", "Tahoma", sans-serif; }
  .sheet { page: WordSection1; width: 15.4cm; margin: 0 auto; }
  .stage { text-align: center; font-size: 16pt; font-weight: bold; margin: .2cm 0 .05cm; }
  .title { text-align: center; font-size: 14pt; font-weight: bold; margin: .05cm 0; }
  .meta { text-align: center; font-size: 10pt; margin: .04cm 0 .18cm; }
  table { width: 15.4cm; border-collapse: collapse; table-layout: fixed; border: 1px solid #000; }
  th, td { border: 1px solid #000; padding: 4px 5px; text-align: center; vertical-align: middle; font-size: 10pt; line-height: 1.15; }
  th { font-size: 9.5pt; font-weight: bold; background: #f2f2f2; }
  td.name { text-align: right; font-weight: bold; width: 5.4cm; }
  td.score { font-weight: bold; }
  .w-no { width: .75cm; }
  .w-seat { width: 1.35cm; }
  .w-name { width: 5.4cm; }
  .w-birth { width: 1.75cm; }
  .w-score { width: 1.2cm; }
  .w-rank { width: 1cm; }
  .w-class { width: 1.2cm; }
  .sign { width: 70%; margin: .8cm auto 0; border: 0; }
  .sign td { border: 0; font-weight: bold; font-size: 10pt; text-align: center; }
  .sig-line { display: inline-block; width: 2.6cm; height: .35cm; border-bottom: 1px solid #000; }
</style>
</head>
<body>
  <div class="sheet">
    <div class="stage">${esc(gradeName)}</div>
    <div class="title">كشف بالطلاب العشرة الأوائل في مجموع ${esc(data.term || '')} بدون النشاط</div>
    <div class="meta">${esc(data.academicYearName || '')} - لعام ${esc(data.year || '')} - المجموع الكلي: ${esc(data.maxTotal || '')}</div>
    <table>
      <thead>
        <tr>
          <th class="w-no">م</th>
          <th class="w-seat">رقم الجلوس</th>
          <th class="w-name">اسم الطالب</th>
          <th class="w-birth">تاريخ الميلاد</th>
          <th class="w-score">المجموع</th>
          <th class="w-rank">الترتيب</th>
          <th class="w-class">الفصل</th>
        </tr>
      </thead>
      <tbody>
        ${rows || '<tr><td colspan="7">لا توجد بيانات</td></tr>'}
      </tbody>
    </table>
    <table class="sign">
      <tr>
        <td>لجنة النظام والمراقبة<br><span class="sig-line">&nbsp;</span></td>
        <td>مدير المدرسة<br><span class="sig-line">&nbsp;</span></td>
      </tr>
    </table>
  </div>
</body>
</html>`;

      const blob = new Blob([fullHtml], { type: 'application/msword;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      a.click();
      URL.revokeObjectURL(url);
      return;
    }
    const dateStr = new Date().toISOString().slice(0, 10).replace(/-/g, '');
    const fileName = `اوائل_الطلاب_${data.gradeLevelName || ''}_${dateStr}.doc`;

    let rows = '';
    for (const st of data.students) {
      rows += `<tr>
        <td style="padding:6px 8px;border:1px solid #000;text-align:center">${st.rowNumber}</td>
        <td style="padding:6px 8px;border:1px solid #000;text-align:center">${st.seatNumber || '—'}</td>
        <td style="padding:6px 8px;border:1px solid #000;text-align:right;font-weight:700">${st.studentName}</td>
        <td style="padding:6px 8px;border:1px solid #000;text-align:center">${st.birthDate || '—'}</td>
        <td style="padding:6px 8px;border:1px solid #000;text-align:center;font-weight:700">${st.totalScore}</td>
        <td style="padding:6px 8px;border:1px solid #000;text-align:center">${st.rank}</td>
        <td style="padding:6px 8px;border:1px solid #000;text-align:center">${st.className}</td>
      </tr>`;
    }

    const fullHtml = `<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head><meta charset="UTF-8">
<title>أوائل الطلاب</title>
<style>
  body{font-family:'Traditional Arabic','Amiri',Tahoma,Arial;margin:20px;color:#000}
  h1{text-align:center;font-size:20px;margin:4px 0}
  .sub{text-align:center;font-size:14px;margin:4px 0;color:#555}
  table{width:100%;border-collapse:collapse;margin:15px 0}
  th{padding:8px 6px;border:1px solid #000;font-size:12px;text-align:center;font-weight:bold}
  td{padding:6px 8px;border:1px solid #000;font-size:13px}
  tr:nth-child(even){background:#f5f5f5}
</style>
</head>
<body>
  <h1>${data.gradeLevelName || ''}</h1>
  <div style="text-align:center;font-size:16px;font-weight:700;margin:10px 0">كشف بالطلاب العشرة الأوائل فى مجموع ${data.term || ''} بدون النشاط</div>
  <div style="text-align:center;font-size:13px;color:#555;margin:4px 0">${data.academicYearName || ''}</div>
  <div style="text-align:center;font-size:13px;color:#555;margin:4px 0">لعام ${data.year || ''} — المجموع الكلي: ${data.maxTotal || ''}</div>
  <table style="width:100%;border-collapse:collapse;border:1px solid #000;margin:10px 0">
    <tr>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:35px">م</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:65px">رقم الجلوس</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold">اسم الطالب</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:85px">تاريخ الميلاد</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:70px">المجموع</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:55px">الترتيب</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:60px">الفصل</th>
    </tr>
    ${rows}
  </table>
  <table style="width:100%;border-collapse:collapse;margin-top:30px">
    <tr>
      <td style="border:none;text-align:center;width:50%"><div style="font-size:14px">لجنة النظام والمراقبة</div><div style="border-top:1px solid #000;padding-top:4px;margin-top:30px;font-size:13px">التوقيع</div></td>
      <td style="border:none;text-align:center;width:50%"><div style="font-size:14px">مدير المدرسة</div><div style="border-top:1px solid #000;padding-top:4px;margin-top:30px;font-size:13px">التوقيع</div></td>
    </tr>
  </table>
</body>
</html>`;

    const blob = new Blob([fullHtml], { type: 'application/msword' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  // ── Render Honor Roll (كشف بأوائل الطلاب) ──
  private renderHonorRollHtml(data: any) {
    const w = window.open('', '_blank', 'width=1100,height=750');
    if (!w) { this.showError('الرجاء السماح للنوافذ المنبثقة'); return; }

    let rows = '';
    for (const st of data.students) {
      rows += `<tr>
        <td style="text-align:center;padding:5px 7px;border:1px solid #000">${st.rowNumber}</td>
        <td style="text-align:center;padding:5px 7px;border:1px solid #000">${st.seatNumber || '—'}</td>
        <td style="text-align:right;padding:5px 7px;border:1px solid #000;font-weight:700">${st.studentName}</td>
        <td style="text-align:center;padding:5px 7px;border:1px solid #000">${st.birthDate || '—'}</td>
        <td style="text-align:center;padding:5px 7px;border:1px solid #000;font-weight:700">${st.totalScore}</td>
        <td style="text-align:center;padding:5px 7px;border:1px solid #000">${st.rank}</td>
        <td style="text-align:center;padding:5px 7px;border:1px solid #000">${st.className}</td>
      </tr>`;
    }

    w.document.write(`<!DOCTYPE html>
<html dir="rtl" lang="ar">
<head><meta charset="UTF-8">
<title>كشف بأوائل الطلاب</title>
<style>
  @page { margin: 1.5cm; size: A4 landscape; }
  body { font-family: 'Traditional Arabic', 'Amiri', 'Segoe UI', Tahoma, sans-serif; margin: 0; padding: 20px; color: #000; }
  .print-btn { text-align: center; margin: 15px 0; }
  .print-btn button { padding: 10px 24px; font-size: 15px; background: #000; color: #fff; border: none; border-radius: 8px; cursor: pointer; margin: 0 5px; }
  .print-btn button:hover { background: #333; }
  .print-btn .sec { background: #555; }
  @media print { body { padding: 0; } .print-btn { display: none; } }
</style>
</head>
<body>
<div class="print-btn">
  <button onclick="window.print()">🖨️ طباعة</button>
  <button class="sec" onclick="window.close()">إغلاق</button>
</div>
<div style="text-align:center;margin-bottom:20px">
  <div style="font-size:20px;font-weight:800;margin:4px 0">${data.gradeLevelName || ''}</div>
  <div style="font-size:16px;font-weight:700;margin:10px 0">كشف بالطلاب العشرة الأوائل فى مجموع ${data.term || ''} بدون النشاط</div>
  <div style="font-size:13px;color:#555;margin:4px 0">${data.academicYearName || ''}</div>
  <div style="font-size:13px;color:#555;margin:4px 0">لعام ${data.year || ''} — المجموع الكلي: ${data.maxTotal || ''}</div>
</div>
<table style="width:100%;border-collapse:collapse;border:1px solid #000;margin:10px 0">
  <thead>
    <tr>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:35px">م</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:65px">رقم الجلوس</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold">اسم الطالب</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:85px">تاريخ الميلاد</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:70px">المجموع</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:55px">الترتيب</th>
      <th style="border:1px solid #000;padding:7px 5px;font-size:11px;text-align:center;font-weight:bold;width:60px">الفصل</th>
    </tr>
  </thead>
  <tbody>
    ${rows || '<tr><td colspan="7" style="text-align:center;padding:10px;border:1px solid #000;color:#888">لا توجد بيانات</td></tr>'}
  </tbody>
</table>
<table style="width:100%;border-collapse:collapse;margin-top:30px">
  <tr>
    <td style="border:none;text-align:center;width:50%"><div style="font-size:13px">لجنة النظام والمراقبة</div><div style="border-top:1px solid #000;padding-top:4px;margin-top:30px;font-size:12px">التوقيع</div></td>
    <td style="border:none;text-align:center;width:50%"><div style="font-size:13px">مدير المدرسة</div><div style="border-top:1px solid #000;padding-top:4px;margin-top:30px;font-size:12px">التوقيع</div></td>
  </tr>
</table>
</body>
</html>`);
    w.document.close();
  }

  // ── Preview helpers ──
  getPreviewSubjects(certId: number | null): CertificateSubject[] {
    const cert = this.certificates().find(c => c.id === certId);
    return cert?.subjects || [];
  }

  getPreviewGrade(certId: number | null): string {
    return this.certificates().find(c => c.id === certId)?.gradeLevel || '';
  }

  getPreviewName(certId: number | null): string {
    return this.certificates().find(c => c.id === certId)?.name || '';
  }

  getPreviewExam(certId: number | null): string {
    const cert = this.certificates().find(c => c.id === certId);
    if (!cert) return '';
    return `امتحان ${cert.examRole} ${cert.term} لعام ${cert.year}`;
  }

  getPreviewCounted(certId: number | null, type: 'max' | 'min'): number {
    const cert = this.certificates().find(c => c.id === certId);
    if (!cert?.subjects) return 0;
    const counted = cert.subjects.filter(s => s.isCountedInTotal);
    return type === 'max' ? counted.reduce((s, sub) => s + sub.maxScore, 0) : counted.reduce((s, sub) => s + sub.minScore, 0);
  }

  getPreviewGrand(certId: number | null): number {
    const cert = this.certificates().find(c => c.id === certId);
    if (!cert?.subjects) return 0;
    const counted = cert.subjects.filter(s => s.isCountedInTotal).reduce((s, sub) => s + sub.maxScore, 0);
    const activities = cert.subjects.filter(s => !s.isCountedInTotal).reduce((s, sub) => s + sub.maxScore, 0);
    return counted + activities;
  }

  // ── Helpers ──
  totalSubjects = computed(() => this.certificates().reduce((s, c) => s + (c.subjects?.length || 0), 0));

  private showError(msg: string) { this.errorMessage.set(msg); this.successMessage.set(''); setTimeout(() => this.errorMessage.set(''), 4000); }
  private showSuccess(msg: string) { this.successMessage.set(msg); this.errorMessage.set(''); setTimeout(() => this.successMessage.set(''), 3000); }
}
