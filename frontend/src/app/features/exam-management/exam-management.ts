import { Component, signal, computed, inject, OnInit, OnDestroy } from '@angular/core';
import { NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import {
  ExamManagerService,
  ExamItem, ExamDetail, ExamStats, ExamFilter,
  ExamDraftQuestion, CreateExamQuestionPayload,
  ExamAttemptSummary, ExamAttemptGradingDetail, AiGradeSuggestion
} from './exam-manager.service';
import { AcademicYearService } from '../../core/services/academic-year.service';
import { GradeLevelService } from '../../core/services/grade-level.service';
import {
  QuestionBankService, QuestionBankItemDto, SearchQuestionBankDto
} from '../../core/services/question-bank.service';

@Component({
  selector: 'app-exam-management',
  imports: [Sidebar, FormsModule, NgClass],
  templateUrl: './exam-management.html',
  styleUrl: './exam-management.css'
})
export class ExamManagement implements OnInit, OnDestroy {
  private router = inject(Router);
  private api = inject(ExamManagerService);
  private academicYearSvc = inject(AcademicYearService);
  private gradeLevelSvc = inject(GradeLevelService);
  private qbSvc = inject(QuestionBankService);
  private destroy$ = new Subject<void>();

  // ── Academic Year ─────────────────────────────────────────────
  private currentAcademicYearId = signal<number | undefined>(undefined);

  // ── Layout ────────────────────────────────────────────────────
  sidebarOpen = signal(false);
  activeTab = signal<string>('all');

  // ── Filters & Pagination ──────────────────────────────────────
  searchTerm = signal('');
  subjectFilter = signal<number | undefined>(undefined);
  sortBy = signal('newest');
  page = signal(1);
  pageSize = signal(20);
  totalCount = signal(0);
  totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  private searchSubject = new Subject<string>();

  // ── Modals ────────────────────────────────────────────────────
  showAddModal    = signal(false);
  showViewModal   = signal(false);
  showPublishConfirm = signal(false);
  showDeleteConfirm  = signal(false);
  showResultsModal   = signal(false);
  showGradingModal   = signal(false);

  // ── Modal state ───────────────────────────────────────────────
  viewingExam    = signal<ExamDetail | null>(null);
  editingExam    = signal<ExamItem | null>(null);
  publishingExam = signal<ExamItem | null>(null);
  deletingExam   = signal<ExamItem | null>(null);
  resultsExam    = signal<ExamAttemptSummary[] | null>(null);
  resultsExamId  = signal<number | null>(null);
  gradingAttempt = signal<ExamAttemptGradingDetail | null>(null);
  aiGradingLoading = signal(false);
  aiGradeSuggestions = signal<AiGradeSuggestion[] | null>(null);
  aiGradeError = signal('');

  // ── Form fields ───────────────────────────────────────────────
  formError  = signal('');
  loadError  = signal('');
  loading    = signal(false);

  newName      = signal('');
  newSubjectId = signal<number | null>(null);
  newClassId   = signal<number | null>(null);
  newGradeLevelId = signal<number | null>(null);
  /** 'class' = نشر لفصل محدد، 'grade' = نشر للصف الدراسي كله */
  scopeMode   = signal<'class' | 'grade'>('class');
  newDate      = signal('');
  newStart     = signal('');
  newEnd       = signal('');
  newTotalScore = signal<number>(100);
  draftTotal    = computed(() => this.draftQuestions().reduce((sum, q) => sum + (q.points || 0), 0));

  // ── Draft questions (for create / edit modal) ─────────────────
  draftQuestions = signal<ExamDraftQuestion[]>([]);
  private _draftCounter = 0;

  // ── Question Bank picker (داخل مودال الإنشاء/التعديل) ─────────
  showBankPanel = signal(false);
  bankLoading = signal(false);
  bankQuestions = signal<QuestionBankItemDto[]>([]);
  bankSearchText = signal('');
  bankSelectedType = signal<number | null>(null);
  bankSelectedIds = signal<Set<number>>(new Set());
  bankTotalCount = signal(0);
  bankPage = signal(1);
  bankPageSize = signal(10);
  bankTotalPages = computed(() => Math.max(1, Math.ceil(this.bankTotalCount() / this.bankPageSize())));
  private bankSearchSubject = new Subject<string>();

  readonly BANK_QUESTION_TYPES = [
    { value: null, label: 'كل الأنواع' },
    { value: 1, label: 'اختيار من متعدد' },
    { value: 2, label: 'صح/خطأ' },
    { value: 3, label: 'أكمل الفراغ' },
    { value: 4, label: 'مقالي' },
  ];

  /** هل الفورم جاهز لاستخدام البنك؟ لازم مادة + صف دراسي يكونوا مختارين */
  canUseBank = computed(() => !!(this.newSubjectId() && this.newGradeLevelId()));

  // ── Data ──────────────────────────────────────────────────────
  exams    = signal<ExamItem[]>([]);
  stats    = signal<ExamStats>({ total: 0, upcoming: 0, ended: 0, avgScore: 0 });
  subjects = signal<{ id: number; name: string }[]>([]);
  classes  = signal<{ id: number; name: string; gradeLevelId: number }[]>([]);
  gradeLevels = signal<{ id: number; name: string }[]>([]);

  // ── Static tabs ───────────────────────────────────────────────
  readonly examTabs = [
    { key: 'all',      label: 'الكل'       },
    { key: 'upcoming', label: 'قادمة'      },
    { key: 'active',   label: 'جاري الآن'  },
    { key: 'ended',    label: 'منتهية'     },
    { key: 'draft',    label: 'مسودة'      },
  ];

  readonly sortOptions = [
    { value: 'newest',    label: 'الأحدث'   },
    { value: 'oldest',    label: 'الأقدم'   },
    { value: 'name-asc',  label: 'أ - ي'    },
    { value: 'name-desc', label: 'ي - أ'    },
    { value: 'date-asc',  label: 'التاريخ (تصاعدي)' },
    { value: 'date-desc', label: 'التاريخ (تنازلي)' },
  ];

  calculatedDuration = computed(() => this.calculateDurationMinutes(this.newStart(), this.newEnd()));

  /** الفصول المتاحة للصف الدراسي المختار حاليًا في الفورم (لو محدد) */
  filteredClasses = computed(() => {
    const gradeId = this.newGradeLevelId();
    const all = this.classes();
    return gradeId ? all.filter(c => c.gradeLevelId === gradeId) : all;
  });

  ngOnInit() {
    this.academicYearSvc.getCurrent().subscribe({
      next: r => {
        const year = r?.data ?? r;
        if (year?.id) this.currentAcademicYearId.set(year.id);
      },
      error: () => {}
    });

    // Debounced search
    this.searchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(() => {
      this.page.set(1);
      this.loadExams();
    });

    // Debounced search داخل بانل بنك الأسئلة
    this.bankSearchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(() => {
      this.bankPage.set(1);
      this.searchBank();
    });

    this.loadAll();
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onSearchInput(value: string) {
    this.searchTerm.set(value);
    this.searchSubject.next(value);
  }

  onSubjectChange(subjectId: number | string) {
    this.subjectFilter.set(subjectId ? Number(subjectId) : undefined);
    this.page.set(1);
    this.loadExams();
  }

  onSortChange(sort: string) {
    this.sortBy.set(sort);
    this.page.set(1);
    this.loadExams();
  }

  setActiveTab(tab: string) {
    this.activeTab.set(tab);
    this.page.set(1);
    this.loadExams();
  }

  goToPage(p: number) {
    if (p < 1 || p > this.totalPages()) return;
    this.page.set(p);
    this.loadExams();
  }

  pageNumbers(): number[] {
    const total = this.totalPages();
    const current = this.page();
    if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
    const pages: number[] = [];
    if (current > 2) pages.push(1);
    if (current > 3) pages.push(-1);
    if (current > 1) pages.push(current - 1);
    pages.push(current);
    if (current < total) pages.push(current + 1);
    if (current < total - 2) pages.push(-2);
    if (current < total - 1) pages.push(total);
    return pages;
  }

  setPageSize(size: number) {
    this.pageSize.set(size);
    this.page.set(1);
    this.loadExams();
  }

  private buildFilter(): ExamFilter {
    return {
      search: this.searchTerm() || undefined,
      subjectId: this.subjectFilter(),
      status: this.activeTab() === 'all' ? undefined : this.activeTab(),
      sortBy: this.sortBy(),
      page: this.page(),
      pageSize: this.pageSize(),
      academicYearId: this.currentAcademicYearId(),
    };
  }

  loadExams() {
    this.loading.set(true);
    this.loadError.set('');
    this.api.getAll(this.buildFilter()).subscribe({
      next: r => {
        if (r.isSuccess) {
          this.exams.set(r.data.items);
          this.totalCount.set(r.data.totalCount);
        }
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('تعذّر تحميل الامتحانات، تحقق من الاتصال وأعد المحاولة.');
        this.loading.set(false);
      }
    });
  }

  loadAll() {
    this.loadExams();

    const yearId = this.currentAcademicYearId();
    this.api.getStats(undefined, yearId).subscribe({
      next:  r => { if (r.isSuccess) this.stats.set(r.data); },
      error: () => {}
    });

    this.api.getSubjects().subscribe({
      next:  r => this.subjects.set(r),
      error: () => this.subjects.set([])
    });

    this.gradeLevelSvc.getAll().subscribe({
      next: (res: any) => {
        const data = res?.data ?? (Array.isArray(res) ? res : []);
        this.gradeLevels.set((data as any[]).map(g => ({ id: g.id, name: g.name })));
      },
      error: () => this.gradeLevels.set([])
    });
  }

  goToGenerator() {
    this.router.navigate(['/exam-generator']);
  }

  openAddModal() {
    this.editingExam.set(null);
    this.newName.set('');
    this.newSubjectId.set(null);
    this.newClassId.set(null);
    this.newGradeLevelId.set(null);
    this.classes.set([]);
    this.scopeMode.set('class');
    this.newDate.set('');
    this.newStart.set('');
    this.newEnd.set('');
    this.newTotalScore.set(100);
    this.formError.set('');
    this.draftQuestions.set([]);
    this.showAddModal.set(true);
  }

  /** تحميل قائمة الفصول الخاصة بمادة معيّنة (المعلم + المادة معاً) */
  private loadClassesForSubject(subjectId: number | null) {
    if (!subjectId) {
      this.classes.set([]);
      return;
    }
    this.api.getClasses(subjectId).subscribe({
      next:  r => this.classes.set(r),
      error: () => this.classes.set([])
    });
  }

  /** لما يبدّل المعلم المادة في فورم إنشاء/تعديل امتحان — نعيد تحميل الفصول المرتبطة بها ونصفّر اختيار الفصل السابق */
  onFormSubjectChange(value: number | string | null) {
    const id = value ? Number(value) : null;
    this.newSubjectId.set(id);
    this.newClassId.set(null);
    this.loadClassesForSubject(id);
  }

  /** لما يبدّل المعلم الصف الدراسي — نصفّر اختيار الفصل لو مبقى مش مناسب للصف الجديد */
  onFormGradeLevelChange(value: number | string | null) {
    const id = value ? Number(value) : null;
    this.newGradeLevelId.set(id);
    const currentClassId = this.newClassId();
    if (currentClassId != null && id != null) {
      const cls = this.classes().find(c => c.id === currentClassId);
      if (cls && cls.gradeLevelId !== id) this.newClassId.set(null);
    }
  }

  openEditModal(e: ExamItem) {
    this.editingExam.set(e);
    this.newName.set(e.name);
    const subj = this.subjects().find(s => s.name === e.subject);
    const subjectId = subj ? subj.id : (e.subjectId ?? null);
    this.newSubjectId.set(subjectId);
    this.newClassId.set(e.classId ?? null);
    // scopeMode: لو classId موجود → 'class'، لو null → 'grade'
    this.scopeMode.set(e.classId ? 'class' : 'grade');
    this.newGradeLevelId.set(e.gradeLevelId ?? null);
    this.newDate.set(e.date);
    this.newStart.set(e.startTime);
    this.newEnd.set(e.endTime);
    this.formError.set('');
    this.draftQuestions.set([]);

    this.loadClassesForSubject(subjectId);

    // جلب الأسئلة الحالية للامتحان وتعبئة draftQuestions
    this.api.getById(e.id).subscribe({
      next: detail => {
        // حدّث الصف الدراسي من التفاصيل الكاملة لو موجود
        if (detail.gradeLevelId != null) this.newGradeLevelId.set(detail.gradeLevelId);
        this.newTotalScore.set(detail.totalScore ?? 100);
        const drafts: ExamDraftQuestion[] = detail.questions.map(q => ({
          _localId: ++this._draftCounter,
          id: q.id,
          type: q.type as 'mcq' | 'true-false' | 'fill-blank' | 'essay',
          text: q.text,
          options: q.options ? [...q.options] : (q.type === 'mcq' || q.type === 'true-false' ? ['', '', '', ''] : []),
          correctAnswer: q.correctAnswer,
          points: q.points ?? 10,
        }));
        this.draftQuestions.set(drafts);
        this.showAddModal.set(true);
      },
      error: () => {
        // فتح المودال بدون أسئلة لو فشل الجلب
        this.showAddModal.set(true);
      }
    });
  }

  openViewModal(e: ExamItem) {
    this.api.getById(e.id).subscribe(detail => {
      this.viewingExam.set(detail);
      this.showViewModal.set(true);
    });
  }

  closeModals() {
    this.showAddModal.set(false);
    this.showViewModal.set(false);
    this.showPublishConfirm.set(false);
    this.showDeleteConfirm.set(false);
    this.showResultsModal.set(false);
    this.showGradingModal.set(false);
    this.viewingExam.set(null);
    this.publishingExam.set(null);
    this.deletingExam.set(null);
    this.resultsExam.set(null);
    this.resultsExamId.set(null);
    this.gradingAttempt.set(null);
    this.aiGradeSuggestions.set(null);
    this.aiGradeError.set('');
    this.aiGradingLoading.set(false);
    this.formError.set('');
    this.draftQuestions.set([]);
    this.resetBankPanel();
  }

  saveExam() {
    const durationMinutes = this.calculatedDuration();
    const isGradeScope = this.scopeMode() === 'grade';

    // بيانات أساسية مطلوبة دائماً
    if (!this.newName().trim() || !this.newSubjectId() || !this.newGradeLevelId()
        || !this.newDate() || !this.newStart() || !this.newEnd()) {
      this.formError.set('من فضلك اكمل بيانات الامتحان (الاسم، المادة، الصف، التاريخ، الأوقات) قبل الحفظ');
      return;
    }

    // لو scope = فصل محدد، الفصل مطلوب
    if (!isGradeScope && !this.newClassId()) {
      this.formError.set('من فضلك اختر الفصل الدراسي أو بدّل النطاق إلى "الصف كله"');
      return;
    }

    if (durationMinutes <= 0) {
      this.formError.set('وقت النهاية يجب ان يكون بعد وقت البداية');
      return;
    }

    const drafts = this.draftQuestions();
    for (const q of drafts) {
      if (!q.text.trim()) {
        this.formError.set('من فضلك اكتب نص كل سؤال قبل الحفظ');
        return;
      }
      if (!q.points || q.points <= 0) {
        this.formError.set('من فضلك أدخل درجة صحيحة (أكبر من صفر) لكل سؤال');
        return;
      }
      if (q.type === 'mcq') {
        const filledOptions = q.options.filter(o => o.trim());
        if (filledOptions.length < 2) {
          this.formError.set('سؤال الاختيار من متعدد يجب أن يحتوي على خيارين على الأقل');
          return;
        }
        if (!q.correctAnswer.trim() || !filledOptions.includes(q.correctAnswer)) {
          this.formError.set('من فضلك حدد الإجابة الصحيحة لكل سؤال اختيار من متعدد');
          return;
        }
      }
      if (q.type === 'true-false' && !q.correctAnswer.trim()) {
        this.formError.set('من فضلك حدد الإجابة الصحيحة لكل سؤال صح وخطأ');
        return;
      }
      if (q.type === 'fill-blank' && !q.correctAnswer.trim()) {
        this.formError.set('من فضلك حدد الإجابة الصحيحة لكل سؤال أكمل الفراغ');
        return;
      }
    }

    const questions: CreateExamQuestionPayload[] = drafts.map(q => ({
      type: q.type,
      text: q.text.trim(),
      options: (q.type === 'mcq' || q.type === 'true-false') ? q.options.filter(o => o.trim()) : undefined,
      correctAnswer: q.correctAnswer || undefined,
      points: q.points,
    }));

    const payload = {
      title: this.newName().trim(),
      subjectId: this.newSubjectId() ?? 0,
      gradeLevelId: this.newGradeLevelId() ?? 0,
      classId: isGradeScope ? null : (this.newClassId() ?? null),
      date: this.newDate(),
      startTime: this.newStart(),
      endTime: this.newEnd(),
      durationMinutes,
      totalScore: this.newTotalScore(),
      questions,
    };

    const existing = this.editingExam();
    if (existing) {
      this.api.update(existing.id, payload).subscribe({
        next: r => {
          if (r.isSuccess) { this.closeModals(); this.loadAll(); }
          else this.formError.set(r.message ?? 'حدث خطأ أثناء التحديث');
        },
        error: err => this.formError.set(err?.error?.message ?? 'خطأ في الشبكة، حاول مرة أخرى')
      });
    } else {
      this.api.create(payload).subscribe({
        next: r => {
          if (r.isSuccess) { this.closeModals(); this.loadAll(); }
          else this.formError.set(r.message ?? 'حدث خطأ أثناء الإنشاء');
        },
        error: err => this.formError.set(err?.error?.message ?? 'خطأ في الشبكة، حاول مرة أخرى')
      });
    }
  }

  requestDeleteExam(e: ExamItem) {
    this.deletingExam.set(e);
    this.showDeleteConfirm.set(true);
  }

  confirmDeleteExam() {
    const exam = this.deletingExam();
    if (!exam) return;
    this.api.delete(exam.id).subscribe({
      next: r => {
        if (r.isSuccess) { this.closeModals(); this.loadAll(); }
      },
      error: () => this.closeModals()
    });
  }

  publishExam(id: number) {
    const exam = this.exams().find(e => e.id === id);
    if (!exam) return;
    this.publishingExam.set(exam);
    this.showPublishConfirm.set(true);
  }

  confirmPublish() {
    const exam = this.publishingExam();
    if (!exam) return;

    this.api.publish(exam.id).subscribe({
      next: r => {
        if (r.isSuccess) { this.closeModals(); this.loadAll(); }
        else this.formError.set(r.message ?? 'تعذّر النشر');
      },
      error: err => this.formError.set(err?.error?.message ?? 'خطأ في الشبكة')
    });
  }

  togglePublishResults(examId: number, publish: boolean) {
    const call = publish
      ? this.api.publishResults(examId)
      : this.api.unpublishResults(examId);

    call.subscribe({
      next:  r => { if (r.isSuccess) this.loadAll(); else alert(r.message); },
      error: err => alert(err?.error?.message ?? 'خطأ في الشبكة')
    });
  }

  // ── Draft questions helpers ───────────────────────────────────

  addQuestion() {
    this.draftQuestions.update(qs => [
      ...qs,
      {
        _localId: ++this._draftCounter,
        id: undefined,
        type: 'mcq',
        text: '',
        options: ['', '', '', ''],
        correctAnswer: '',
        points: 10,
      }
    ]);
  }

  removeQuestion(localId: number) {
    this.draftQuestions.update(qs => qs.filter(q => q._localId !== localId));
  }

  updateQuestionType(localId: number, type: 'mcq' | 'true-false' | 'fill-blank' | 'essay') {
    this.draftQuestions.update(qs => qs.map(q => {
      if (q._localId !== localId) return q;
      const options = type === 'true-false' ? ['صواب', 'خطأ']
                    : type === 'mcq'       ? ['', '', '', '']
                    : [];
      return { ...q, type, options, correctAnswer: '' };
    }));
  }

  updateQuestionText(localId: number, text: string) {
    this.draftQuestions.update(qs =>
      qs.map(q => q._localId === localId ? { ...q, text } : q)
    );
  }

  updateQuestionOption(localId: number, idx: number, value: string) {
    this.draftQuestions.update(qs => qs.map(q => {
      if (q._localId !== localId) return q;
      const options = [...q.options];
      options[idx] = value;
      return { ...q, options };
    }));
  }

  updateQuestionAnswer(localId: number, answer: string) {
    this.draftQuestions.update(qs =>
      qs.map(q => q._localId === localId ? { ...q, correctAnswer: answer } : q)
    );
  }

  updateQuestionPoints(localId: number, pts: number) {
    this.draftQuestions.update(qs =>
      qs.map(q => q._localId === localId ? { ...q, points: pts } : q)
    );
  }

  // ── Question Bank picker ─────────────────────────────────────

  /** تحويل نوع سؤال البنك (رقمي) إلى نوع الـ draft (string) */
  private bankTypeToDraft(n: number): ExamDraftQuestion['type'] {
    switch (n) {
      case 1: return 'mcq';
      case 2: return 'true-false';
      case 3: return 'fill-blank';
      case 4: return 'essay';
      default: return 'mcq';
    }
  }

  /** تحويل سؤال من بنك الأسئلة إلى صيغة الـ draft المستخدمة في الامتحان */
  private toDraftQuestion(b: QuestionBankItemDto): ExamDraftQuestion {
    const type = this.bankTypeToDraft(b.questionType);
    let options: string[] = [];
    let correctAnswer = '';

    if (type === 'mcq') {
      // رجّع نصوص الخيارات من البنك، وأضمن على الأقل 2 خيارات (حتى لو فاضية)
      // عشان الـ UI للـ MCQ يعرض خانات الإدخال دايماً.
      const bankOptions = (b.options || []).map(o => o.optionText.trim()).filter(t => t.length > 0);
      options = bankOptions.length >= 2 ? bankOptions : ['', '', '', ''];

      // الإجابة الصحيحة: الأول من الخيار المعلّم isCorrect، وإلا من correctAnswer.
      let answer = (b.options || []).find(o => o.isCorrect)?.optionText.trim() ?? '';
      if (!answer) answer = (b.correctAnswer ?? '').trim();

      // أتأكد إن الإجابة الصحيحة موجودة فعلاً بين الخيارات، وإلا امسحها
      // (تجنّب وجود correctAnswer لا يطابق أي خيار — يبقى الـ radio محدد على...
      //  شيء غير موجود).
      correctAnswer = answer && options.includes(answer) ? answer : '';
    } else if (type === 'true-false') {
      options = ['صواب', 'خطأ'];
      correctAnswer = (b.correctAnswer ?? '').toLowerCase() === 'true' ? 'صواب' : 'خطأ';
    } else {
      // fill-blank / essay
      correctAnswer = b.correctAnswer ?? '';
    }

    return {
      _localId: ++this._draftCounter,
      type,
      text: b.questionText,
      options,
      correctAnswer,
      points: 10, // افتراضي — المدرس يقدر يعدّله
    };
  }

  /** فتح بانل بنك الأسئلة (لازم المادة والصف يكونوا مختارين) */
  openBankPanel() {
    if (!this.canUseBank()) {
      this.formError.set('اختر المادة والصف الدراسي أولاً قبل الإضافة من بنك الأسئلة');
      return;
    }
    this.formError.set('');
    this.showBankPanel.set(true);
    this.bankSearchText.set('');
    this.bankSelectedType.set(null);
    this.bankSelectedIds.set(new Set());
    this.bankPage.set(1);
    this.searchBank();
  }

  /** إغلاق بانل البنك وإعادة ضبط حالته */
  closeBankPanel() {
    this.showBankPanel.set(false);
    this.resetBankPanel();
  }

  private resetBankPanel() {
    this.bankQuestions.set([]);
    this.bankSearchText.set('');
    this.bankSelectedType.set(null);
    this.bankSelectedIds.set(new Set());
    this.bankPage.set(1);
    this.bankTotalCount.set(0);
    this.bankLoading.set(false);
  }

  onBankSearchInput(value: string) {
    this.bankSearchText.set(value);
    this.bankSearchSubject.next(value);
  }

  onBankTypeChange(value: number | null) {
    this.bankSelectedType.set(value);
    this.bankPage.set(1);
    this.searchBank();
  }

  /** بحث في بنك الأسئلة بالفلاتر الحالية (مفلتر بالمادة والصف المختارين في الفورم) */
  searchBank() {
    const subjectId = this.newSubjectId();
    const gradeLevelId = this.newGradeLevelId();
    if (!subjectId || !gradeLevelId) return;

    this.bankLoading.set(true);
    const dto: SearchQuestionBankDto = {
      searchText: this.bankSearchText() || undefined,
      subjectId,
      gradeLevelId,
      questionType: this.bankSelectedType() ?? undefined,
      page: this.bankPage(),
      pageSize: this.bankPageSize(),
    };

    this.qbSvc.search(dto).subscribe({
      next: (res: any) => {
        const data = res?.data;
        this.bankQuestions.set(data?.items || []);
        this.bankTotalCount.set(data?.totalCount || 0);
        this.bankLoading.set(false);
      },
      error: () => {
        this.bankLoading.set(false);
        this.bankQuestions.set([]);
        this.bankTotalCount.set(0);
      },
    });
  }

  bankGoToPage(p: number) {
    if (p < 1 || p > this.bankTotalPages()) return;
    this.bankPage.set(p);
    this.searchBank();
  }

  bankPageNumbers(): number[] {
    const total = this.bankTotalPages();
    const current = this.bankPage();
    const pages: number[] = [];
    const start = Math.max(1, current - 2);
    const end = Math.min(total, current + 2);
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  }

  toggleBankSelection(id: number) {
    this.bankSelectedIds.update(set => {
      const next = new Set(set);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  isBankSelected(id: number): boolean {
    return this.bankSelectedIds().has(id);
  }

  /** تحويل الأسئلة المختارة من البنك إلى أسئلة draft وإضافتها للامتحان */
  addSelectedFromBank() {
    const selected = this.bankQuestions().filter(q => this.bankSelectedIds().has(q.id));
    if (selected.length === 0) return;

    const drafts = selected.map(q => this.toDraftQuestion(q));
    this.draftQuestions.update(list => [...list, ...drafts]);

    this.bankSelectedIds.set(new Set());
    this.closeBankPanel();
  }

  /** اسم نوع سؤال البنك للعرض */
  bankQTypeName(v: number): string {
    const found = this.BANK_QUESTION_TYPES.find(t => t.value === v);
    return found?.label ?? '';
  }

  // ── Results / Grading ─────────────────────────────────────────

  openResultsModal(examId: number) {
    this.resultsExamId.set(examId);
    this.resultsExam.set(null);
    this.showResultsModal.set(true);

    this.api.getAttemptsByExam(examId).subscribe({
      next:  r => { if (r.isSuccess) this.resultsExam.set(r.data); },
      error: () => this.resultsExam.set([])
    });
  }

  openGradingModal(attemptId: number) {
    this.gradingAttempt.set(null);
    this.aiGradeSuggestions.set(null);
    this.aiGradeError.set('');
    this.aiGradingLoading.set(false);
    this.showGradingModal.set(true);

    this.api.getAttemptDetail(attemptId).subscribe({
      next:  r => { if (r.isSuccess) this.gradingAttempt.set(r.data); },
      error: () => this.closeModals()
    });
  }

  aiGradeAttempt() {
    const attempt = this.gradingAttempt();
    if (!attempt) return;

    this.aiGradingLoading.set(true);
    this.aiGradeError.set('');
    this.aiGradeSuggestions.set(null);

    this.api.aiGradeAttempt(attempt.id).subscribe({
      next: r => {
        this.aiGradingLoading.set(false);
        if (r.isSuccess && r.data) {
          this.aiGradeSuggestions.set(r.data.suggestions);
          // تطبيق الاقتراحات تلقائياً على الدرجات
          this.applyAiSuggestions(r.data.suggestions);
        } else {
          this.aiGradeError.set(r.message || 'فشل التصحيح بالذكاء الاصطناعي');
        }
      },
      error: err => {
        this.aiGradingLoading.set(false);
        this.aiGradeError.set(err?.error?.message || 'خطأ في الاتصال بخدمة الذكاء الاصطناعي');
      }
    });
  }

  private applyAiSuggestions(suggestions: AiGradeSuggestion[]) {
    this.gradingAttempt.update(a => {
      if (!a) return a;
      return {
        ...a,
        answers: a.answers.map(ans => {
          const suggestion = suggestions.find(s => s.answerId === ans.id);
          if (suggestion && (ans.questionType === 'essay' || ans.questionType === 'fill-blank')) {
            return {
              ...ans,
              pointsEarned: suggestion.suggestedPoints,
              _aiSuggested: suggestion.suggestedPoints,
              _aiJustification: suggestion.justification
            };
          }
          return ans;
        })
      };
    });
  }

  saveGrading() {
    const attempt = this.gradingAttempt();
    if (!attempt) return;

    const answers = attempt.answers
      .filter(a => a.questionType === 'essay' || a.questionType === 'fill-blank')
      .map(a => ({
        answerId:     a.id,
        pointsEarned: a.pointsEarned ?? 0,
        feedback:     a.feedback ?? '',
      }));

    this.api.gradeEssayAnswers(attempt.id, { answers }).subscribe({
      next: r => {
        if (r.isSuccess) {
          const examId = this.resultsExamId();
          this.showGradingModal.set(false);
          this.gradingAttempt.set(null);
          if (examId) this.openResultsModal(examId); // refresh results
        } else {
          alert(r.message ?? 'حدث خطأ أثناء الحفظ');
        }
      },
      error: err => alert(err?.error?.message ?? 'خطأ في الشبكة')
    });
  }

  setEssayGrade(answerId: number, pts: number) {
    this.gradingAttempt.update(a => {
      if (!a) return a;
      return {
        ...a,
        answers: a.answers.map(ans =>
          ans.id === answerId ? { ...ans, pointsEarned: pts } : ans
        )
      };
    });
  }

  setEssayFeedback(answerId: number, feedback: string) {
    this.gradingAttempt.update(a => {
      if (!a) return a;
      return {
        ...a,
        answers: a.answers.map(ans =>
          ans.id === answerId ? { ...ans, feedback } : ans
        )
      };
    });
  }

  private calculateDurationMinutes(start: string, end: string): number {
    if (!start || !end) return 0;

    const [startHours, startMinutes] = start.split(':').map(Number);
    const [endHours, endMinutes] = end.split(':').map(Number);
    if ([startHours, startMinutes, endHours, endMinutes].some(value => Number.isNaN(value))) return 0;

    return (endHours * 60 + endMinutes) - (startHours * 60 + startMinutes);
  }

  getStatusText(status: string): string {
    const map: Record<string, string> = { upcoming: 'قادم', active: 'جاري الآن', ended: 'منتهٍ', draft: 'مسودة' };
    return map[status] || status;
  }

  getStatusClass(status: string): string {
    const map: Record<string, string> = { upcoming: 'bg-secondary/10 text-secondary', active: 'bg-green-50 text-green-700', ended: 'bg-surface-container-high text-outline', draft: 'bg-tertiary-fixed/20 text-tertiary' };
    return map[status] || '';
  }

  /** هل الامتحان منشور للصف الدراسي كله (CST=null)؟ */
  isGradeScopeExam(e: ExamItem): boolean {
    return e.classId == null;
  }

  /** نص وصف النطاق لعرضه على الكارت */
  getScopeBadgeText(e: ExamItem): string {
    return this.isGradeScopeExam(e) ? 'الصف كله' : 'فصل محدد';
  }

  hasSchedule(e: ExamItem): boolean {
    return !!(e.date && e.date !== '' && e.startTime && e.startTime !== '' && e.endTime && e.endTime !== '');
  }

  isExamIncomplete(e: ExamItem): boolean {
    return e.isAIGenerated && !this.hasSchedule(e);
  }

  getNeedsAttentionMessage(e: ExamItem): string {
    if (!e.isAIGenerated) return '';
    const missing: string[] = [];
    if (!e.date || e.date === '') missing.push('التاريخ');
    if (!e.startTime || e.startTime === '') missing.push('وقت البداية');
    if (!e.endTime || e.endTime === '') missing.push('وقت النهاية');
    if (missing.length > 0) return `أضف ${missing.join('، ')}`;
    return '';
  }

  formatSubmittedAt(iso: string): string {
    // الـ API بترجع DateTimeOffset على شكل "2026-06-23T10:00:00+00:00" وهي صيغة
    // صالحة لـ Date مباشرة. لا نضيف 'Z' لأن لو القيمة فيها offset بالفعل (مثل +00:00)
    // هيبقى "...+00:00Z" وهي صيغة غير صالحة ويرجع NaN. نمرّر النص كما هو لـ Date.
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    const datePart = d.toLocaleDateString('ar-EG', { day: 'numeric', month: 'numeric', year: 'numeric' });
    const timePart = d.toLocaleTimeString('ar-EG', { hour: 'numeric', minute: '2-digit' });
    return `${datePart}، ${timePart}`;
  }
}
