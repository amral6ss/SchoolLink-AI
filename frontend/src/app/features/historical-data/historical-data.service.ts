import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { buildApiUrl } from '../../core/utils/api-url';

export interface HistoricalYear {
  id: number;
  name: string;
  startDate: string;
  endDate: string;
  isCurrent: boolean;
}

export interface HistoricalClass {
  id: number;
  name: string;
  gradeLevelName?: string;
  studentCount: number;
}

export interface HistoricalStudent {
  id: number;
  fullName: string;
  enrollmentId: number;
}

export interface HistoricalFinalGrade {
  id: number;
  enrollmentId: number;
  subjectId?: number;
  subjectName?: string;
  studentId: number;
  studentName?: string;
  className?: string;
  academicTerm: number;
  periodAvgScore: number;
  assessment1Score: number;
  assessment2Score: number;
  writtenTotal: number;
  finalExamScore: number;
  total: number;
  maxTotal: number;
  isPublished: boolean;
  isComplete: boolean;
  percentage: number;
}

export interface HistoricalEvaluation {
  enrollmentId: number;
  studentName?: string;
  itemName?: string;
  periodName?: string;
  score?: number;
  maxScore: number;
}

export interface HistoricalAssessment {
  id: number;
  enrollmentId: number;
  studentName?: string;
  subjectName?: string;
  assessmentType: string;
  score: number;
  maxScore: number;
  term?: number;
}

export interface HistoricalExam {
  examId: number;
  examTitle?: string;
  subjectName?: string;
  score?: number;
  totalScore?: number;
  percentage?: number;
  isCompleted: boolean;
}

export interface HistoricalAssignment {
  assignmentId: number;
  assignmentTitle?: string;
  subjectName?: string;
  score?: number;
  maxScore?: number;
  percentage?: number;
  isGraded: boolean;
}

export interface HistoricalAbsence {
  enrollmentId: number;
  studentName?: string;
  subjectName?: string;
  absenceDate: string;
  isAbsent: boolean;
  reason?: string;
}

export interface HistoricalStudentSummary {
  studentId: number;
  studentName: string;
  className?: string;
  gradeLevelName?: string;
  finalGrades: HistoricalFinalGrade[];
  evaluations: HistoricalEvaluation[];
  assessments: HistoricalAssessment[];
  exams: HistoricalExam[];
  assignments: HistoricalAssignment[];
  absences: HistoricalAbsence[];
}

export interface HistoricalDataOverview {
  totalStudents: number;
  totalClasses: number;
  totalFinalGrades: number;
  classAverage?: number;
}

@Injectable({ providedIn: 'root' })
export class HistoricalDataService {
  private http = inject(HttpClient);
  private apiUrl = buildApiUrl('historical-data');

  getYears() {
    return this.http.get<any>(`${this.apiUrl}/years`);
  }

  getClasses(academicYearId: number) {
    return this.http.get<any>(`${this.apiUrl}/classes`, {
      params: { academicYearId }
    });
  }

  getStudents(classId: number) {
    return this.http.get<any>(`${this.apiUrl}/students`, {
      params: { classId }
    });
  }

  getStudentsByYear(academicYearId: number) {
    return this.http.get<any>(`${this.apiUrl}/students/by-year`, {
      params: { academicYearId }
    });
  }

  getOverview(classId: number, term?: number) {
    const params: any = { classId };
    if (term) params.term = term;
    return this.http.get<any>(`${this.apiUrl}/overview`, { params });
  }

  getFinalGrades(classId: number, term?: number, subjectId?: number) {
    const params: any = { classId };
    if (term) params.term = term;
    if (subjectId) params.subjectId = subjectId;
    return this.http.get<any>(`${this.apiUrl}/final-grades`, { params });
  }

  getEvaluations(classId: number, periodId?: number) {
    const params: any = { classId };
    if (periodId) params.periodId = periodId;
    return this.http.get<any>(`${this.apiUrl}/evaluations`, { params });
  }

  getAssessments(classId: number, subjectId?: number, term?: number) {
    const params: any = { classId };
    if (subjectId) params.subjectId = subjectId;
    if (term) params.term = term;
    return this.http.get<any>(`${this.apiUrl}/assessments`, { params });
  }

  getExams(enrollmentId: number) {
    return this.http.get<any>(`${this.apiUrl}/exams`, {
      params: { enrollmentId }
    });
  }

  getAssignments(enrollmentId: number) {
    return this.http.get<any>(`${this.apiUrl}/assignments`, {
      params: { enrollmentId }
    });
  }

  getAbsences(classId: number, subjectId?: number) {
    const params: any = { classId };
    if (subjectId) params.subjectId = subjectId;
    return this.http.get<any>(`${this.apiUrl}/absences`, { params });
  }

  getStudentSummary(studentId: number, academicYearId: number) {
    return this.http.get<any>(`${this.apiUrl}/student-summary`, {
      params: { studentId, academicYearId }
    });
  }
}
