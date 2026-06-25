/**
 * Timetable Shared Models
 * =======================
 * DTOs مشتركة تعكس بالضبط ما يرجعه الـ backend بعد فك الـ OperationResult wrapper
 * بواسطة apiInterceptor.
 *
 * مصدر الحقيقة: Project.BLL/DTOs/Timetables/
 */

/* ── Slot (حصة واحدة في الجدول) ─────────────────────────── */
export interface TimetableSlotDto {
  id:                    number;
  timetableId:           number;
  dayOfWeek:             string;    // "Sunday" | "Monday" | ... (من SchoolDay enum)
  periodNumber:          number;
  startTime:             string;    // "HH:mm:ss"  من TimeOnly
  endTime:               string;    // "HH:mm:ss"
  isBreak:               boolean;
  classSubjectTeacherId: number | null;
  subjectName:           string | null;
  teacherName:           string | null;
  roomId:                number | null;
  roomName:              string | null;
}

/* ── Timetable (جدول الفصل كله) ─────────────────────────── */
export interface TimetableDto {
  id:             number;
  classId:        number;
  className:      string;
  academicYearId: number;
  isActive:       boolean;
  status?:        string;
  statusValue?:   number;
  versionNumber?: number;
  publishedAt?:   string | null;
  archivedAt?:    string | null;
  sourceTimetableId?: number | null;
  createdAt:      string;
  updatedAt:      string;
  slots:          TimetableSlotDto[];
}

/* ── Teacher slot (الحصة في جدول المعلم) ────────────────── */
export interface TeacherScheduleSlotDto {
  id:                    number;
  timetableId:           number;
  classId:               number;
  className:             string;
  dayOfWeek:             string;
  periodNumber:          number;
  startTime:             string;
  endTime:               string;
  isBreak:               boolean;
  classSubjectTeacherId: number | null;
  subjectName:           string | null;
  roomName:              string | null;
}

/* ── Child schedule (جدول ابن ولي الأمر) ───────────────── */
export interface ChildScheduleDto extends TimetableDto {
  studentId:   number;
  studentName: string;
}
