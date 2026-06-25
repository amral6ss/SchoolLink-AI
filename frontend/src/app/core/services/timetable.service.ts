import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { buildApiUrl } from '../utils/api-url';import {
  TimetableDto,
  TimetableSlotDto,
  TeacherScheduleSlotDto,
  ChildScheduleDto,
} from '../models/timetable.models';

/* ── Admin / Shared DTOs ────────────────────────────────── */
export interface TimetableListItem {
  id:             number;
  classId:        number | string;
  className?:     string;
  academicYearId: number | string;
  isActive?:      boolean;
  status?:        string;
  statusValue?:   number;
  versionNumber?: number;
  publishedAt?:   string | null;
  archivedAt?:    string | null;
  sourceTimetableId?: number | null;
  createdAt?:     string;
  updatedAt?:     string;
  slots?:         TimetableSlotDto[];
}

export interface TimetableValidationIssue {
  severity:               string;
  code:                   string;
  message:                string;
  slotId?:                number | null;
  dayOfWeek?:             string | null;
  periodNumber?:          number | null;
  classSubjectTeacherId?: number | null;
}

export interface TimetableValidationResult {
  timetableId:                     number;
  canActivate:                     boolean;
  totalSlots:                      number;
  lessonSlots:                     number;
  breakSlots:                      number;
  missingAssignmentsCount:         number;
  overScheduledAssignmentsCount:   number;
  errors:                          TimetableValidationIssue[];
  warnings:                        TimetableValidationIssue[];
}

/* ── Re-export shared types so consumers import from one place ── */
export type { TimetableDto, TimetableSlotDto, TeacherScheduleSlotDto, ChildScheduleDto };

@Injectable({
  providedIn: 'root'
})
export class TimetableService {
  private http   = inject(HttpClient);
  private apiUrl = buildApiUrl('timetables');

  /* ── Admin: CRUD ──────────────────────────────────────── */

  getAll(): Observable<any> {
    return this.http.get<any>(this.apiUrl);
  }

  getByClass(classId: number, academicYearId: number): Observable<any> {
    const params = new HttpParams()
      .set('classId',        classId.toString())
      .set('academicYearId', academicYearId.toString());
    return this.http.get<any>(`${this.apiUrl}/by-class`, { params });
  }

  getById(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  getActiveByClass(classId: number, academicYearId: number): Observable<any> {
    const params = new HttpParams()
      .set('classId',        classId.toString())
      .set('academicYearId', academicYearId.toString());
    return this.http.get<any>(`${this.apiUrl}/active/by-class`, { params });
  }

  create(data: Partial<TimetableListItem>): Observable<any> {
    return this.http.post<any>(this.apiUrl, data);
  }

  cloneDraft(classId: number, academicYearId: number, replaceExisting: boolean = false): Observable<any> {
    const params = new HttpParams()
      .set('classId',        classId.toString())
      .set('academicYearId', academicYearId.toString())
      .set('replaceExisting', replaceExisting.toString());
    return this.http.post<any>(`${this.apiUrl}/clone-draft`, {}, { params });
  }

  validate(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}/validate`);
  }

  activate(id: number): Observable<any> {
    return this.http.patch<any>(`${this.apiUrl}/${id}/activate`, {});
  }

  deactivate(id: number): Observable<any> {
    return this.http.patch<any>(`${this.apiUrl}/${id}/deactivate`, {});
  }

  update(id: number, data: Partial<TimetableListItem>): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, data);
  }

  delete(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`);
  }

  /* ── Admin: Slots ─────────────────────────────────────── */

  addSlot(data: unknown): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/slots`, data);
  }

  updateSlot(id: number, data: unknown): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/slots/${id}`, data);
  }

  deleteSlot(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/slots/${id}`);
  }

  /**
   * تحديث توقيت كل الحصص برقم حصة معيّن داخل الجدول دفعة واحدة (batch).
   * يُستخدم من رأس الجدول الموحّد لتطبيق التوقيت على عمود كامل (كل الأيام).
   */
  updatePeriodTiming(
    timetableId: number,
    periodNumber: number,
    startTime: string,
    endTime: string
  ): Observable<any> {
    return this.http.put<any>(
      `${this.apiUrl}/${timetableId}/period-time`,
      { periodNumber, startTime, endTime }
    );
  }

  /* ── Teacher ──────────────────────────────────────────── */

  getTeacherSchedule(teacherId: number, academicYearId: number): Observable<any> {
    const params = new HttpParams().set('academicYearId', academicYearId.toString());
    return this.http.get<any>(
      `${this.apiUrl}/teacher-schedule/${teacherId}`, { params }
    );
  }

  /** جدول المعلم الحالي (السنة الدراسية الحالية تلقائياً) */
  getMyScheduleCurrentYear(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/my-schedule/current-year`);
  }

  /* ── Student ──────────────────────────────────────────── */

  /** جدول فصل الطالب للسنة الدراسية الحالية */
  getMyStudentScheduleCurrentYear(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/my-student-schedule/current-year`);
  }

  /* ── Parent ───────────────────────────────────────────── */

  /** جداول جميع الأبناء للسنة الدراسية الحالية */
  getMyChildSchedulesCurrentYear(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/my-child-schedules/current-year`);
  }
}
