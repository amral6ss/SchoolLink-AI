using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.Exam;
using Project.BLL.Interfaces;
using Project.BLL.Utils;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;
using Project.BLL.DTOs.Notifications;

namespace Project.BLL.Services
{
    public class ExamService : IExamService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IExamMediaService _mediaService;
        private readonly IExamHtmlRenderer _htmlRenderer;
        private readonly INotificationService _notificationService;

        public ExamService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IExamMediaService mediaService,
            IExamHtmlRenderer htmlRenderer,
            INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _mediaService = mediaService;
            _htmlRenderer = htmlRenderer;
            _notificationService = notificationService;
        }

        public async Task<OperationResult<List<ExamSummaryDto>>> GetAllByClassSubjectTeacherAsync(int classSubjectTeacherId)
        {
            var exams = await _unitOfWork.Exams
                .GetByClassSubjectTeacherIdAsync(classSubjectTeacherId, CancellationToken.None);

            var dtos = _mapper.Map<List<ExamSummaryDto>>(exams);
            return OperationResult<List<ExamSummaryDto>>.Success(dtos);
        }

        public async Task<OperationResult<List<ExamSummaryDto>>> GetAiExamHistoryByTeacherAsync(int teacherId)
        {
            var csts = await _unitOfWork.ClassSubjectTeachers
                .FindAsync(cst => cst.TeacherId == teacherId && !cst.IsDeleted, CancellationToken.None);

            if (csts.Count == 0)
                return OperationResult<List<ExamSummaryDto>>.Success(new List<ExamSummaryDto>());

            var cstIds = csts.Select(c => c.Id).ToList();
            var allExams = await _unitOfWork.Exams
                .GetAIGeneratedByTeacherAsync(cstIds, CancellationToken.None);

            var dtos = _mapper.Map<List<ExamSummaryDto>>(allExams);
            return OperationResult<List<ExamSummaryDto>>.Success(dtos, "تم جلب سجل الامتحانات بنجاح");
        }

        public async Task<OperationResult<GetExamDto>> GetByIdAsync(int id)
        {
            var exam = await _unitOfWork.Exams.GetWithQuestionsAsync(id, CancellationToken.None);

            if (exam == null || exam.IsDeleted)
                return OperationResult<GetExamDto>.Failure("الامتحان غير موجود", 404);

            var dto = _mapper.Map<GetExamDto>(exam);
            return OperationResult<GetExamDto>.Success(dto);
        }

        public async Task<OperationResult<GetExamDto>> GetByUidAsync(Guid uid, CancellationToken ct = default)
        {
            var exam = await _unitOfWork.Exams.GetWithQuestionsByUidAsync(uid, ct);
            if (exam == null || exam.IsDeleted)
                return OperationResult<GetExamDto>.Failure("الامتحان غير موجود", 404);

            var dto = _mapper.Map<GetExamDto>(exam);
            return OperationResult<GetExamDto>.Success(dto);
        }

        public async Task<OperationResult<ExamSummaryDto>> CreateAsync(CreateExamDto dto)
        {
            if (dto.ClassSubjectTeacherId.HasValue)
            {
                var classSubjectTeacher = await _unitOfWork.ClassSubjectTeachers
                    .GetByIdAsync(dto.ClassSubjectTeacherId.Value);

                if (classSubjectTeacher == null || classSubjectTeacher.IsDeleted)
                    return OperationResult<ExamSummaryDto>.Failure("المادة غير موجودة", 404);
            }

            if (dto.GradeLevelId > 0)
            {
                var gradeLevel = await _unitOfWork.GradeLevels.GetByIdAsync(dto.GradeLevelId);
                if (gradeLevel == null || gradeLevel.IsDeleted)
                    return OperationResult<ExamSummaryDto>.Failure("الصف الدراسي غير موجود", 404);
            }

            var exam = _mapper.Map<Exam>(dto);

            await _unitOfWork.Exams.AddAsync(exam);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            // Notify students about new exam
            if (exam.ClassSubjectTeacherId.HasValue)
            {
                var cst = await _unitOfWork.ClassSubjectTeachers.GetByIdAsync(exam.ClassSubjectTeacherId.Value);
                if (cst != null)
                {
                    var enrollments = await _unitOfWork.StudentEnrollments
                        .GetActiveByClassAsync(cst.ClassId, cst.AcademicYearId);
                    var studentIds = enrollments
                        .Where(e => e.Student != null && e.Student.UserId != null)
                        .Select(e => e.Student.UserId!.Value)
                        .Distinct()
                        .ToList();

                    if (studentIds.Count != 0)
                    {
                        await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
                        {
                            UserIds = studentIds,
                            Title = "امتحان جديد",
                            Body = $"تم إضافة امتحان جديد: {exam.Title}",
                            Type = NotificationType.Exam
                        });
                    }
                }
            }

            var resultDto = _mapper.Map<ExamSummaryDto>(exam);
            return OperationResult<ExamSummaryDto>.Success(resultDto, "تم إنشاء الامتحان بنجاح");
        }

        public async Task<OperationResult<ExamSummaryDto>> UpdateAsync(UpdateExamDto dto)
        {
            var exam = await _unitOfWork.Exams.GetByIdAsync(dto.Id);

            if (exam == null || exam.IsDeleted)
                return OperationResult<ExamSummaryDto>.Failure("الامتحان غير موجود", 404);

            // Check if schedule changed
            var oldStartTime = exam.StartTime;
            var oldEndTime = exam.EndTime;

            exam.Title = dto.Title;
            exam.StartTime = dto.StartTime;
            exam.EndTime = dto.EndTime;
            exam.DurationMinutes = dto.DurationMinutes;
            exam.TotalScore = dto.TotalScore;
            exam.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Exams.Update(exam);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            // Notify if schedule changed
            if (oldStartTime != dto.StartTime || oldEndTime != dto.EndTime)
            {
                if (exam.ClassSubjectTeacherId.HasValue)
                {
                    var cst = await _unitOfWork.ClassSubjectTeachers.GetByIdAsync(exam.ClassSubjectTeacherId.Value);
                    if (cst != null)
                    {
                        var enrollments = await _unitOfWork.StudentEnrollments
                            .GetActiveByClassAsync(cst.ClassId, cst.AcademicYearId);
                        var studentIds = enrollments
                            .Where(e => e.Student != null && e.Student.UserId != null)
                            .Select(e => e.Student.UserId!.Value)
                            .Distinct()
                            .ToList();

                        if (studentIds.Count != 0)
                        {
                            await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
                            {
                                UserIds = studentIds,
                                Title = "تغيير موعد الامتحان",
                                Body = $"تم تغيير موعد الامتحان: {exam.Title}",
                                Type = NotificationType.ExamScheduleChanged
                            });
                        }
                    }
                }
            }

            var resultDto = _mapper.Map<ExamSummaryDto>(exam);
            return OperationResult<ExamSummaryDto>.Success(resultDto, "تم تحديث الامتحان بنجاح");
        }

        public async Task<OperationResult> DeleteAsync(int id)
        {
            var exam = await _unitOfWork.Exams.GetByIdAsync(id);

            if (exam == null || exam.IsDeleted)
                return OperationResult.Failure("الامتحان غير موجود");

            _unitOfWork.Exams.SoftDelete(exam);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            return OperationResult.Success("تم حذف الامتحان بنجاح");
        }

        public async Task<OperationResult> PublishAsync(int id)
        {
            var exam = await _unitOfWork.Exams.GetByIdAsync(id);

            if (exam == null || exam.IsDeleted)
                return OperationResult.Failure("الامتحان غير موجود");

            if (exam.IsPublished)
                return OperationResult.Failure("الامتحان منشور بالفعل");

            exam.IsPublished = true;
            exam.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Exams.Update(exam);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            // Notify students about published exam
            await NotifyExamStudentsAsync(exam);

            return OperationResult.Success("تم نشر الامتحان بنجاح");
        }

        public async Task<OperationResult> UnPublishAsync(int id)
        {
            var exam = await _unitOfWork.Exams.GetByIdAsync(id);

            if (exam == null || exam.IsDeleted)
                return OperationResult.Failure("الامتحان غير موجود");

            if (!exam.IsPublished)
                return OperationResult.Failure("الامتحان غير منشور");

            exam.IsPublished = false;
            exam.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.Exams.Update(exam);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            return OperationResult.Success("تم إلغاء نشر الامتحان بنجاح");
        }

        public async Task<OperationResult<List<ExamSummaryDto>>> GetExamsByStudentAsync(int enrollmentId)
        {
            var enrollment = await _unitOfWork.StudentEnrollments.GetByIdAsync(enrollmentId);
            if (enrollment == null || enrollment.IsDeleted)
                return OperationResult<List<ExamSummaryDto>>.Failure("التسجيل غير موجود", 404);

            var csts = await _unitOfWork.ClassSubjectTeachers
                .GetByClassAndYearAsync(enrollment.ClassId, enrollment.AcademicYearId);

            var cstIds = csts.Select(c => c.Id).ToList();
            if (cstIds.Count == 0)
                return OperationResult<List<ExamSummaryDto>>.Success(new List<ExamSummaryDto>());

            var allExams = await _unitOfWork.Exams
                .FindAsync(e => (e.ClassSubjectTeacherId != null && cstIds.Contains(e.ClassSubjectTeacherId.Value) || e.ClassSubjectTeacherId == null) && !e.IsDeleted);

            var dtos = _mapper.Map<List<ExamSummaryDto>>(allExams);
            return OperationResult<List<ExamSummaryDto>>.Success(dtos, "تم جلب الامتحانات بنجاح");
        }

        public async Task<OperationResult<List<ExamSummaryDto>>> GetUpcomingExamsAsync(int classId, int academicYearId)
        {
            var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
            if (classEntity == null || classEntity.IsDeleted)
                return OperationResult<List<ExamSummaryDto>>.Failure("الفصل غير موجود", 404);

            var exams = await _unitOfWork.Exams.GetUpcomingByClassAsync(classId, 7);
            var dtos = _mapper.Map<List<ExamSummaryDto>>(exams.Where(e => !e.IsDeleted));
            return OperationResult<List<ExamSummaryDto>>.Success(dtos, "تم جلب الامتحانات القادمة بنجاح");
        }

        public async Task<OperationResult<GetExamDto>> CreateFromAiAsync(CreateExamFromAiDto dto, CancellationToken ct = default)
        {
            if (dto.ClassSubjectTeacherId.HasValue)
            {
                var classSubjectTeacher = await _unitOfWork.ClassSubjectTeachers
                    .GetByIdAsync(dto.ClassSubjectTeacherId.Value);

                if (classSubjectTeacher == null || classSubjectTeacher.IsDeleted)
                    return OperationResult<GetExamDto>.Failure("المادة غير موجودة", 404);
            }

            if (dto.GradeLevelId <= 0)
                return OperationResult<GetExamDto>.Failure("الصف الدراسي مطلوب", 400);

            var gradeLevel = await _unitOfWork.GradeLevels.GetByIdAsync(dto.GradeLevelId);
            if (gradeLevel == null || gradeLevel.IsDeleted)
                return OperationResult<GetExamDto>.Failure("الصف الدراسي غير موجود", 404);

            // If no CST but SubjectId is provided, validate subject exists
            if (!dto.ClassSubjectTeacherId.HasValue && dto.SubjectId.HasValue)
            {
                var subject = await _unitOfWork.Subjects.GetByIdAsync(dto.SubjectId.Value);
                if (subject == null || subject.IsDeleted)
                    return OperationResult<GetExamDto>.Failure("المادة غير موجودة", 404);
            }

            var exam = new Exam
            {
                ClassSubjectTeacherId = dto.ClassSubjectTeacherId,
                SubjectId = !dto.ClassSubjectTeacherId.HasValue ? dto.SubjectId : null,
                GradeLevelId = dto.GradeLevelId,
                Title = dto.Title,
                DurationMinutes = dto.DurationMinutes,
                TotalScore = dto.TotalScore,
                Category = dto.Category,
                IsAIGenerated = true,
                IsPublished = false
            };

            await _unitOfWork.Exams.AddAsync(exam);
            await _unitOfWork.SaveChangesAsync(ct);

            int order = 1;

            foreach (var groupDto in dto.Groups)
            {
                var group = new ExamQuestionGroup
                {
                    ExamId = exam.Id,
                    DisplayType = groupDto.DisplayType,
                    ContentTitle = groupDto.ContentTitle,
                    ContentText = groupDto.ContentText,
                    ImagePrompt = groupDto.ImagePrompt,
                    DisplayOrder = groupDto.DisplayOrder > 0 ? groupDto.DisplayOrder : order++
                };

                await _unitOfWork.ExamQuestionGroups.AddAsync(group);
                await _unitOfWork.SaveChangesAsync(ct);

                foreach (var qDto in groupDto.Questions)
                {
                    var question = MapQuestion(qDto, exam.Id, group.Id, groupDto.DisplayType);
                    await _unitOfWork.ExamQuestions.AddAsync(question);
                }

                if (!string.IsNullOrWhiteSpace(group.ImagePrompt))
                {
                    try
                    {
                        group.ImageUrl = await _mediaService.GenerateImageAsync(group.ImagePrompt, group.Id, ct);
                    }
                    catch (Exception ex)
                    {
                        group.ImagePrompt = $"[فشل توليد الصورة: {ex.Message}]";
                    }

                    if (groupDto.DisplayType == TemplateContentType.Diagram && !string.IsNullOrWhiteSpace(group.ContentText))
                    {
                        try
                        {
                            group.ContentText = _mediaService.SanitizeSvg(group.ContentText);
                        }
                        catch (Exception ex)
                        {
                            group.ContentText = $"[SVG غير صالح: {ex.Message}]";
                        }
                    }

                    _unitOfWork.ExamQuestionGroups.Update(group);
                }
            }

            foreach (var qDto in dto.StandaloneQuestions)
            {
                var question = MapQuestion(qDto, exam.Id, null, TemplateContentType.None);
                await _unitOfWork.ExamQuestions.AddAsync(question);
            }

            await _unitOfWork.SaveChangesAsync(ct);

            var resultExam = await _unitOfWork.Exams.GetWithQuestionsAsync(exam.Id, ct);
            var resultDto = _mapper.Map<GetExamDto>(resultExam);
            return OperationResult<GetExamDto>.Success(resultDto, "تم إنشاء الامتحان بواسطة AI بنجاح");
        }

        public async Task<OperationResult<string>> RenderHtmlAsync(Guid uid, CancellationToken ct = default)
        {
            var exam = await _unitOfWork.Exams.GetByUidAsync(uid, ct);
            if (exam == null || exam.IsDeleted)
                return OperationResult<string>.Failure("الامتحان غير موجود", 404);

            var html = await _htmlRenderer.RenderExamAsync(exam.Id, ct);
            return OperationResult<string>.Success(html, "تم تجهيز HTML للطباعة");
        }

        public async Task<OperationResult> SaveExamQuestionsAsync(SaveExamQuestionsDto dto, CancellationToken ct = default)
        {
            var exam = await _unitOfWork.Exams.GetByUidAsync(dto.Uid, ct);
            if (exam == null || exam.IsDeleted)
                return OperationResult.Failure("الامتحان غير موجود");

            exam.Title = dto.Title;
            exam.DurationMinutes = dto.DurationMinutes;
            exam.TotalScore = dto.TotalScore;
            exam.ClassSubjectTeacherId = dto.ClassSubjectTeacherId;
            if (dto.GradeLevelId > 0)
                exam.GradeLevelId = dto.GradeLevelId;
            exam.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.Exams.Update(exam);

            var existingQuestions = await _unitOfWork.ExamQuestions
                .GetWithOptionsByExamIdAsync(exam.Id, ct);

            var incomingIds = dto.Questions.Where(q => q.Id > 0).Select(q => q.Id).ToHashSet();

            // Soft-delete removed questions and their options
            foreach (var q in existingQuestions)
            {
                if (!incomingIds.Contains(q.Id))
                {
                    foreach (var opt in q.Options.ToList())
                        _unitOfWork.ExamQuestionOptions.SoftDelete(opt);
                    _unitOfWork.ExamQuestions.SoftDelete(q);
                }
            }

            foreach (var qDto in dto.Questions)
            {
                var question = existingQuestions.FirstOrDefault(q => q.Id == qDto.Id && qDto.Id > 0);
                bool isNew = question == null;

                if (isNew)
                {
                    question = new ExamQuestion
                    {
                        ExamId = exam.Id,
                        GroupId = null,
                        DisplayType = TemplateContentType.None,
                    };
                }

                // Preserve existing QuestionType when incoming value is default (0 = unset)
                if (isNew)
                    question.QuestionType = qDto.QuestionType;
                else if (qDto.QuestionType != 0)
                    question.QuestionType = qDto.QuestionType;

                // Set CorrectAnswer from options (MCQ/TF) or from DTO (FillBlank/Essay)
                // مع تطبيع القيمة لـ canonical "True"/"False" لأسئلة صح/خطأ (الطبقة 2)
                if (qDto.Options.Count > 0)
                {
                    var correctOpt = qDto.Options.FirstOrDefault(o => o.IsCorrect);
                    if (correctOpt != null)
                        question.CorrectAnswer = BooleanNormalizer.NormalizeCanonicalCorrectAnswer(question.QuestionType, correctOpt.OptionText);
                }
                else if (qDto.CorrectAnswer != null)
                {
                    // Validation (الطبقة 4): رفض القيم غير الصالحة لأسئلة صح/خطأ
                    if (question.QuestionType == QuestionType.TrueFalse
                        && !BooleanNormalizer.IsValidTrueFalseAnswer(qDto.CorrectAnswer))
                        return OperationResult.Failure($"قيمة الإجابة الصحيحة لسؤال صح/خطأ غير صالحة: \"{qDto.CorrectAnswer}\"");

                    question.CorrectAnswer = BooleanNormalizer.NormalizeCanonicalCorrectAnswer(question.QuestionType, qDto.CorrectAnswer);
                }

                question.QuestionText = qDto.QuestionText;
                question.Points = qDto.Points;
                question.DisplayOrder = qDto.DisplayOrder;

                // Handle options
                if (qDto.Options.Count > 0)
                {
                    var existingOptions = isNew ? new List<ExamQuestionOption>() : question.Options.ToList();
                    var incomingOptionIds = qDto.Options.Where(o => o.Id > 0).Select(o => o.Id).ToHashSet();

                    foreach (var opt in existingOptions)
                    {
                        if (!incomingOptionIds.Contains(opt.Id))
                            _unitOfWork.ExamQuestionOptions.SoftDelete(opt);
                    }

                    foreach (var oDto in qDto.Options)
                    {
                        var option = existingOptions.FirstOrDefault(o => o.Id == oDto.Id && oDto.Id > 0);
                        if (option == null)
                        {
                            option = new ExamQuestionOption();
                            question.Options.Add(option);
                        }
                        option.OptionText = oDto.OptionText;
                        option.IsCorrect = oDto.IsCorrect;
                        option.DisplayOrder = oDto.DisplayOrder;
                    }
                }
                else if (!isNew)
                {
                    // Remove all existing options if no options provided
                    foreach (var opt in question.Options.ToList())
                        _unitOfWork.ExamQuestionOptions.SoftDelete(opt);
                }

                if (isNew)
                    await _unitOfWork.ExamQuestions.AddAsync(question, ct);
                else
                    _unitOfWork.ExamQuestions.Update(question);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            return OperationResult.Success("تم حفظ التعديلات بنجاح");
        }

        private static ExamQuestion MapQuestion(AiQuestionDto dto, int examId, int? groupId, TemplateContentType displayType)
        {
            var question = new ExamQuestion
            {
                ExamId = examId,
                GroupId = groupId,
                DisplayType = displayType,
                QuestionText = dto.QuestionText,
                QuestionType = dto.QuestionType,
                // تطبيع القيمة لـ canonical "True"/"False" لأسئلة صح/خطأ (الطبقة 2)
                // (الـ AI flow بيبعت options للـ TF عادةً، بس نطبّع كـ safety net لو رجّع نص)
                CorrectAnswer = BooleanNormalizer.NormalizeCanonicalCorrectAnswer(dto.QuestionType, dto.CorrectAnswer),
                Points = dto.Points,
                DisplayOrder = dto.DisplayOrder,
                Options = dto.Options?.Select(o => new ExamQuestionOption
                {
                    OptionText = o.Text,
                    IsCorrect = o.IsCorrect,
                    DisplayOrder = o.DisplayOrder
                }).ToList() ?? new List<ExamQuestionOption>()
            };

            return question;
        }

        /// <summary>
        /// إرسال إشعار إلى جميع الطلاب المستهدفين (و أولياء أمورهم) بعد نشر الامتحان
        /// </summary>
        private async Task NotifyExamStudentsAsync(Exam exam)
        {
            var currentYear = await _unitOfWork.AcademicYears.GetCurrentAsync();
            if (currentYear == null) return;

            List<int> studentIds;

            if (exam.ClassSubjectTeacherId.HasValue)
            {
                var cst = await _unitOfWork.ClassSubjectTeachers
                    .GetByIdAsync(exam.ClassSubjectTeacherId.Value);
                if (cst == null) return;

                var enrollments = await _unitOfWork.StudentEnrollments
                    .GetActiveByClassAsync(cst.ClassId, cst.AcademicYearId);
                studentIds = enrollments
                    .Where(e => e.Student != null && e.Student.UserId != null && e.Student.IsActive
                        && e.Student.LifecycleStatus == StudentLifecycleStatus.Active)
                    .Select(e => e.Student.UserId!.Value)
                    .Distinct()
                    .ToList();
            }
            else
            {
                // Grade-level exam (no specific CST)
                var enrollments = await _unitOfWork.StudentEnrollments
                    .GetActiveByGradeLevelAndYearWithDetailsAsync(exam.GradeLevelId, currentYear.Id);
                studentIds = enrollments
                    .Where(e => e.Student != null && e.Student.UserId != null && e.Student.IsActive
                        && e.Student.LifecycleStatus == StudentLifecycleStatus.Active)
                    .Select(e => e.Student.UserId!.Value)
                    .Distinct()
                    .ToList();
            }

            if (studentIds.Count == 0) return;

            await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
            {
                UserIds = studentIds,
                Title = "امتحان جديد",
                Body = $"تم نشر الامتحان: {exam.Title}",
                Type = NotificationType.Exam
            });

            // كمان نبعت إشعار لأولياء الأمور
            var parentIds = new List<int>();
            var allEnrollments = await _unitOfWork.StudentEnrollments
                .GetActiveByStudentsAndYearAsync(studentIds, currentYear.Id);
            foreach (var enrollment in allEnrollments.DistinctBy(e => e.StudentId))
            {
                var parentLinks = await _unitOfWork.ParentStudents
                    .FindAsync(ps => ps.StudentId == enrollment.StudentId);
                parentIds.AddRange(parentLinks.Select(ps => ps.ParentId));
            }
            parentIds = parentIds.Distinct().ToList();
            if (parentIds.Count == 0) return;

            await _notificationService.SendBulkNotificationAsync(new SendBulkNotificationRequest
            {
                UserIds = parentIds,
                Title = "امتحان جديد",
                Body = $"تم نشر الامتحان: {exam.Title}",
                Type = NotificationType.Exam
            });
        }
    }
}
