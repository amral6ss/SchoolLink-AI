import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map } from 'rxjs';
import { buildApiUrl } from '../../core/utils/api-url';

export interface ChildProgressItem {
  studentId: number;
  studentName: string;
  className: string;
  gradeLevelName: string;
  avgScore: number;
  attendancePercentage: number;
  assignments: AssignmentProgress[];
  exams: ExamProgress[];
}

export interface AssignmentProgress {
  id: number;
  subject: string;
  title: string;
  deadline?: string;
  status: string;
  score?: number;
  maxScore: number;
}

export interface ExamProgress {
  id: number;
  subject: string;
  title: string;
  date?: string;
  status: string;
  score?: number;
  maxScore: number;
}

export interface ChildExamAttemptResult {
  examId: number;
  subject: string;
  title: string;
  studentId: number;
  studentName: string;
  score?: number;
  maxScore: number;
  status: string;
  message: string;
  answers: ChildExamAnswer[];
}

export interface ChildExamAnswer {
  questionId: number;
  questionText: string;
  answerText?: string;
  correctAnswerText?: string;
  isCorrect?: boolean;
  pointsEarned: number;
  questionPoints: number;
  aIFeedback?: string;
}

interface OperationResult<T> {
  isSuccess: boolean;
  data: T;
}

@Injectable({ providedIn: 'root' })
export class ChildProgressService {
  private http = inject(HttpClient);
  private base = buildApiUrl('child-progress');

  get(term?: number | null) {
    let url = this.base;
    if (term != null) {
      url += `?term=${term}`;
    }
    return this.http.get<OperationResult<ChildProgressItem[]>>(url).pipe(
      map(res => res.data)
    );
  }

  getExamAttempt(examId: number) {
    return this.http.get<OperationResult<ChildExamAttemptResult>>(`${this.base}/exam-attempt/${examId}`).pipe(
      map(res => res.data)
    );
  }
}
