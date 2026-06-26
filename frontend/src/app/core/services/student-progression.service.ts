import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { buildApiUrl } from '../utils/api-url';

// الـ backend بيرجع enums كنص (JsonStringEnumConverter في Program.cs)
// فلازم نقارنها بـ string مش رقم.
export type AcademicStatus = 'NoGrades' | 'Unpublished' | 'Passed' | 'Failed';
export type StudentLifecycleStatus = 'Active' | 'Graduated' | 'Transferred' | 'Withdrawn';
export type AcademicTermLabel = 'FirstSemester' | 'SecondSemester';
// ProgressionTermScope ده query param بيُبعت رقم للـ backend، فسيبناها رقم.
export type ProgressionTermScope = 1 | 2 | 3; // First=1, Second=2, Both=3

export interface SubjectGrade {
  subjectId: number | null;
  subjectName: string;
  percentage: number;
  isPublished: boolean;
  isPassed: boolean;
  // الـ backend بيرجع term كنص ("FirstSemester"/"SecondSemester") بسبب JsonStringEnumConverter
  term?: AcademicTermLabel | null;
}

export interface StudentProgressionCandidate {
  enrollmentId: number;
  studentId: number;
  studentName: string;
  currentClassId: number;
  currentClassName: string;
  currentGradeLevelId: number;
  currentGradeLevelName: string;
  academicYearId: number;
  academicYearName: string;
  studentIsActive: boolean;
  studentLifecycleStatus: StudentLifecycleStatus;
  studentLifecycleStatusName: string;
  hasStudentAccount: boolean;
  hasFinalGrade: boolean;
  finalTotal?: number | null;
  hasPublishedFinalGrade: boolean;
  academicStatus: AcademicStatus;
  passedSubjectsCount: number;
  failedSubjectsCount: number;
  subjectGrades: SubjectGrade[];
}

export interface StudentProgressionRequest {
  enrollmentIds: number[];
  action: 1 | 2 | 3;
  targetClassId?: number | null;
  targetAcademicYearId?: number | null;
  classMappings?: StudentProgressionClassMapping[];
  effectiveDate: string;
  passingThreshold?: number;
  note?: string;
}

export interface StudentProgressionClassMapping {
  sourceClassId: number;
  targetClassId: number;
}

export interface StudentProgressionFailure {
  enrollmentId: number;
  studentId: number;
  studentName: string;
  reason: string;
}

export interface StudentProgressionResult {
  totalRequested: number;
  successCount: number;
  promotedCount: number;
  retainedCount: number;
  graduatedCount: number;
  failureCount: number;
  failures: StudentProgressionFailure[];
}

@Injectable({
  providedIn: 'root'
})
export class StudentProgressionService {
  private http = inject(HttpClient);
  private apiUrl = buildApiUrl('student-progression');

  getCandidates(
    gradeLevelId: number,
    academicYearId: number,
    termScope: ProgressionTermScope = 3,
    passingThreshold: number = 50
  ): Observable<any> {
    const params = new HttpParams()
      .set('gradeLevelId', gradeLevelId)
      .set('academicYearId', academicYearId)
      .set('termScope', termScope)
      .set('passingThreshold', passingThreshold);

    return this.http.get<any>(`${this.apiUrl}/candidates`, { params });
  }

  execute(request: StudentProgressionRequest): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/execute`, request);
  }
}
