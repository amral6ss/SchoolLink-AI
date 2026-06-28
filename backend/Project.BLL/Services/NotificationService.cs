using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.Common;
using Project.BLL.DTOs.Notifications;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;

namespace Project.BLL.Services;

public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly INotificationPushService _pushService;



    public NotificationService(IUnitOfWork unitOfWork, IMapper mapper, INotificationPushService pushService)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _pushService = pushService;
    }



    public async Task<OperationResult> SendNotificationAsync(SendNotificationRequest request)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId);
        if (user == null || user.IsDeleted || !user.IsActive)
            return OperationResult.Failure("المستخدم المستهدف غير موجود أو غير نشط");

        var notification = _mapper.Map<Notification>(request);
        await _unitOfWork.Notifications.AddAsync(notification);
        await _unitOfWork.SaveChangesAsync();

        // Real-time push
        var dto = _mapper.Map<NotificationDto>(notification);
        await _pushService.PushToUserAsync(request.UserId, dto);

        return OperationResult.Success("تم إرسال الإشعار بنجاح");
    }

    public async Task<OperationResult<IEnumerable<NotificationDto>>> GetNotificationsByUserAsync(int userId, bool onlyUnread)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return OperationResult<IEnumerable<NotificationDto>>.Failure("المستخدم غير موجود");

        IReadOnlyList<Notification> notifications;
        if (onlyUnread)
            notifications = await _unitOfWork.Notifications.GetUnreadByUserIdAsync(userId);
        else
            notifications = await _unitOfWork.Notifications.FindAsync(n => n.UserId == userId);

        var dtos = _mapper.Map<IEnumerable<NotificationDto>>(notifications
            .OrderByDescending(n => n.CreatedAt));

        return OperationResult<IEnumerable<NotificationDto>>.Success(dtos);
    }

    public async Task<OperationResult<NotificationDto>> GetNotificationByIdAsync(int notificationId, int userId)
    {
        var notification = await _unitOfWork.Notifications.GetByIdAsync(notificationId);
        if (notification == null || notification.UserId != userId)
            return OperationResult<NotificationDto>.Failure("الإشعار غير موجود أو لا يخص هذا المستخدم");

        var dto = _mapper.Map<NotificationDto>(notification);
        return OperationResult<NotificationDto>.Success(dto);
    }

    public async Task<OperationResult<int>> GetUnreadCountAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return OperationResult<int>.Failure("المستخدم غير موجود");

        var count = await _unitOfWork.Notifications.GetUnreadCountAsync(userId);
        return OperationResult<int>.Success(count);
    }

    public async Task<OperationResult> MarkNotificationAsReadAsync(int notificationId, int userId)
    {
        var notification = await _unitOfWork.Notifications.GetByIdAsync(notificationId);
        if (notification == null || notification.UserId != userId)
            return OperationResult.Failure("الإشعار غير موجود أو لا يخص هذا المستخدم");

        await _unitOfWork.Notifications.MarkAsReadAsync(notificationId);
        return OperationResult.Success("تم تحديد الإشعار كمقروء");
    }

    public async Task<OperationResult> MarkAllNotificationsAsReadAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return OperationResult.Failure("المستخدم غير موجود");

        await _unitOfWork.Notifications.MarkAllAsReadAsync(userId);
        return OperationResult.Success("تم تحديد جميع الإشعارات كمقروءة");
    }

    public async Task<OperationResult<PagedResult<NotificationDto>>> GetNotificationsByUserPagedAsync(int userId, PaginationFilter filter)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return OperationResult<PagedResult<NotificationDto>>.Failure("المستخدم غير موجود");

        var notifications = await _unitOfWork.Notifications.GetByUserIdPagedAsync(userId, filter.Page, filter.PageSize);
        var totalCount = await _unitOfWork.Notifications.CountAsync(n => n.UserId == userId);
        var dtos = _mapper.Map<IEnumerable<NotificationDto>>(notifications);

        var paged = new PagedResult<NotificationDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return OperationResult<PagedResult<NotificationDto>>.Success(paged);
    }

    public async Task<OperationResult> SendBulkNotificationAsync(SendBulkNotificationRequest request)
    {
        var notifications = new List<Notification>();
        var uniqueUserIds = request.UserIds.Distinct().ToList();
        foreach (var userId in uniqueUserIds)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null || user.IsDeleted || !user.IsActive)
                continue;

            notifications.Add(new Notification
            {
                UserId = userId,
                Title = request.Title,
                Body = request.Body,
                Type = request.Type,
                DataJson = request.DataJson
            });
        }

        if (notifications.Count == 0)
            return OperationResult.Failure("لا يوجد مستلمون صالحون");

        await _unitOfWork.Notifications.BulkAddAsync(notifications);
        await _unitOfWork.SaveChangesAsync();

        // Real-time push for all recipients — كل push مستقل عشان فشل واحد ما يأثرش على الباقي
        var pushExceptions = new List<Exception>();
        foreach (var n in notifications)
        {
            try
            {
                var dto = _mapper.Map<NotificationDto>(n);
                await _pushService.PushToUserAsync(n.UserId, dto);
            }
            catch (Exception ex)
            {
                pushExceptions.Add(ex);
            }
        }

        if (pushExceptions.Count > 0 && notifications.Count == pushExceptions.Count)
            return OperationResult.Failure("فشل إرسال الإشعارات عبر الإتصال المباشر، ولكن تم حفظها في قاعدة البيانات");

        return OperationResult.Success($"تم إرسال الإشعارات إلى {notifications.Count} مستخدمين");
    }

    public async Task<OperationResult> DeleteBulkNotificationsAsync(List<int> notificationIds, int userId)
    {
        foreach (var id in notificationIds)
        {
            var notification = await _unitOfWork.Notifications.GetByIdAsync(id);
            if (notification == null || notification.UserId != userId)
                continue;

            _unitOfWork.Notifications.SoftDelete(notification);
        }

        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success($"تم حذف {notificationIds.Count} إشعار");
    }

    public async Task<OperationResult> DeleteNotificationAsync(int notificationId, int userId)
    {
        var notification = await _unitOfWork.Notifications.GetByIdAsync(notificationId);
        if (notification == null || notification.UserId != userId)
            return OperationResult.Failure("الإشعار غير موجود أو لا يخص هذا المستخدم");

        _unitOfWork.Notifications.SoftDelete(notification);
        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success("تم حذف الإشعار بنجاح");
    }
}