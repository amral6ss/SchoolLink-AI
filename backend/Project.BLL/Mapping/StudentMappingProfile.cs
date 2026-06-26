using AutoMapper;
using Project.BLL.DTOs.Students;
using Project.Domain.Entities;

namespace Project.BLL.Mapping;

public class StudentMappingProfile : Profile
{
    public StudentMappingProfile()
    {
        CreateMap<Student, StudentDto>()
            .ForMember(d => d.UserName, o => o.MapFrom(s => s.User != null ? s.User.FullName : null))
            .ForMember(d => d.UserEmail, o => o.MapFrom(s => s.User != null ? s.User.ContactEmail : null))
            .ForMember(d => d.LifecycleStatusName, o => o.MapFrom(s => s.LifecycleStatus.ToString()));

        CreateMap<CreateStudentRequest, Student>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.UserId, o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.User, o => o.Ignore())
            .ForMember(d => d.Enrollments, o => o.Ignore())
            .ForMember(d => d.Parents, o => o.Ignore())
            .ForMember(d => d.IsActive, o => o.MapFrom(_ => true))
            .ForMember(d => d.LifecycleStatus, o => o.MapFrom(_ => Project.Domain.Enums.StudentLifecycleStatus.Active));
    }
}
