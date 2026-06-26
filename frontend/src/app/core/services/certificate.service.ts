import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { buildApiUrl } from '../utils/api-url';

export interface CertificateSubject {
  id?: number;
  certificateId?: number;
  subjectName: string;
  maxScore: number;
  minScore: number;
  isCountedInTotal: boolean;
  sortOrder: number;
}

export interface Certificate {
  id: number;
  name: string;
  gradeLevel: string;
  term: string;
  examRole: string;
  year: string;
  subjects: CertificateSubject[];
}

@Injectable({ providedIn: 'root' })
export class CertificateService {
  private http = inject(HttpClient);
  private apiUrl = buildApiUrl('certificates');

  getAll(): Observable<any> {
    return this.http.get<any>(this.apiUrl);
  }

  getById(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  create(data: Partial<Certificate>): Observable<any> {
    return this.http.post<any>(this.apiUrl, data);
  }

  update(id: number, data: Partial<Certificate>): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, { ...data, id });
  }

  delete(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`);
  }

  /** Generate certificates for one or more classes. classIds is a comma-separated string. */
  generate(certId: number, classIds: string, term: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${certId}/generate`, {
      params: { classIds, term: term.toString() }
    });
  }

  /** Grade sheet for one or more classes. classIds is a comma-separated string. */
  gradeSheet(certId: number, classIds: string, term: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${certId}/grade-sheet`, {
      params: { classIds, term: term.toString() }
    });
  }

  /** Honor roll (أوائل الطلاب) for one or more classes. */
  honorRoll(certId: number, classIds: string, term: number, top: number = 10): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${certId}/honor-roll`, {
      params: { classIds, term: term.toString(), top: top.toString() }
    });
  }
}
