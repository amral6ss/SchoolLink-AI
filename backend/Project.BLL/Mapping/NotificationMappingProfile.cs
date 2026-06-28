using AutoMapper;
using Project.BLL.DTOs.Notifications;
using Project.Domain.Entities;

namespace Project.BLL.Mapping;

public class NotificationMappingProfile : Profile
{
    public NotificationMappingProfile()
    {
        // نبعت UTC (بـ Z suffix) — الـ JSON serializer بيضيف Z تلقائيًا لـ DateTimeKind.Utc
        // الـ JavaScript بيحسب الفرق صح: new Date("...Z") vs new Date() = local time
        CreateMap<Notification, NotificationDto>()
            .ForMember(d => d.CreatedAt, o => o.MapFrom(s => DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc)))
            .ForMember(d => d.ReadAt,    o => o.MapFrom(s => s.ReadAt.HasValue
                ? DateTime.SpecifyKind(s.ReadAt.Value, DateTimeKind.Utc)
                : (DateTime?)null));

        CreateMap<SendNotificationRequest, Notification>()
            .ForMember(d => d.IsRead,    o => o.Ignore())
            .ForMember(d => d.ReadAt,    o => o.Ignore())
            .ForMember(d => d.Id,        o => o.Ignore())
            .ForMember(d => d.IsDeleted, o => o.Ignore())
            .ForMember(d => d.CreatedAt, o => o.Ignore())
            .ForMember(d => d.UpdatedAt, o => o.Ignore())
            .ForMember(d => d.User,      o => o.Ignore());
    }
}

