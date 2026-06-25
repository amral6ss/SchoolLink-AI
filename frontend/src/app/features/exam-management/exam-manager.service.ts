import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { buildApiUrl } from '../../core/utils/api-url';
import { OperationResult, PagedResult } from '../../core/models/api.model';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

export interface ExamFilter {
  search?: string;
  subjectId?: number;
  status?: string;
  sortBy?: string;
  page?: number;
  pageSize?: number;
  academicYearId?: number;
}

export interface ExamItem {
  id: number;
  name: string;
  subject: string;
  class: string;
  subjectId?: number | null;
  classId?: number | null;
  gradeLevelId?: number | null;
  gradeLevel?: string;
  date: string;
  startTime: string;
  endTime: string;
  duration: number;
  questionCount: number;
  status: string;
  avgScore?: number;
  submitted?: number;
  total?: number;
  isResultPublished: boolean;
  pendingGradingCount?: number;
  isAIGenerated: boolean;
}

export interface ExamQuestion {
  id: number;
  type: string;
  text: string;
  options?: string[];
  correctAnswer: string;
  points: number;
}

/** سؤال مؤقت داخل مودال الإنشاء/التعديل (لم يُحفظ بعد) */
export interface ExamDraftQuestion {
  _localId: number;           // مُعرّف محلي فقط
  id?: number;                // مُعرّف السيرفر لو موجود (وقت التعديل)
  type: 'mcq' | 'true-false' | 'fill-blank' | 'essay';
  text: string;
  options: string[];
  correctAnswer: string;
  points: number;
}

export interface CreateExamQuestionPayload {
  type: 'mcq' | 'true-false' | 'fill-blank' | 'essay';
  text: string;
  options?: string[];
  correctAnswer?: string;
  points: number;
}

/** ملخص محاولة طالب (لمودال النتائج) */
export interface ExamAttemptSummary {
  id: number;
  studentName: string;
  score: number | null;
  totalScore: number;
  isGraded: boolean;
  submittedAt: string | null;
  status: string;       // 'submitted' | 'graded' | 'waitingGrade'
}

/** إجابة طالب (لمودال التصحيح) */
export interface ExamAttemptAnswerDetail {
  id: number;
  questionText: string;
  questionType: string;     // 'mcq' | 'true-false' | 'essay'
  questionPoints: number;
  answerText: string | null;
  isCorrect: boolean | null;
  pointsEarned: number;
  feedback?: string;
  correctAnswerText?: string;
  _aiSuggested?: number;      // الدرجة المقترحة من AI (محلي فقط)
  _aiJustification?: string;  // مبرر AI (محلي فقط)
}

/** تفاصيل محاولة كاملة (لمودال التصحيح) */
export interface ExamAttemptGradingDetail {
  id: number;
  studentName: string;
  score: number | null;
  totalScore: number;
  isGraded: boolean;
  answers: ExamAttemptAnswerDetail[];
}

export interface GradeEssayAnswerPayload {
  answerId: number;
  pointsEarned: number;
  feedback?: string;
}

export interface GradeEssayAttemptPayload {
  answers: GradeEssayAnswerPayload[];
}

export interface AiGradeSuggestion {
  answerId: number;
  suggestedPoints: number;
  justification?: string;
}

export interface AiGradeResponse {
  attemptId: number;
  suggestions: AiGradeSuggestion[];
}

export interface ExamDetail {
  id: number;
  name: string;
  subject: string;
  class: string;
  subjectId?: number | null;
  classId?: number | null;
  gradeLevelId?: number | null;
  gradeLevel?: string;
  date: string;
  startTime: string;
  endTime: string;
  duration: number;
  questionCount: number;
  status: string;
  isResultPublished: boolean;
  totalScore: number;
  questions: ExamQuestion[];
}

export interface ExamStats {
  total: number;
  upcoming: number;
  ended: number;
  avgScore: number;
}

export interface CreateExamPayload {
  title: string;
  subjectId: number;
  gradeLevelId: number;          // الصف الدراسي (مطلوب دائماً)
  classId?: number | null;        // فصل محدد (اختياري — null = نشر للصف كله)
  date: string;
  startTime: string;
  endTime: string;
  durationMinutes: number;
  totalScore: number;
  questions: CreateExamQuestionPayload[];
}

