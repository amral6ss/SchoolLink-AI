import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { buildApiUrl } from '../utils/api-url';
import { map } from 'rxjs/operators';

export interface Student {
  id: number;
  fullName: string;
  nationalId?: string | null;
  gender?: number | null;
  birthDate?: string | null;
  userId?: number | null;
  userName?: string | null;
  userEmail?: string | null;
  isActive: boolean;
  lifecycleStatus?: number;
  lifecycleStatusName?: string;
  createdAt?: string;
}

export interface CreateStudentRequest {
  fullName: string;
  nationalId?: string;
  gender?: number | null;
  birthDate?: string | null;
}

export interface UpdateStudentRequest {
  id: number;
  fullName: string;
  gender?: number | null;
  birthDate?: string | null;
}

export interface StudentSearchFilter {
  searchTerm?: string;
  classId?: number;
  academicYearId?: number;
  isActive?: boolean;
}

export interface LinkStudentUserRequest {
  studentId: number;
  userId: number;
}

@Injectable({
  providedIn: 'root'
})
export class StudentService {
  private http = inject(HttpClient);
  private apiUrl = buildApiUrl('students');

  getAll(): Observable<any> {
    return this.http.get<any>(this.apiUrl);
  }

  search(filter: StudentSearchFilter): Observable<any> {
    let params = new HttpParams();
    if (filter.searchTerm) params = params.set('searchTerm', filter.searchTerm);
    if (filter.classId) params = params.set('classId', filter.classId);
    if (filter.academicYearId) params = params.set('academicYearId', filter.academicYearId);
    if (filter.isActive !== undefined) params = params.set('isActive', filter.isActive);

    return this.http.get<any>(`${this.apiUrl}/search`, { params });
  }

  create(data: CreateStudentRequest): Observable<any> {
    return this.http.post<any>(this.apiUrl, data);
  }

  update(id: number, data: UpdateStudentRequest): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, data);
  }

  delete(id: number): Observable<any> {
    return this.http
      .delete<any>(`${this.apiUrl}/${id}`)
      .pipe(map(() => void 0));
  }

  getByUserId(userId: number): Observable<any> {
    return this.http.get(`${this.apiUrl}/by-user/${userId}`);
  }

  linkUser(data: LinkStudentUserRequest): Observable<any> {
    return this.http
      .post<any>(`${this.apiUrl}/link-user`, data)
      .pipe(map(() => void 0));
  }
}
