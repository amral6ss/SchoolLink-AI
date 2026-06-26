import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map } from 'rxjs';
import { buildApiUrl } from '../utils/api-url';

export interface SchoolProfile {
  id: number;
  schoolName: string;
  governorate: string;
  directorate: string;
  educationalAdministration: string;
  address?: string;
  phone?: string;
  email?: string;
  managerName?: string;
  logoPath?: string;
  isActive: boolean;
}

interface OperationResult<T> {
  isSuccess: boolean;
  data: T;
}

@Injectable({ providedIn: 'root' })
export class SchoolProfileService {
  private http = inject(HttpClient);
  private base = buildApiUrl('schoolprofile');

  get() {
    return this.http.get<OperationResult<SchoolProfile>>(this.base).pipe(
      map(res => res.data)
    );
  }

  update(data: Partial<SchoolProfile>) {
    return this.http.put<OperationResult<SchoolProfile>>(this.base, data).pipe(
      map(res => res.data)
    );
  }
}
