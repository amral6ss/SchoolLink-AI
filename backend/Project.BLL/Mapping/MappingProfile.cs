using AutoMapper;
using Project.BLL.DTOs;
using Project.BLL.DTOs.EvaluationTemplates;
using Project.BLL.DTOs.EvaluationPeriods;
using Project.BLL.DTOs.EvaluationItems;
using Project.BLL.DTOs.StudentEvaluations;
using Project.BLL.DTOs.DailyAbsences;
using Project.BLL.DTOs.PeriodAverages;
using Project.BLL.DTOs.PeriodicAssessments;
using Project.BLL.DTOs.FinalGrades;
using Project.Domain.Entities;

namespace Project.BLL.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Academic Year
        CreateMap<AcademicYear, AcademicYearDto>();
        CreateMap<CreateAcademicYearRequest, AcademicYear>()
            .ForMember(d => d.IsCurrent, opt => opt.Ignore());

        // Grade Level
        CreateMap<GradeLevel, GradeLevelDto>();
        CreateMap<CreateGradeLevelRequest, GradeLevel>();

        // Subject
        CreateMap<Subject, SubjectDto>();
        CreateMap<CreateSubjectRequest, Subject>();

        // SchoolClass
        CreateMap<SchoolClass, ClassDto>()
            .ForMember(d => d.GradeLevelName,
                opt => opt.MapFrom(s => s.GradeLevel.Name))
            .ForMember(d => d.AcademicYearName,
                opt => opt.MapFrom(s => s.AcademicYear.Name))
            .ForMember(d => d.Status,
                opt => opt.MapFrom(s => (int)s.Status))
            .ForMember(d => d.StatusName,
                opt => opt.MapFrom(s => s.Status.ToString()));

        // ClassSubjectTeacher
        CreateMap<ClassSubjectTeacher, ClassSubjectTeacherDto>()
            .ForMember(d => d.ClassName,
                opt => opt.MapFrom(s => s.Class.Name))
            .ForMember(d => d.SubjectName,
                opt => opt.MapFrom(s => s.Subject.Name))
            .ForMember(d => d.TeacherName,
                opt => opt.MapFrom(s => s.Teacher.FullName))
            .ForMember(d => d.AcademicYearName,
                opt => opt.MapFrom(s => s.AcademicYear.Name));

        // Timetable
        CreateMap<Timetable, TimetableDto>()
            .ForMember(d => d.ClassName,
                opt => opt.MapFrom(s => s.Class.Name))
            .ForMember(d => d.IsActive,
                opt => opt.MapFrom(s => s.Status == Project.Domain.Enums.TimetableStatus.Active))
            .ForMember(d => d.Status,
                opt => opt.MapFrom(s => s.Status.ToString()))
            .ForMember(d => d.StatusValue,
                opt => opt.MapFrom(s => (int)s.Status));
        CreateMap<Timetable, ChildScheduleDto>()
            .IncludeBase<Timetable, TimetableDto>();

        // TimetableSlot
        CreateMap<TimetableSlot, TimetableSlotDto>()
            .ForMember(d => d.DayOfWeek,
                opt => opt.MapFrom(s => s.DayOfWeek.ToString()))
            .ForMember(d => d.SubjectName,
                opt => opt.MapFrom(s => s.ClassSubjectTeacher != null
                    ? s.ClassSubjectTeacher.Subject.Name : null))
            .ForMember(d => d.TeacherName,
                opt => opt.MapFrom(s => s.ClassSubjectTeacher != null
                    ? s.ClassSubjectTeacher.Teacher.FullName : null))
            .ForMember(d => d.RoomName,
                opt => opt.MapFrom(s => s.Room != null ? s.Room.Name : null));

        // Room
        CreateMap<Room, RoomDto>()
            .ForMember(d => d.Type,
                opt => opt.MapFrom(s => s.Type));
        CreateMap<CreateRoomRequest, Room>();
        CreateMap<UpdateRoomRequest, Room>();

        // Evaluation Template
        CreateMap<EvaluationTemplate, EvaluationTemplateDto>()
            .ForMember(d => d.GradeLevelName,
                opt => opt.MapFrom(s => s.GradeLevel.Name))
            .ForMember(d => d.SubjectName,
                opt => opt.MapFrom(s => s.Subject.Name))
            .ForMember(d => d.AcademicYearName,
                opt => opt.MapFrom(s => s.AcademicYear.Name));
        CreateMap<CreateEvaluationTemplateRequest, EvaluationTemplate>();

        // Evaluation Period
        CreateMap<EvaluationPeriod, EvaluationPeriodDto>()
            .ForMember(d => d.AcademicYearName,
                opt => opt.MapFrom(s => s.AcademicYear.Name));
        CreateMap<CreateEvaluationPeriodRequest, EvaluationPeriod>();

        // Evaluation Item
        CreateMap<EvaluationItem, EvaluationItemDto>();
        CreateMap<CreateEvaluationItemRequest, EvaluationItem>();

        // Student Evaluation
        CreateMap<StudentEvaluation, StudentEvaluationDto>()
            .ForMember(d => d.ItemName,
                opt => opt.MapFrom(s => s.EvaluationItem.Name))
            .ForMember(d => d.MaxScore,
                opt => opt.MapFrom(s => s.EvaluationItem.MaxScore))
            .ForMember(d => d.SubjectName,
                opt => opt.MapFrom(s => s.EvaluationItem.Template.Subject.Name))
            .ForMember(d => d.PeriodName,
                opt => opt.MapFrom(s => s.Period.Name));

        // Daily Absence
        CreateMap<DailyAbsence, DailyAbsenceDto>()
            .ForMember(d => d.SubjectName,
                opt => opt.MapFrom(s => s.ClassSubjectTeacher != null
                    ? s.ClassSubjectTeacher.Subject.Name : null));
        CreateMap<RecordAbsenceRequest, DailyAbsence>();

        // Period Average
        CreateMap<PeriodAverage, PeriodAverageDto>()
            .ForMember(d => d.PeriodName,
                opt => opt.MapFrom(s => s.Period.Name))
            .ForMember(d => d.PeriodType,
                opt => opt.MapFrom(s => s.Period.PeriodType.ToString()));

        // Periodic Assessment
        CreateMap<PeriodicAssessment, PeriodicAssessmentDto>();
        CreateMap<RecordPeriodicAssessmentRequest, PeriodicAssessment>();

        // Final Grade
        CreateMap<FinalGrade, FinalGradeDto>()
            .ForMember(d => d.StudentName,
                opt => opt.MapFrom(s => s.Enrollment.Student.FullName))
            .ForMember(d => d.StudentId,
                opt => opt.MapFrom(s => s.Enrollment.Student.Id));

    }
}
