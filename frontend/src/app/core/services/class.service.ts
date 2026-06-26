import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { buildApiUrl } from '../utils/api-url';
export interface ClassEntity {
  id: number;
  name: string;
  gradeLevelId: number;
  gradeLevelName?: string;
  academicYearId: number;
  academicYearName?: string;
  capacity?: number | null;
  status?: number;
  statusName?: string;
}

export interface CopyClassesFromYearRequest {
  sourceAcademicYearId: number;
  targetAcademicYearId: number;
}

export interface CopyClassesFromYearPreviewItem {
  sourceClassId: number;
  gradeLevelId: number;
  gradeLevelName: string;
  className: string;
  capacity?: number | null;
  status: number;
  alreadyExists: boolean;
  targetClassId?: number | null;
  action: string;
}

export interface CopyClassesFromYearResult {
  sourceAcademicYearId: number;
  targetAcademicYearId: number;
  totalSourceClasses: number;
  createdCount: number;
  skippedExistingCount: number;
  items: CopyClassesFromYearPreviewItem[];
}

@Injectable({
  providedIn: 'root'
})
export class ClassService {
  private http = inject(HttpClient);
  private apiUrl = buildApiUrl('class-management');

  getAll(filter?: any): Observable<any> {
    let params = new HttpParams();
    if (filter) {
      Object.keys(filter).forEach(key => {
        if (filter[key]) params = params.set(key, filter[key]);
      });
    }
    return this.http.get<any>(this.apiUrl, { params });
  }

  getById(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  getByGradeLevel(gradeLevelId: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/by-grade-level/${gradeLevelId}`);
  }

  getStudents(classId: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${classId}/students`);
  }

  create(data: Partial<ClassEntity>): Observable<any> {
    return this.http.post<any>(this.apiUrl, data);
  }

  createWithStudents(data: any): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/with-students`, data);
  }

  update(id: number, data: Partial<ClassEntity>): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, { ...data, id });
  }

  delete(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`);
  }

  previewCopyFromYear(data: CopyClassesFromYearRequest): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/copy-from-year/preview`, data);
  }

  copyFromYear(data: CopyClassesFromYearRequest): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/copy-from-year`, data);
  }

  getMyClassesCurrentYear(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/my-classes/current-year`);
  }
}
