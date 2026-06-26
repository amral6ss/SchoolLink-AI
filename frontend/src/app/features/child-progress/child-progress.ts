import { Component, signal, OnInit, inject } from '@angular/core';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { ChildProgressService, ChildProgressItem, ChildExamAttemptResult, ChildExamAnswer } from './child-progress.service';
import { AcademicYearService } from '../../core/services/academic-year.service';

interface AssignmentView {
  id: number;
  subject: string;
  title: string;
  deadline: string;
  status: 'submitted' | 'not-submitted' | 'late';
  score?: number;
  maxScore: number;
}

interface ExamView {
  id: number;
  subject: string;
  title: string;
  date: string;
  status: 'upcoming' | 'done' | 'missed' | 'pending';
  score?: number;
  maxScore: number;
}

@Component({
  selector: 'app-child-progress',
  imports: [Sidebar],
  templateUrl: './child-progress.html',
  styles: [`
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0,0,0,0.4);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: flex-start;
      justify-content: center;
      padding: 40px 16px;
      z-index: 1000;
      overflow-y: auto;
    }
    .modal-content {
      background: #fff;
      border-radius: 20px;
      padding: 28px;
      max-width: 720px;
      width: 100%;
      max-height: 85vh;
      overflow-y: auto;
      box-shadow: 0 20px 60px rgba(0,0,0,0.15);
    }
  `]
})
export class ChildProgress implements OnInit {
  private service = inject(ChildProgressService);
  private academicYearService = inject(AcademicYearService);

  sidebarOpen = signal(false);
  activeTab = signal<'assignments' | 'exams'>('assignments');

  children = signal<ChildProgressItem[]>([]);
  selectedChildIndex = signal<number>(0);

  student = signal<{ name: string; class: string; avgScore: number; attendance: number } | null>(null);
  assignments = signal<AssignmentView[]>([]);
  exams = signal<ExamView[]>([]);

  loading = signal(false);
  selectedTerm = signal<number>(0); // 0 يعني لم يتم التحديد بعد

  // Exam detail modal
  examDetail = signal<ChildExamAttemptResult | null>(null);
  examDetailLoading = signal(false);

  ngOnInit() {
    // استنى الترم الحالي الأول قبل تحميل البيانات
    this.academicYearService.getCurrentTerm().subscribe({
      next: (res) => {
        if (res?.data != null) {
          // Backend may serialise AcademicTerm as string name ("SecondSemester")
          // or as integer — normalise to int before storing
          const termMap: Record<string, number> = { FirstSemester: 1, SecondSemester: 2, Final: 3 };
          const term = typeof res.data === 'number' ? res.data : (termMap[res.data as string] ?? 1);
          this.selectedTerm.set(term);
        } else {
          this.selectedTerm.set(1);
        }
        this.loadData();
      },
      error: () => {
        this.selectedTerm.set(1);
        this.loadData();
      }
    });
  }

  loadData() {
    this.loading.set(true);
    this.service.get(this.selectedTerm()).subscribe({
      next: (items: ChildProgressItem[]) => {
        this.loading.set(false);
        if (items.length === 0) return;
        this.children.set(items);
        this.selectedChildIndex.set(0);
        this.displayChild(0);
      },
      error: () => this.loading.set(false),
    });
  }

  private displayChild(index: number) {
    const child = this.children()[index];
    if (!child) return;
    this.student.set({
      name: child.studentName,
      class: `${child.gradeLevelName} - ${child.className}`,
      avgScore: child.avgScore,
      attendance: child.attendancePercentage,
    });
    this.assignments.set(child.assignments.map(a => ({
      id: a.id,
      subject: a.subject,
      title: a.title,
      deadline: a.deadline ?? '',
      status: a.status as AssignmentView['status'],
      score: a.score,
      maxScore: a.maxScore,
    })));
    this.exams.set(child.exams.map(e => ({
      id: e.id,
      subject: e.subject,
      title: e.title,
      date: e.date ?? '',
      status: e.status as ExamView['status'],
      score: e.score,
      maxScore: e.maxScore,
    })));
  }

  onChildChange(event: Event) {
    const idx = Number((event.target as HTMLSelectElement).value);
    this.selectedChildIndex.set(idx);
    this.displayChild(idx);
  }

  onTermChange(event: Event) {
    const value = (event.target as HTMLSelectElement).value;
    this.selectedTerm.set(Number(value));
    this.loadData();
  }

  getStatusText(s: string): string {
    const m: Record<string, string> = { submitted: 'تم التسليم', 'not-submitted': 'لم يسلّم', late: 'متأخر', pending: 'قيد التصحيح', upcoming: 'قادم', done: 'أدّاه', missed: 'لم يؤدّه' };
    return m[s] || s;
  }

  getStatusClass(s: string): string {
    const m: Record<string, string> = { submitted: 'bg-green-50 text-green-700', 'not-submitted': 'bg-secondary/10 text-secondary', late: 'bg-error/10 text-error', pending: 'bg-amber-50 text-amber-700', upcoming: 'bg-secondary/10 text-secondary', done: 'bg-green-50 text-green-700', missed: 'bg-error/10 text-error' };
    return m[s] || '';
  }

  viewExamDetail(examId: number) {
    this.examDetailLoading.set(true);
    this.examDetail.set(null);
    this.service.getExamAttempt(examId).subscribe({
      next: (result) => {
        this.examDetail.set(result);
        this.examDetailLoading.set(false);
      },
      error: () => this.examDetailLoading.set(false),
    });
  }

  closeExamDetail() {
    this.examDetail.set(null);
  }
}
