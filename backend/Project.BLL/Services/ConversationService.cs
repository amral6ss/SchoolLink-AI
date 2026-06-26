using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.Common;
using Project.BLL.DTOs.Conversations;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;
using Project.BLL.DTOs.Notifications;

namespace Project.BLL.Services;

public class ConversationService : IConversationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly INotificationService _notificationService;

    public ConversationService(IUnitOfWork unitOfWork, IMapper mapper, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _notificationService = notificationService;
    }

    public async Task<OperationResult<ConversationDto>> CreateDirectConversationAsync(CreateDirectConversationRequest request)
    {
        var initiator = await _unitOfWork.Users.GetByIdAsync(request.InitiatorUserId);
        if (initiator == null || initiator.IsDeleted || !initiator.IsActive)
            return OperationResult<ConversationDto>.Failure("المستخدم البادئ غير موجود أو غير نشط");

        var target = await _unitOfWork.Users.GetByIdAsync(request.TargetUserId);
        if (target == null || target.IsDeleted || !target.IsActive)
            return OperationResult<ConversationDto>.Failure("المستخدم المستهدف غير موجود أو غير نشط");

        var existing = await _unitOfWork.Conversations.GetDirectConversationAsync(
            request.InitiatorUserId, request.TargetUserId);

        if (existing != null)
        {
            var existingDto = await MapConversationWithParticipantsAsync(existing);
            return OperationResult<ConversationDto>.Success(existingDto, "تم إرجاع المحادثة الموجودة");
        }

        var conversation = new Conversation
        {
            Type = ConversationType.Direct,
            LastMessageAt = DateTime.UtcNow
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        var participants = new[]
        {
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = request.InitiatorUserId,
                JoinedAt = DateTime.UtcNow
            },
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = request.TargetUserId,
                JoinedAt = DateTime.UtcNow
            }
        };

        await _unitOfWork.ConversationParticipants.AddRangeAsync(participants);
        await _unitOfWork.SaveChangesAsync();

        var dto = await MapConversationWithParticipantsAsync(conversation);
        return OperationResult<ConversationDto>.Success(dto, "تم إنشاء المحادثة بنجاح");
    }

    public async Task<OperationResult<ConversationDto>> CreateGroupConversationAsync(CreateGroupConversationRequest request)
    {
        var creator = await _unitOfWork.Users.GetByIdAsync(request.CreatorUserId);
        if (creator == null || creator.IsDeleted || !creator.IsActive)
            return OperationResult<ConversationDto>.Failure("المنشئ غير موجود أو غير نشط");

        if (creator.Role == UserRole.Student || creator.Role == UserRole.Parent)
            return OperationResult<ConversationDto>.Failure("يمكن للمشرفين والمعلمين فقط إنشاء محادثات جماعية");

        if (request.ParticipantUserIds.Count < 2)
            return OperationResult<ConversationDto>.Failure("يجب أن يكون هناك مشاركان على الأقل");

        var allUserIds = new List<int> { request.CreatorUserId };
        allUserIds.AddRange(request.ParticipantUserIds);

        foreach (var userId in allUserIds)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null || user.IsDeleted || !user.IsActive)
                return OperationResult<ConversationDto>.Failure($"المستخدم بالمعرف {userId} غير موجود أو غير نشط");
        }

        var conversation = new Conversation
        {
            Title = request.Title,
            Type = ConversationType.Group,
            LastMessageAt = DateTime.UtcNow
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        var participants = allUserIds.Select(userId => new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        }).ToList();

        await _unitOfWork.ConversationParticipants.AddRangeAsync(participants);
        await _unitOfWork.SaveChangesAsync();

        var dto = await MapConversationWithParticipantsAsync(conversation);
        return OperationResult<ConversationDto>.Success(dto, "تم إنشاء المحادثة الجماعية بنجاح");
    }

    public async Task<OperationResult<ConversationDto>> CreateSubjectGroupConversationAsync(CreateSubjectGroupConversationRequest request)
    {
        var creator = await _unitOfWork.Users.GetByIdAsync(request.CreatorUserId);
        if (creator == null || creator.IsDeleted || !creator.IsActive)
            return OperationResult<ConversationDto>.Failure("المنشئ غير موجود أو غير نشط");

        if (!creator.Role.IsAdminLike() && creator.Role != UserRole.Teacher)
            return OperationResult<ConversationDto>.Failure("يمكن للمشرفين والمعلمين فقط إنشاء محادثات جماعية للمواد");

        var subject = await _unitOfWork.Subjects.GetByIdAsync(request.SubjectId);
        if (subject == null || subject.IsDeleted)
            return OperationResult<ConversationDto>.Failure("المادة غير موجودة");

        var schoolClass = await _unitOfWork.Classes.GetByIdAsync(request.ClassId);
        if (schoolClass == null || schoolClass.IsDeleted)
            return OperationResult<ConversationDto>.Failure("الفصل غير موجود");

        if (schoolClass.Status != ClassStatus.Active)
            return OperationResult<ConversationDto>.Failure("لا يمكن إنشاء مجموعة لفصل غير نشط");

        var cst = await _unitOfWork.ClassSubjectTeachers.GetByClassSubjectAndYearAsync(
            request.ClassId, request.SubjectId, request.AcademicYearId);

        if (cst == null)
            return OperationResult<ConversationDto>.Failure("لا يوجد معلم مخصص لهذه المادة في هذا الفصل");

        var enrollments = await _unitOfWork.StudentEnrollments.GetActiveByClassAsync(
            request.ClassId, request.AcademicYearId);

        var studentUserIds = enrollments
            .Select(e => e.Student.UserId)
            .Where(id => id.HasValue)
            .Cast<int>()
            .ToList();

        var allUserIds = new List<int> { cst.TeacherId };
        allUserIds.AddRange(studentUserIds);

        if (!allUserIds.Contains(request.CreatorUserId))
            allUserIds.Add(request.CreatorUserId);

        allUserIds = allUserIds.Distinct().ToList();

        var title = request.Title ?? $"{subject.Name} - {schoolClass.Name}";

        var conversation = new Conversation
        {
            Title = title,
            Type = ConversationType.Group,
            LastMessageAt = DateTime.UtcNow
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        var participants = allUserIds.Select(userId => new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        }).ToList();

        await _unitOfWork.ConversationParticipants.AddRangeAsync(participants);
        await _unitOfWork.SaveChangesAsync();

        var dto = await MapConversationWithParticipantsAsync(conversation);
        return OperationResult<ConversationDto>.Success(dto, "تم إنشاء المحادثة الجماعية للمادة بنجاح");
    }

    public async Task<OperationResult<ConversationDto>> CreateClassGroupConversationAsync(CreateClassGroupConversationRequest request)
    {
        var creator = await _unitOfWork.Users.GetByIdAsync(request.CreatorUserId);
        if (creator == null || creator.IsDeleted || !creator.IsActive)
            return OperationResult<ConversationDto>.Failure("المنشئ غير موجود أو غير نشط");

        if (!creator.Role.IsAdminLike() && creator.Role != UserRole.Teacher)
            return OperationResult<ConversationDto>.Failure("يمكن للمشرفين والمعلمين فقط إنشاء محادثات جماعية");

        var schoolClass = await _unitOfWork.Classes.GetByIdAsync(request.ClassId);
        if (schoolClass == null || schoolClass.IsDeleted)
            return OperationResult<ConversationDto>.Failure("الفصل غير موجود");

        if (schoolClass.Status != ClassStatus.Active)
            return OperationResult<ConversationDto>.Failure("لا يمكن إنشاء مجموعة لفصل غير نشط");

        var enrollments = await _unitOfWork.StudentEnrollments.GetActiveByClassAsync(
            request.ClassId, request.AcademicYearId);

        var studentUserIds = enrollments
            .Select(e => e.Student.UserId)
            .Where(id => id.HasValue)
            .Cast<int>()
            .ToList();

        var csts = await _unitOfWork.ClassSubjectTeachers.GetByClassWithTeacherAsync(
            request.ClassId, request.AcademicYearId);

        var teacherUserIds = csts
            .Select(cst => cst.TeacherId)
            .Distinct()
            .ToList();

        var allUserIds = new List<int>();
        allUserIds.AddRange(studentUserIds);
        allUserIds.AddRange(teacherUserIds);

        if (!allUserIds.Contains(request.CreatorUserId))
            allUserIds.Add(request.CreatorUserId);

        allUserIds = allUserIds.Distinct().ToList();

        var title = request.Title ?? $"مجموعة {schoolClass.Name}";

        var conversation = new Conversation
        {
            Title = title,
            Type = ConversationType.Group,
            LastMessageAt = DateTime.UtcNow
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        var participants = allUserIds.Select(userId => new ConversationParticipant
        {
            ConversationId = conversation.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        }).ToList();

        await _unitOfWork.ConversationParticipants.AddRangeAsync(participants);
        await _unitOfWork.SaveChangesAsync();

        var dto = await MapConversationWithParticipantsAsync(conversation);
        return OperationResult<ConversationDto>.Success(dto, "تم إنشاء مجموعة الفصل بنجاح");
    }

    public async Task<OperationResult> DeleteConversationAsync(int conversationId, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult.Failure("المحادثة غير موجودة");

        var isParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(conversationId, userId);
        if (!isParticipant)
            return OperationResult.Failure("المستخدم ليس مشتركا في هذه المحادثة");

        _unitOfWork.Conversations.SoftDelete(conversation);
        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success("تم حذف المحادثة بنجاح");
    }

    public async Task<OperationResult<ConversationDto>> UpdateConversationTitleAsync(int conversationId, string title, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult<ConversationDto>.Failure("المحادثة غير موجودة");

        if (conversation.Type != ConversationType.Group)
            return OperationResult<ConversationDto>.Failure("يمكن تحديث عنوان المحادثات الجماعية فقط");

        var isParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(conversationId, userId);
        if (!isParticipant)
            return OperationResult<ConversationDto>.Failure("المستخدم ليس مشتركا في هذه المحادثة");

        if (string.IsNullOrWhiteSpace(title))
            return OperationResult<ConversationDto>.Failure("العنوان لا يمكن أن يكون فارغا");

        conversation.Title = title;
        conversation.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Conversations.Update(conversation);
        await _unitOfWork.SaveChangesAsync();

        var dto = await MapConversationWithParticipantsAsync(conversation);
        return OperationResult<ConversationDto>.Success(dto, "تم تحديث العنوان بنجاح");
    }

    public async Task<OperationResult<IEnumerable<ConversationParticipantDto>>> GetConversationParticipantsAsync(int conversationId, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult<IEnumerable<ConversationParticipantDto>>.Failure("المحادثة غير موجودة");

        var isParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(conversationId, userId);
        if (!isParticipant)
            return OperationResult<IEnumerable<ConversationParticipantDto>>.Failure("المستخدم ليس مشتركا في هذه المحادثة");

        var participants = await _unitOfWork.ConversationParticipants.GetByConversationIdAsync(conversationId);
        var userIds = participants.Select(p => p.UserId).Distinct().ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id));
        var userDict = users.ToDictionary(u => u.Id, u => u.FullName);

        var dtos = participants.Select(p =>
        {
            var dto = _mapper.Map<ConversationParticipantDto>(p);
            dto.UserName = userDict.GetValueOrDefault(p.UserId, "");
            return dto;
        }).ToList();

        return OperationResult<IEnumerable<ConversationParticipantDto>>.Success(dtos);
    }

    public async Task<OperationResult> MarkConversationAsReadAsync(int conversationId, int userId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult.Failure("المحادثة غير موجودة");

        var isParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(conversationId, userId);
        if (!isParticipant)
            return OperationResult.Failure("المستخدم ليس مشتركا في هذه المحادثة");

        await _unitOfWork.ConversationParticipants.UpdateLastReadAtAsync(conversationId, userId, DateTime.UtcNow);
        return OperationResult.Success("تم وضع علامة مقروءة على المحادثة");
    }

    public async Task<OperationResult<IEnumerable<ConversationDto>>> SearchConversationsAsync(int userId, string term)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return OperationResult<IEnumerable<ConversationDto>>.Failure("المستخدم غير موجود");

        var conversations = await _unitOfWork.Conversations.GetWithLastMessageAsync(userId);
        var filtered = conversations
            .Where(c => c.Title != null && c.Title.Contains(term, StringComparison.OrdinalIgnoreCase));

        var dtos = new List<ConversationDto>();
        foreach (var conversation in filtered)
        {
            var dto = await MapConversationWithParticipantsAsync(conversation);
            dtos.Add(dto);
        }

        return OperationResult<IEnumerable<ConversationDto>>.Success(dtos);
    }

    public async Task<OperationResult<ConversationDto>> GetConversationByIdAsync(int conversationId, int requestingUserId)
    {
        var conversation = await _unitOfWork.Conversations.GetWithParticipantsAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult<ConversationDto>.Failure("المحادثة غير موجودة");

        var isParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(conversationId, requestingUserId);
        if (!isParticipant)
            return OperationResult<ConversationDto>.Failure("المستخدم ليس مشتركا في هذه المحادثة");

        var dto = await MapConversationWithParticipantsAsync(conversation);
        return OperationResult<ConversationDto>.Success(dto);
    }

    public async Task<OperationResult<IEnumerable<ConversationDto>>> GetUserConversationsAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return OperationResult<IEnumerable<ConversationDto>>.Failure("المستخدم غير موجود");

        var conversations = await _unitOfWork.Conversations.GetWithLastMessageAsync(userId);

        var dtos = new List<ConversationDto>();
        foreach (var conversation in conversations)
        {
            var dto = await MapConversationWithParticipantsAsync(conversation);
            dtos.Add(dto);
        }

        return OperationResult<IEnumerable<ConversationDto>>.Success(dtos.OrderByDescending(c => c.LastMessageAt));
    }

    public async Task<OperationResult<int>> GetUnreadMessagesCountAsync(int userId)
    {
        var participants = await _unitOfWork.ConversationParticipants.GetByUserIdAsync(userId);
        var totalUnread = 0;

        foreach (var participant in participants)
        {
            var unread = await _unitOfWork.Messages.GetUnreadCountAsync(participant.ConversationId, userId);
            totalUnread += unread;
        }

        return OperationResult<int>.Success(totalUnread);
    }

    public async Task<OperationResult> AddParticipantAsync(int conversationId, int userId, int callerUserId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult.Failure("المحادثة غير موجودة");

        if (conversation.Type != ConversationType.Group)
            return OperationResult.Failure("يمكن إضافة مشاركين فقط إلى المحادثات الجماعية");

        var isCallerParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(conversationId, callerUserId);
        if (!isCallerParticipant)
            return OperationResult.Failure("المتصل ليس مشتركا في هذه المحادثة");

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.IsDeleted || !user.IsActive)
            return OperationResult.Failure("المستخدم غير موجود أو غير نشط");

        // Check if user was previously a participant (including soft-deleted)
        var existingParticipant = await _unitOfWork.ConversationParticipants
            .GetByConversationAndUserWithDeletedAsync(conversationId, userId);

        if (existingParticipant != null)
        {
            if (!existingParticipant.IsDeleted)
                return OperationResult.Failure("المستخدم مشترك بالفعل");

            // Restore soft-deleted participant
            existingParticipant.IsDeleted = false;
            existingParticipant.JoinedAt = DateTime.UtcNow;
            existingParticipant.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.ConversationParticipants.Update(existingParticipant);
            await _unitOfWork.SaveChangesAsync();

            await _notificationService.SendNotificationAsync(new SendNotificationRequest
            {
                UserId = userId,
                Title = "دعوة محادثة جماعية",
                Body = $"تمت إعادة إضافتك إلى محادثة جماعية: {conversation.Title ?? conversation.Id.ToString()}",
                Type = NotificationType.GroupChatInvite
            });

            return OperationResult.Success("تم إعادة إضافة المشترك بنجاح");
        }

        var participant = new ConversationParticipant
        {
            ConversationId = conversationId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        };

        await _unitOfWork.ConversationParticipants.AddAsync(participant);
        await _unitOfWork.SaveChangesAsync();

        // Notify the added user
        await _notificationService.SendNotificationAsync(new SendNotificationRequest
        {
            UserId = userId,
            Title = "دعوة محادثة جماعية",
            Body = $"تمت إضافتك إلى محادثة جماعية: {conversation.Title ?? conversation.Id.ToString()}",
            Type = NotificationType.GroupChatInvite
        });

        return OperationResult.Success("تم إضافة المشترك بنجاح");
    }

    public async Task<OperationResult> RemoveParticipantAsync(int conversationId, int userId, int callerUserId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult.Failure("المحادثة غير موجودة");

        if (conversation.Type != ConversationType.Group)
            return OperationResult.Failure("يمكن إزالة مشاركين فقط من المحادثات الجماعية");

        var isCallerParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(conversationId, callerUserId);
        if (!isCallerParticipant)
            return OperationResult.Failure("المتصل ليس مشتركا في هذه المحادثة");

        var participant = await _unitOfWork.ConversationParticipants
            .GetByConversationAndUserAsync(conversationId, userId);

        if (participant == null)
            return OperationResult.Failure("المستخدم ليس مشتركا في هذه المحادثة");

        _unitOfWork.ConversationParticipants.SoftDelete(participant);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult.Success("تم إزالة المشترك بنجاح");
    }

    public async Task<OperationResult<MessageDto>> SendMessageAsync(SendMessageRequest request)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(request.ConversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult<MessageDto>.Failure("المحادثة غير موجودة");

        var isParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(
            request.ConversationId, request.SenderId);

        if (!isParticipant)
            return OperationResult<MessageDto>.Failure("المرسل ليس مشتركا في هذه المحادثة");

        if (string.IsNullOrWhiteSpace(request.Content))
            return OperationResult<MessageDto>.Failure("محتوى الرسالة لا يمكن أن يكون فارغا");

        var participants = await _unitOfWork.ConversationParticipants
            .GetByConversationIdAsync(request.ConversationId);
        var otherParticipants = participants.Where(p => p.UserId != request.SenderId);
        foreach (var other in otherParticipants)
        {
            var blocked = await _unitOfWork.BlockedUsers.IsBlockedAsync(
                request.SenderId, other.UserId, request.ConversationId);
            if (blocked)
                return OperationResult<MessageDto>.Failure("لا يمكنك إرسال رسالة لمستخدم قمت بحظره");

            var blockedByOther = await _unitOfWork.BlockedUsers.IsBlockedAsync(
                other.UserId, request.SenderId, request.ConversationId);
            if (blockedByOther)
                return OperationResult<MessageDto>.Failure("لا يمكنك إرسال رسالة لهذا المستخدم");
        }

        var message = _mapper.Map<Message>(request);
        message.SentAt = DateTime.UtcNow;

        await _unitOfWork.Messages.AddAsync(message);

        conversation.LastMessageAt = DateTime.UtcNow;
        _unitOfWork.Conversations.Update(conversation);

        await _unitOfWork.SaveChangesAsync();

        var sender = await _unitOfWork.Users.GetByIdAsync(request.SenderId);

        // Notify other participants about new message
        var otherParticipantIds = participants
            .Where(p => p.UserId != request.SenderId)
            .Select(p => p.UserId)
            .ToList();

        if (otherParticipantIds.Count != 0)
        {
            await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
            {
                UserIds = otherParticipantIds,
                Title = "رسالة جديدة",
                Body = $"لديك رسالة جديدة من {(sender?.FullName ?? "")}",
                Type = NotificationType.NewMessage
            });
        }

        var dto = _mapper.Map<MessageDto>(message);
        dto.SenderName = sender?.FullName ?? "";

        return OperationResult<MessageDto>.Success(dto, "تم إرسال الرسالة بنجاح");
    }

    public async Task<OperationResult<MessageDto>> UpdateMessageAsync(int messageId, int userId, string newContent)
    {
        var message = await _unitOfWork.Messages.GetByIdAsync(messageId);
        if (message == null || message.IsDeleted)
            return OperationResult<MessageDto>.Failure("الرسالة غير موجودة");
        if (message.SenderId != userId)
            return OperationResult<MessageDto>.Failure("لا يمكنك تعديل رسالة شخص آخر");

        if (string.IsNullOrWhiteSpace(newContent))
            return OperationResult<MessageDto>.Failure("محتوى الرسالة لا يمكن أن يكون فارغا");

        message.Content = newContent;
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;
        _unitOfWork.Messages.Update(message);
        await _unitOfWork.SaveChangesAsync();

        var sender = await _unitOfWork.Users.GetByIdAsync(message.SenderId);
        var dto = _mapper.Map<MessageDto>(message);
        dto.SenderName = sender?.FullName ?? "";
        return OperationResult<MessageDto>.Success(dto, "تم تعديل الرسالة بنجاح");
    }

    public async Task<OperationResult<string?>> DeleteMessageAsync(int messageId, int userId)
    {
        var message = await _unitOfWork.Messages.GetByIdAsync(messageId);
        if (message == null || message.IsDeleted)
            return OperationResult<string?>.Failure("الرسالة غير موجودة");
        if (message.SenderId != userId)
            return OperationResult<string?>.Failure("لا يمكنك حذف رسالة شخص آخر");

        message.IsDeleted = true;
        _unitOfWork.Messages.Update(message);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult<string?>.Success(message.AttachmentUrl, "تم حذف الرسالة بنجاح");
    }

    public async Task<OperationResult<MessageDto>> GetMessageByIdAsync(int messageId)
    {
        var message = await _unitOfWork.Messages.GetByIdAsync(messageId);
        if (message == null || message.IsDeleted)
            return OperationResult<MessageDto>.Failure("الرسالة غير موجودة");
        var dto = _mapper.Map<MessageDto>(message);
        return OperationResult<MessageDto>.Success(dto);
    }

    public async Task<OperationResult<MessageDto>> TranscribeMessageAsync(int messageId, int userId, string voiceText)
    {
        var message = await _unitOfWork.Messages.GetByIdAsync(messageId);
        if (message == null || message.IsDeleted)
            return OperationResult<MessageDto>.Failure("الرسالة غير موجودة");
        if (string.IsNullOrWhiteSpace(message.AttachmentUrl) || message.AttachmentType?.StartsWith("audio/") != true)
            return OperationResult<MessageDto>.Failure("الرسالة ليست رسالة صوتية");

        message.VoiceText = voiceText;
        _unitOfWork.Messages.Update(message);
        await _unitOfWork.SaveChangesAsync();

        var sender = await _unitOfWork.Users.GetByIdAsync(message.SenderId);
        var dto = _mapper.Map<MessageDto>(message);
        dto.SenderName = sender?.FullName ?? "";
        return OperationResult<MessageDto>.Success(dto, "تم التعرف على النص بنجاح");
    }

    public async Task<OperationResult> BlockUserAsync(int conversationId, int blockerId, int blockedUserId)
    {
        if (blockerId == blockedUserId)
            return OperationResult.Failure("لا يمكنك حظر نفسك");

        var blockerUser = await _unitOfWork.Users.GetByIdAsync(blockerId);
        var blockedUser = await _unitOfWork.Users.GetByIdAsync(blockedUserId);
        if (blockerUser == null || blockedUser == null)
            return OperationResult.Failure("المستخدم غير موجود");

        if (blockerUser.Role == UserRole.Parent)
            return OperationResult.Failure("لا يمكنك حظر المستخدمين");

        if (blockerUser.Role == UserRole.Student && blockedUser.Role != UserRole.Student && blockedUser.Role != UserRole.Parent)
            return OperationResult.Failure("لا يمكن للطالب حظر هذا المستخدم");

        if (blockerUser.Role == UserRole.Teacher && blockedUser.Role.IsAdminLike())
            return OperationResult.Failure("لا يمكن للمدرس حظر مدير");

        var existing = await _unitOfWork.BlockedUsers.GetBlockAsync(blockerId, blockedUserId, conversationId);
        if (existing != null)
        {
            if (!existing.IsDeleted)
                return OperationResult.Failure("المستخدم محظور بالفعل");
            existing.IsDeleted = false;
            _unitOfWork.BlockedUsers.Update(existing);
        }
        else
        {
            var block = new BlockedUser
            {
                BlockerId = blockerId,
                BlockedUserId = blockedUserId,
                ConversationId = conversationId
            };
            await _unitOfWork.BlockedUsers.AddAsync(block);
        }
        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success("تم حظر المستخدم بنجاح");
    }

    public async Task<OperationResult> UnblockUserAsync(int conversationId, int blockerId, int blockedUserId)
    {
        var block = await _unitOfWork.BlockedUsers.GetBlockAsync(blockerId, blockedUserId, conversationId);
        if (block == null)
            return OperationResult.Failure("المستخدم غير محظور");

        _unitOfWork.BlockedUsers.SoftDelete(block);
        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success("تم إلغاء حظر المستخدم بنجاح");
    }

    public async Task<OperationResult<BlockStatusDto>> IsUserBlockedAsync(int conversationId, int userId, int otherUserId)
    {
        var blockedByMe = await _unitOfWork.BlockedUsers.IsBlockedAsync(userId, otherUserId, conversationId);
        var blockedByOther = await _unitOfWork.BlockedUsers.IsBlockedAsync(otherUserId, userId, conversationId);
        return OperationResult<BlockStatusDto>.Success(new BlockStatusDto
        {
            IsBlocked = blockedByMe || blockedByOther,
            BlockedByMe = blockedByMe,
            BlockedByOther = blockedByOther
        });
    }

    public async Task<OperationResult<PagedResult<MessageDto>>> GetMessagesAsync(
        int conversationId, int requestingUserId, PaginationFilter filter)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null || conversation.IsDeleted)
            return OperationResult<PagedResult<MessageDto>>.Failure("المحادثة غير موجودة");

        var isParticipant = await _unitOfWork.ConversationParticipants.IsParticipantAsync(
            conversationId, requestingUserId);

        if (!isParticipant)
            return OperationResult<PagedResult<MessageDto>>.Failure("المستخدم ليس مشتركا في هذه المحادثة");

        var messages = await _unitOfWork.Messages.GetByConversationPagedAsync(
            conversationId, filter.Page, filter.PageSize);

        await _unitOfWork.ConversationParticipants.UpdateLastReadAtAsync(
            conversationId, requestingUserId, DateTime.UtcNow);

        var totalCount = await _unitOfWork.Messages.CountAsync(m => m.ConversationId == conversationId);

        var dtos = _mapper.Map<IEnumerable<MessageDto>>(messages);

        var paged = new PagedResult<MessageDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return OperationResult<PagedResult<MessageDto>>.Success(paged);
    }

    private async Task<ConversationDto> MapConversationWithParticipantsAsync(Conversation conversation)
    {
        var dto = _mapper.Map<ConversationDto>(conversation);

        var participants = await _unitOfWork.ConversationParticipants
            .GetByConversationIdAsync(conversation.Id);

        var userIds = participants.Select(p => p.UserId).Distinct().ToList();
        var users = await _unitOfWork.Users.FindAsync(u => userIds.Contains(u.Id));
        var userDict = users.ToDictionary(u => u.Id, u => u.FullName);
        var userRoleDict = users.ToDictionary(u => u.Id, u => u.Role.ToString());

        dto.Participants = participants.Select(p =>
        {
            var pDto = _mapper.Map<ConversationParticipantDto>(p);
            pDto.UserName = userDict.GetValueOrDefault(p.UserId, "");
            pDto.Role = userRoleDict.GetValueOrDefault(p.UserId, "");
            return pDto;
        }).ToList();

        return dto;
    }
}
