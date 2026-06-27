import { Component, signal, OnInit, inject, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { AuthService } from '../../core/services/auth.service';
import { RoleService } from '../../shared/role.service';
import { LessonFeedbackService, ClassSubjectTeacherDto, LessonFeedbackDto } from './lesson-feedback.service';
import { TimetableService } from '../../core/services/timetable.service';

@Component({
  selector: 'app-lesson-feedback',
  imports: [Sidebar, FormsModule],
  templateUrl: './lesson-feedback.html',
  styleUrl: './lesson-feedback.css'
})
export class LessonFeedback implements OnInit {
  auth = inject(AuthService);
  private roleService = inject(RoleService);
  private service = inject(LessonFeedbackService);
  private timetableService = inject(TimetableService);

  role = computed(() => this.roleService.currentRole());
  sidebarOpen = signal(false);

  loading = signal(true);
  error = signal('');

  subjects = signal<ClassSubjectTeacherDto[]>([]);
  subjectDaysMap = signal<Record<number, string[]>>({});
  feedbackHistory = signal<LessonFeedbackDto[]>([]);
  selectedSubjectId = signal<number | null>(null);
  lessonDays = signal<string[]>([]);
  private currentAcademicYearId: number | null = null;
  lessonDate = signal('');
  selectedDay = signal<string>('');
  feedbackRating = signal(5);
  feedbackUnderstanding = signal<number>(1);
  feedbackComment = signal('');
  submitLoading = signal(false);
  submitSuccess = signal('');
  existingLessonFeedback = signal<LessonFeedbackDto | null>(null);

  teacherSubjects = signal<ClassSubjectTeacherDto[]>([]);
  summaries = signal<Record<number, any>>({});
  expandedCst = signal<number | null>(null);
  teacherRawFeedback = signal<Record<number, LessonFeedbackDto[]>>({});

  teachers = signal<any[]>([]);
  selectedTeacherId = signal<number | null>(null);
  adminSubjects = signal<ClassSubjectTeacherDto[]>([]);
  adminSummaries = signal<Record<number, any>>({});
  adminRawMode = signal(false);
  rawFeedback = signal<Record<number, LessonFeedbackDto[]>>({});

  async ngOnInit() {
    this.loading.set(true);
    this.error.set('');

    try {
      const r = this.role();
      if (r === 'student') {
        await this.loadStudentData();
      } else if (r === 'teacher') {
        await this.loadTeacherData();
      } else if (r === 'admin') {
        await this.loadAdminData();
      }
    } catch (e: any) {
      this.error.set(e?.message || 'حدث خطأ أثناء تحميل البيانات');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadStudentData() {
    const user = this.auth.user();
    if (!user) { this.error.set('المستخدم غير موجود'); return; }

    const student: any = await this.service.getStudentByUserId(user.userId).toPromise();
    if (!student?.id) { this.error.set('بيانات الطالب غير موجودة'); return; }

    const academicYear: any = await this.service.getCurrentAcademicYear().toPromise();
    if (!academicYear?.id) { this.error.set('السنة الدراسية غير موجودة'); return; }

    this.currentAcademicYearId = academicYear.id;

    const enrollment: any = await this.service.getActiveEnrollment(student.id, academicYear.id).toPromise();
    if (!enrollment?.id) { this.error.set('التسجيل غير موجود'); return; }

    const subs = await this.service.getStudentSubjects(enrollment.classId, academicYear.id).toPromise();
    this.subjects.set(subs ?? []);

    // Load lesson days for all subjects from the active timetable
    await this.loadAllSubjectDays(enrollment.classId, academicYear.id);

    const feedback = await this.service.getMyFeedback(enrollment.id).toPromise();
    this.feedbackHistory.set(feedback ?? []);
  }

  async onSubjectSelect(cstId: number) {
    this.selectedSubjectId.set(cstId);
    this.existingLessonFeedback.set(null);
    this.feedbackRating.set(5);
    this.feedbackUnderstanding.set(1);
    this.feedbackComment.set('');
    this.submitSuccess.set('');
    this.lessonDays.set([]);
    this.selectedDay.set('');
    this.lessonDate.set('');

    await this.loadLessonDays(cstId);
  }

  private async loadAllSubjectDays(classId: number, academicYearId: number) {
    try {
      const res: any = await this.timetableService.getByClass(classId, academicYearId).toPromise();
      const allTts: any[] = res?.data ?? res ?? [];
      // Prefer draft (latest editable state) over active timetable
      const tt = Array.isArray(allTts)
        ? (allTts.find((t: any) => !t.isActive) || allTts.find((t: any) => t.isActive))
        : null;
      const slots: any[] = tt?.slots ?? [];
      const map: Record<number, string[]> = {};
      for (const slot of slots) {
        if (slot.isBreak || !slot.classSubjectTeacherId) continue;
        const cstId = slot.classSubjectTeacherId;
        if (!map[cstId]) map[cstId] = [];
        if (!map[cstId].includes(slot.dayOfWeek)) map[cstId].push(slot.dayOfWeek);
      }
      this.subjectDaysMap.set(map);
    } catch { /* no timetable */ }
  }

  private async loadLessonDays(cstId: number) {
    const existing = this.subjectDaysMap()[cstId];
    if (existing) {
      this.lessonDays.set(existing);
      return;
    }
    const sub = this.subjects().find(s => s.id === cstId);
    if (!sub || !this.currentAcademicYearId) return;
    await this.loadAllSubjectDays(sub.classId, this.currentAcademicYearId);
    this.lessonDays.set(this.subjectDaysMap()[cstId] ?? []);
  }

  private getLastDateForDay(dayName: string): string {
    const dayMap: Record<string, number> = {
      Sunday: 0, Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4,
    };
    const target = dayMap[dayName];
    if (target === undefined) return '';
    const today = new Date();
    const todayDay = today.getDay();
    let diff = target - todayDay;
    if (diff > 0) diff -= 7;
    const last = new Date(today);
    last.setDate(today.getDate() + diff);
    return last.toISOString().split('T')[0];
  }

  async onDayChange() {
    const day = this.selectedDay();
    const cstId = this.selectedSubjectId();
    if (!day || !cstId) { this.existingLessonFeedback.set(null); return; }
    this.lessonDate.set(this.getLastDateForDay(day));
    this.submitSuccess.set('');
    await this.checkExistingFeedback(cstId, this.lessonDate());
  }

  private async checkExistingFeedback(cstId: number, date: string) {
    const feedbacks = await this.service.getFeedbackByLesson(cstId, date).toPromise();
    this.existingLessonFeedback.set(feedbacks && feedbacks.length > 0 ? feedbacks[0] : null);
  }

  async onSubmit() {
    const user = this.auth.user();
    if (!user) return;

    const academicYear: any = await this.service.getCurrentAcademicYear().toPromise();
    if (!academicYear?.id) { this.error.set('السنة الدراسية غير موجودة'); return; }

    const student: any = await this.service.getStudentByUserId(user.userId).toPromise();
    if (!student?.id) return;

    const enrollment: any = await this.service.getActiveEnrollment(student.id, academicYear.id).toPromise();
    if (!enrollment?.id) return;

    const cstId = this.selectedSubjectId();
    if (!cstId || !this.lessonDate()) return;

    this.submitLoading.set(true);
    this.submitSuccess.set('');

    try {
      await this.service.submitFeedback({
        enrollmentId: enrollment.id,
        classSubjectTeacherId: cstId,
        lessonDate: this.lessonDate(),
        rating: this.feedbackRating(),
        understanding: this.feedbackUnderstanding(),
        comment: this.feedbackComment() || undefined,
      }).toPromise();

      this.submitSuccess.set('تم تسجيل تقييم الدرس بنجاح');
      this.feedbackRating.set(5);
      this.feedbackUnderstanding.set(1);
      this.feedbackComment.set('');

      const feedback = await this.service.getMyFeedback(enrollment.id).toPromise();
      this.feedbackHistory.set(feedback ?? []);
      await this.checkExistingFeedback(cstId, this.lessonDate());
    } catch (e: any) {
      this.error.set(e?.message || 'حدث خطأ');
    } finally {
      this.submitLoading.set(false);
    }
  }

  private async loadTeacherData() {
    const subs = await this.service.getMyAssignments().toPromise();
    const items = subs ?? [];
    this.teacherSubjects.set(items);
    for (const s of items) {
      const summary = await this.service.getFeedbackSummary(s.id).toPromise();
      this.summaries.update(m => ({ ...m, [s.id]: summary }));
    }
  }

  onUnderstandingClick(v: number) {
    this.feedbackUnderstanding.set(v);
    if (v === 1) this.feedbackRating.set(5);
    else if (v === 2) this.feedbackRating.set(3);
    else this.feedbackRating.set(1);
  }

  async onToggleExpand(cstId: number) {
    if (this.expandedCst() === cstId) {
      this.expandedCst.set(null);
      return;
    }
    this.expandedCst.set(cstId);
    if (this.role() === 'teacher') {
      const raw = await this.service.getFeedbackRaw(cstId).toPromise();
      this.teacherRawFeedback.update(m => ({ ...m, [cstId]: raw ?? [] }));
    }
  }

  private async loadAdminData() {
    const ts = await this.service.getTeachers().toPromise();
    this.teachers.set(ts ?? []);
  }

  async onTeacherSelect(teacherId: number) {
    this.selectedTeacherId.set(teacherId);
    this.adminSubjects.set([]);
    this.adminSummaries.set({});
    this.rawFeedback.set({});

    const academicYear: any = await this.service.getCurrentAcademicYear().toPromise();
    if (!academicYear?.id) return;

    const subs = await this.service.getTeacherAssignments(teacherId, academicYear.id).toPromise();
    const items = subs ?? [];
    this.adminSubjects.set(items);

    for (const s of items) {
      const summary = await this.service.getFeedbackSummary(s.id).toPromise();
      this.adminSummaries.update(m => ({ ...m, [s.id]: summary }));

      const raw = await this.service.getFeedbackRaw(s.id).toPromise();
      this.rawFeedback.update(m => ({ ...m, [s.id]: raw ?? [] }));
    }
  }

  toggleAdminView() {
    this.adminRawMode.set(!this.adminRawMode());
  }

  dayLabel(day: string): string {
    const labels: Record<string, string> = {
      Sunday: 'الأحد', Monday: 'الإثنين', Tuesday: 'الثلاثاء',
      Wednesday: 'الأربعاء', Thursday: 'الخميس',
    };
    return labels[day] || day;
  }

  understandingLabel(v: any): string {
    const n = typeof v === 'number' ? v : parseInt(v, 10);
    switch (n) {
      case 1: return 'نعم';
      case 2: return 'جزئياً';
      case 3: return 'لا';
      default: return '';
    }
  }

  todayString(): string {
    return new Date().toISOString().split('T')[0];
  }
}
