using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.Announcements;
using Project.BLL.DTOs.Common;
using Project.BLL.DTOs.Notifications;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class AnnouncementService : IAnnouncementService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly INotificationService _notificationService;
    private readonly INotificationPushService _pushService;

    public AnnouncementService(IUnitOfWork unitOfWork, IMapper mapper, INotificationService notificationService, INotificationPushService pushService)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _notificationService = notificationService;
        _pushService = pushService;
    }

    public async Task<OperationResult<AnnouncementDto>> CreateAnnouncementAsync(CreateAnnouncementRequest request)
    {
        var author = await _unitOfWork.Users.GetByIdAsync(request.AuthorId);
        if (author == null || author.IsDeleted)
            return OperationResult<AnnouncementDto>.Failure("Author not found");

        if (!author.Role.IsAdminLike() &&
            author.Role != Project.Domain.Enums.UserRole.Teacher)
            return OperationResult<AnnouncementDto>.Failure("Only Admins and Teachers can create announcements");

        if (request.ExpiresAt.HasValue && request.ExpiresAt <= DateTime.UtcNow)
            return OperationResult<AnnouncementDto>.Failure("Expiry date must be in the future");

        var announcement = _mapper.Map<Announcement>(request);
        await _unitOfWork.Announcements.AddAsync(announcement);
        await _unitOfWork.SaveChangesAsync();

        // send notification + save AnnouncementUser records
        var notifType = request.Category switch
        {
            Domain.Enums.AnnouncementType.Event => NotificationType.SchoolEvent,
            Domain.Enums.AnnouncementType.Holiday => NotificationType.Holiday,
            Domain.Enums.AnnouncementType.Emergency => NotificationType.EmergencyAlert,
            _ => NotificationType.Announcement
        };
        await SendAnnouncementNotificationAsync(announcement.Id, notifType, request);

        var dto = _mapper.Map<AnnouncementDto>(announcement);
        dto.AuthorName = author.FullName;

        return OperationResult<AnnouncementDto>.Success(dto, "Announcement created successfully");
    }

    public async Task<OperationResult<AnnouncementDto>> GetAnnouncementByIdAsync(int id)
    {
        var announcement = await _unitOfWork.Announcements.GetByIdAsync(id);
        if (announcement == null || announcement.IsDeleted)
            return OperationResult<AnnouncementDto>.Failure($"Announcement with id {id} not found");

        var dto = _mapper.Map<AnnouncementDto>(announcement);
        dto.TargetedUserCount = await _unitOfWork.AnnouncementUsers
            .CountAsync(au => au.AnnouncementId == id);
        return OperationResult<AnnouncementDto>.Success(dto, "Announcement retrieved successfully");
    }

    public async Task<OperationResult<AnnouncementDto>> UpdateAnnouncementAsync(int id, CreateAnnouncementRequest request)
    {
        var announcement = await _unitOfWork.Announcements.GetByIdAsync(id);
        if (announcement == null || announcement.IsDeleted)
            return OperationResult<AnnouncementDto>.Failure($"Announcement with id {id} not found");

        var caller = await _unitOfWork.Users.GetByIdAsync(request.AuthorId);
        if (caller == null || caller.IsDeleted)
            return OperationResult<AnnouncementDto>.Failure("Caller not found");

        if (!caller.Role.IsAdminLike() &&
            caller.Role != Project.Domain.Enums.UserRole.Teacher)
            return OperationResult<AnnouncementDto>.Failure("Only Admins and Teachers can update announcements");

        if (request.ExpiresAt.HasValue && request.ExpiresAt <= DateTime.UtcNow)
            return OperationResult<AnnouncementDto>.Failure("Expiry date must be in the future");

        announcement.Title = request.Title;
        announcement.Body = request.Body;
        announcement.TargetRole = request.TargetRole;
        announcement.TargetClassId = request.TargetClassId;
        announcement.Category = request.Category;
        announcement.TargetGradeLevelId = request.TargetGradeLevelId;
        announcement.IsForAllUsers = request.IsForAllUsers;
        announcement.IsForAllStudents = request.IsForAllStudents;
        announcement.IsForAllParents = request.IsForAllParents;
        announcement.IsForAllTeachers = request.IsForAllTeachers;
        announcement.ExpiresAt = request.ExpiresAt;
        announcement.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Announcements.Update(announcement);
        await _unitOfWork.SaveChangesAsync();

        // تحديث الإشعارات الموجودة بدل إنشاء إشعارات جديدة (عشان منعملش duplicate)
        var announcementRef = $@"""referenceId"":{id}";
        var existingNotifications = (await _unitOfWork.Notifications
            .FindAsync(n => n.DataJson != null && n.DataJson.Contains(announcementRef))).ToList();

        if (existingNotifications.Any())
        {
            foreach (var n in existingNotifications)
            {
                n.Title = request.Title;
                n.Body = request.Body;
                n.IsRead = false;
                n.ReadAt = null;
                _unitOfWork.Notifications.Update(n);
            }

            // Push التحديث لكل المستخدمين في الوقت الفعلي
            foreach (var notifDto in existingNotifications.Select(n => _mapper.Map<NotificationDto>(n)))
            {
                await _pushService.PushToUserAsync(notifDto.UserId, notifDto);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        var dto = _mapper.Map<AnnouncementDto>(announcement);
        dto.AuthorName = caller.FullName;

        return OperationResult<AnnouncementDto>.Success(dto, "Announcement updated successfully");
    }

    public async Task<OperationResult> DeleteAnnouncementAsync(int id, int callerUserId)
    {
        var announcement = await _unitOfWork.Announcements.GetByIdAsync(id);
        if (announcement == null || announcement.IsDeleted)
            return OperationResult.Failure($"Announcement with id {id} not found");

        var caller = await _unitOfWork.Users.GetByIdAsync(callerUserId);
        if (caller == null || caller.IsDeleted)
            return OperationResult.Failure("Caller not found");

        if (!caller.Role.IsAdminLike() && announcement.AuthorId != callerUserId)
            return OperationResult.Failure("Only the author or an Admin can delete this announcement");

        // مسح كل AnnouncementUser records عشان الإعلان يختفي من عند كل المستخدمين
        var announcementUsers = await _unitOfWork.AnnouncementUsers
            .FindAsync(au => au.AnnouncementId == id);
        if (announcementUsers.Any())
        {
            _unitOfWork.AnnouncementUsers.SoftDeleteRange(announcementUsers);
        }

        _unitOfWork.Announcements.SoftDelete(announcement);
        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success("Announcement deleted successfully");
    }

    public async Task<OperationResult<IEnumerable<AnnouncementDto>>> GetExpiredAnnouncementsAsync(int callerUserId)
    {
        var caller = await _unitOfWork.Users.GetByIdAsync(callerUserId);
        if (caller == null)
            return OperationResult<IEnumerable<AnnouncementDto>>.Failure("Caller not found");

        // تنظيف تلقائي: نحذف الـ AnnouncementUser records للإعلانات المنتهية + نسوفت ديلت للإعلان
        await CleanupExpiredAnnouncementsAsync(callerUserId);

        var announcements = await _unitOfWork.Announcements.GetExpiredAsync();
        var dtos = _mapper.Map<IEnumerable<AnnouncementDto>>(announcements.OrderByDescending(a => a.CreatedAt));
        await PopulateTargetedCounts(dtos);
        return OperationResult<IEnumerable<AnnouncementDto>>.Success(dtos);
    }

    public async Task<OperationResult> CleanupExpiredAnnouncementsAsync(int callerUserId)
    {
        var expired = await _unitOfWork.Announcements.GetExpiredAsync();
        foreach (var announcement in expired)
        {
            // 1. امسح AnnouncementUser records عشان الإعلان يختفي من المستخدمين
            var announcementUsers = await _unitOfWork.AnnouncementUsers
                .FindAsync(au => au.AnnouncementId == announcement.Id);
            if (announcementUsers.Any())
            {
                _unitOfWork.AnnouncementUsers.SoftDeleteRange(announcementUsers);
            }

            // 2. soft delete الإعلان نفسه
            if (!announcement.IsDeleted)
            {
                _unitOfWork.Announcements.SoftDelete(announcement);
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success($"تم تنظيف {expired.Count} إعلان منتهي");
    }

    public async Task<OperationResult<IEnumerable<AnnouncementDto>>> GetActiveAnnouncementsAsync(GetAnnouncementsFilter filter)
    {
        var caller = await _unitOfWork.Users.GetByIdAsync(filter.CallerUserId);
        if (caller == null)
            return OperationResult<IEnumerable<AnnouncementDto>>.Failure("Caller not found");

        // تنظيف تلقائي: نحذف الإعلانات المنتهية
        await CleanupExpiredAnnouncementsAsync(filter.CallerUserId);

        var announcements = await _unitOfWork.Announcements.GetActiveAsync();

        var filtered = announcements.AsEnumerable();

        // if the caller has a specific role, filter by it
        if (filter.CallerRole == UserRole.Student ||
            filter.CallerRole == UserRole.Parent ||
            filter.CallerRole == UserRole.Teacher)
        {
            filtered = filtered.Where(a =>
                a.IsForAllUsers ||
                a.TargetRole == null ||
                a.TargetRole == filter.CallerRole);

            if (filter.CallerRole == UserRole.Student)
                filtered = filtered.Where(a => a.IsForAllStudents || a.IsForAllUsers || a.TargetRole == UserRole.Student);

            if (filter.CallerRole == UserRole.Parent)
                filtered = filtered.Where(a => a.IsForAllParents || a.IsForAllUsers || a.TargetRole == UserRole.Parent);

            if (filter.CallerRole == UserRole.Teacher)
                filtered = filtered.Where(a => a.IsForAllTeachers || a.IsForAllUsers || a.TargetRole == UserRole.Teacher);
        }

        if (filter.ClassId.HasValue)
        {
            filtered = filtered.Where(a =>
                a.TargetClassId == null || a.TargetClassId == filter.ClassId.Value);
        }

        var dtos = _mapper.Map<IEnumerable<AnnouncementDto>>(filtered
            .OrderByDescending(a => a.CreatedAt));
        await PopulateTargetedCounts(dtos);

        return OperationResult<IEnumerable<AnnouncementDto>>.Success(dtos);
    }

    public async Task<OperationResult<IEnumerable<AnnouncementDto>>> SearchAnnouncementsAsync(string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
            return OperationResult<IEnumerable<AnnouncementDto>>.Failure("Search term must be at least 2 characters");

        var all = await _unitOfWork.Announcements.GetActiveAsync();
        var termLower = term.ToLower();
        var matches = all.Where(a => a.Title.ToLower().Contains(termLower) ||
                                     (a.Body != null && a.Body.ToLower().Contains(termLower)));

        var dtos = _mapper.Map<IEnumerable<AnnouncementDto>>(matches.OrderByDescending(a => a.CreatedAt));
        await PopulateTargetedCounts(dtos);
        return OperationResult<IEnumerable<AnnouncementDto>>.Success(dtos);
    }

    public async Task<OperationResult<PagedResult<AnnouncementDto>>> GetAnnouncementsByAuthorAsync(int authorId, PaginationFilter filter)
    {
        var author = await _unitOfWork.Users.GetByIdAsync(authorId);
        if (author == null)
            return OperationResult<PagedResult<AnnouncementDto>>.Failure("Author not found");

        var announcements = await _unitOfWork.Announcements.GetByAuthorIdAsync(authorId);
        var filtered = announcements.Where(a => !a.IsDeleted).OrderByDescending(a => a.CreatedAt).ToList();

        var totalCount = filtered.Count;
        var paged = filtered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList();
        var dtos = _mapper.Map<IEnumerable<AnnouncementDto>>(paged);
        await PopulateTargetedCounts(dtos);
        return OperationResult<PagedResult<AnnouncementDto>>.Success(new PagedResult<AnnouncementDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        });
    }

    private async Task PopulateTargetedCounts(IEnumerable<AnnouncementDto> dtos)
    {
        foreach (var dto in dtos)
        {
            dto.TargetedUserCount = await _unitOfWork.AnnouncementUsers
                .CountAsync(au => au.AnnouncementId == dto.Id && !au.IsDeleted);
        }
    }

    private async Task SendAnnouncementNotificationAsync(int announcementId, NotificationType type, CreateAnnouncementRequest request)
    {
        var recipients = new List<int>();

        if (request.IsForAllUsers || request.IsForAllStudents || request.IsForAllParents || request.IsForAllTeachers)
        {
            if (request.IsForAllUsers)
            {
                var allUsers = await _unitOfWork.Users.FindAsync(u => u.IsActive && !u.IsDeleted);
                recipients.AddRange(allUsers.Select(u => u.Id));
            }
            else
            {
                if (request.IsForAllStudents)
                {
                    var students = await _unitOfWork.Students.FindAsync(s => s.IsActive && !s.IsDeleted
                        && s.LifecycleStatus == StudentLifecycleStatus.Active);
                    recipients.AddRange(students.Where(s => s.UserId != null).Select(s => s.UserId!.Value));
                }
                if (request.IsForAllParents)
                {
                    var parentLinks = await _unitOfWork.ParentStudents.FindAsync(ps => ps.IsDeleted == false);
                    recipients.AddRange(parentLinks.Select(ps => ps.ParentId));
                }
                if (request.IsForAllTeachers)
                {
                    var teachers = await _unitOfWork.Users.FindAsync(u =>
                        u.Role == UserRole.Teacher && u.IsActive && !u.IsDeleted);
                    recipients.AddRange(teachers.Select(t => t.Id));
                }
            }
        }
        else if (request.TargetClassId.HasValue)
        {
            var classEntity = await _unitOfWork.Classes.GetByIdAsync(request.TargetClassId.Value);
            if (classEntity != null)
            {
                var enrollments = await _unitOfWork.StudentEnrollments
                    .GetActiveByClassAsync(request.TargetClassId.Value, classEntity.AcademicYearId);
                foreach (var enrollment in enrollments)
                {
                    var student = await _unitOfWork.Students.GetByIdAsync(enrollment.StudentId);
                    if (student?.UserId != null)
                        recipients.Add(student.UserId.Value);
                }
            }
        }
        else if (request.TargetGradeLevelId.HasValue)
        {
            var classes = await _unitOfWork.Classes.FindAsync(c =>
                c.GradeLevelId == request.TargetGradeLevelId && !c.IsDeleted);
            foreach (var c in classes)
            {
                var enrollments = await _unitOfWork.StudentEnrollments
                    .GetActiveByClassAsync(c.Id, c.AcademicYearId);
                foreach (var enrollment in enrollments)
                {
                    var student = await _unitOfWork.Students.GetByIdAsync(enrollment.StudentId);
                    if (student?.UserId != null)
                        recipients.Add(student.UserId.Value);
                }
            }
        }

        var uniqueRecipients = recipients.Distinct().ToList();
        if (uniqueRecipients.Count == 0) return;

        // Send notifications — نربط الإشعار بالإعلان عشان نقدر نحدثه بعدين
        await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
        {
            UserIds = uniqueRecipients,
            Title = request.Title,
            Body = request.Body,
            Type = type,
            DataJson = System.Text.Json.JsonSerializer.Serialize(new { referenceType = "announcement", referenceId = announcementId })
        });

        // Save AnnouncementUser records to track who was targeted
        var announcementUsers = uniqueRecipients.Select(userId => new AnnouncementUser
        {
            AnnouncementId = announcementId,
            UserId = userId
        }).ToList();

        await _unitOfWork.AnnouncementUsers.AddRangeAsync(announcementUsers);
        await _unitOfWork.SaveChangesAsync();
    }
}