@Injectable({ providedIn: 'root' })
export class ExamManagerService {
  private http = inject(HttpClient);
  private base = buildApiUrl('exam-manager');
  private attemptsBase = buildApiUrl('exam-attempts');

  getAll(filter: ExamFilter = {}): Observable<OperationResult<PagedResult<ExamItem>>> {
    let params = new HttpParams();
    if (filter.search) params = params.set('search', filter.search);
    if (filter.subjectId) params = params.set('subjectId', filter.subjectId);
    if (filter.status && filter.status !== 'all') params = params.set('status', filter.status);
    if (filter.sortBy) params = params.set('sortBy', filter.sortBy);
    if (filter.page) params = params.set('page', filter.page);
    if (filter.pageSize) params = params.set('pageSize', filter.pageSize);
    if (filter.academicYearId) params = params.set('academicYearId', filter.academicYearId);
    return this.http.get<OperationResult<PagedResult<ExamItem>>>(this.base, { params });
  }

  getById(id: number): Observable<ExamDetail> {
    return this.http.get<OperationResult<ExamDetail>>(`${this.base}/${id}`).pipe(
      map(r => r.data)
    );
  }

  getStats(teacherId?: number, academicYearId?: number): Observable<OperationResult<ExamStats>> {
    let params = '';
    if (teacherId && academicYearId) {
      params = `?teacherId=${teacherId}&academicYearId=${academicYearId}`;
    }
    return this.http.get<OperationResult<ExamStats>>(`${this.base}/stats${params}`);
  }

  create(dto: CreateExamPayload): Observable<OperationResult<ExamDetail>> {
    return this.http.post<OperationResult<ExamDetail>>(this.base, dto);
  }

  update(id: number, dto: CreateExamPayload): Observable<OperationResult<any>> {
    return this.http.put<OperationResult<any>>(`${this.base}/${id}`, dto);
  }

  // ── Attempts / Grading ─────────────────────────────────────

  getAttemptsByExam(examId: number): Observable<OperationResult<ExamAttemptSummary[]>> {
    return this.http.get<OperationResult<ExamAttemptSummary[]>>(
      `${this.attemptsBase}/by-exam/${examId}`
    );
  }

  getAttemptDetail(attemptId: number): Observable<OperationResult<ExamAttemptGradingDetail>> {
    return this.http.get<OperationResult<ExamAttemptGradingDetail>>(
      `${this.attemptsBase}/${attemptId}`
    );
  }

  gradeEssayAnswers(attemptId: number, dto: GradeEssayAttemptPayload): Observable<OperationResult<any>> {
    return this.http.patch<OperationResult<any>>(
      `${this.attemptsBase}/${attemptId}/grade`, dto
    );
  }

  aiGradeAttempt(attemptId: number): Observable<OperationResult<AiGradeResponse>> {
    return this.http.post<OperationResult<AiGradeResponse>>(
      `${this.attemptsBase}/${attemptId}/ai-grade-suggestions`, {}
    );
  }

  delete(id: number): Observable<OperationResult<null>> {
    return this.http.delete<OperationResult<null>>(`${this.base}/${id}`);
  }

  publish(id: number): Observable<OperationResult<null>> {
    return this.http.put<OperationResult<null>>(`${this.base}/${id}/publish`, {});
  }

  publishResults(id: number): Observable<OperationResult<null>> {
    return this.http.put<OperationResult<null>>(`${this.base}/${id}/publish-results`, {});
  }

  unpublishResults(id: number): Observable<OperationResult<null>> {
    return this.http.put<OperationResult<null>>(`${this.base}/${id}/unpublish-results`, {});
  }

  getSubjects(): Observable<{ id: number; name: string }[]> {
    return this.http.get<{ id: number; name: string }[]>(`${this.base}/subjects`);
  }

  getClasses(subjectId?: number): Observable<{ id: number; name: string; gradeLevelId: number }[]> {
    let params = new HttpParams();
    if (subjectId) params = params.set('subjectId', subjectId);
    return this.http.get<{ id: number; name: string; gradeLevelId: number }[]>(`${this.base}/classes`, { params });
  }
}
