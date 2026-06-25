using System.Text.Json;
using AutoMapper;
using Common.Results;
using Project.BLL.AI.Interfaces;
using Project.BLL.DTOs.ExamAttempt;
using Project.BLL.Interfaces;
using Project.BLL.Utils;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services
{
    public class ExamAttemptService : IExamAttemptService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILLMRouter _llmRouter;

        public ExamAttemptService(IUnitOfWork unitOfWork, IMapper mapper, ILLMRouter llmRouter)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _llmRouter = llmRouter;
        }

        public async Task<OperationResult<GetExamAttemptDto>> GetByIdAsync(int id)
        {
            var attempt = await _unitOfWork.StudentExamAttempts
                .GetWithAnswersAsync(id, CancellationToken.None);

            if (attempt == null || attempt.IsDeleted)
                return OperationResult<GetExamAttemptDto>.Failure("المحاولة غير موجودة", 404);

            var dto = _mapper.Map<GetExamAttemptDto>(attempt);
            return OperationResult<GetExamAttemptDto>.Success(dto);
        }

        public async Task<OperationResult<List<ExamAttemptSummaryDto>>> GetByExamIdAsync(int examId, int teacherId)
        {
            var exam = await _unitOfWork.Exams.GetByIdAsync(examId);

            if (exam == null || exam.IsDeleted)
                return OperationResult<List<ExamAttemptSummaryDto>>.Failure("الامتحان غير موجود", 404);

            // فحص الملكية/الصلاحية: CST محدد → صاحبه، CST=null → من يُدرّس المادة
            var authorized = await IsTeacherAuthorizedForExamAsync(exam, teacherId);
            if (!authorized)
                return OperationResult<List<ExamAttemptSummaryDto>>.Failure("غير مصرح لك بعرض نتائج هذا الامتحان", 403);

            var attempts = await _unitOfWork.StudentExamAttempts
                .GetByExamIdAsync(examId, CancellationToken.None);

            var dtos = _mapper.Map<List<ExamAttemptSummaryDto>>(attempts);
            return OperationResult<List<ExamAttemptSummaryDto>>.Success(dtos);
        }

        public async Task<OperationResult<GetExamAttemptDto>> StartAttemptAsync(CreateExamAttemptDto dto)
        {
            var enrollment = await _unitOfWork.StudentEnrollments
                .GetByIdAsync(dto.EnrollmentId);

            if (enrollment == null || enrollment.IsDeleted)
                return OperationResult<GetExamAttemptDto>.Failure("التسجيل غير موجود", 404);

            var exam = await _unitOfWork.Exams.GetByIdAsync(dto.ExamId);

            if (exam == null || exam.IsDeleted)
                return OperationResult<GetExamAttemptDto>.Failure("الامتحان غير موجود", 404);

            if (!exam.IsPublished)
                return OperationResult<GetExamAttemptDto>.Failure("الامتحان غير منشور", 400);

            var now = DateTime.UtcNow;
            if (exam.StartTime.HasValue && now < exam.StartTime)
                return OperationResult<GetExamAttemptDto>.Failure("الامتحان لم يبدأ بعد", 400);

            if (exam.EndTime.HasValue && now > exam.EndTime)
                return OperationResult<GetExamAttemptDto>.Failure("الامتحان قد انتهى بالفعل", 400);

            var alreadyAttempted = await _unitOfWork.StudentExamAttempts
                .HasAttemptedAsync(dto.EnrollmentId, dto.ExamId, CancellationToken.None);

            if (alreadyAttempted)
                return OperationResult<GetExamAttemptDto>.Failure("محاولة لهذا الامتحان موجودة بالفعل", 400);

            var attempt = new StudentExamAttempt
            {
                EnrollmentId = dto.EnrollmentId,
                ExamId = dto.ExamId,
                TotalScore = exam.TotalScore,
                StartedAt = now
            };

            await _unitOfWork.StudentExamAttempts.AddAsync(attempt);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            var resultDto = _mapper.Map<GetExamAttemptDto>(attempt);
            return OperationResult<GetExamAttemptDto>.Success(resultDto, "تم بدء المحاولة بنجاح");
        }

        public async Task<OperationResult<GetExamAttemptDto>> SubmitAttemptAsync(SubmitExamAttemptDto dto)
        {
            var attempt = await _unitOfWork.StudentExamAttempts
                .GetWithAnswersAsync(dto.AttemptId, CancellationToken.None);

            if (attempt == null || attempt.IsDeleted)
                return OperationResult<GetExamAttemptDto>.Failure("المحاولة غير موجودة", 404);

            if (attempt.SubmittedAt != null)
                return OperationResult<GetExamAttemptDto>.Failure("تم تقديم المحاولة بالفعل", 400);

            var exam = await _unitOfWork.Exams
                .GetWithQuestionsAsync(attempt.ExamId, CancellationToken.None);

            if (exam == null || exam.IsDeleted)
                return OperationResult<GetExamAttemptDto>.Failure("الامتحان غير موجود", 404);

            // check time limit
            if (exam.DurationMinutes.HasValue)
            {
                var elapsed = (DateTime.UtcNow - attempt.StartedAt).TotalMinutes;
                if (elapsed > exam.DurationMinutes.Value)
                    return OperationResult<GetExamAttemptDto>.Failure("تم تجاوز الوقت المحدد للامتحان", 400);
            }

            // save answers
            foreach (var answerDto in dto.Answers)
            {
                var question = exam.Questions
                    .FirstOrDefault(q => q.Id == answerDto.QuestionId && !q.IsDeleted);

                if (question == null) continue;

                var answer = new StudentExamAnswer
                {
                    AttemptId = attempt.Id,
                    QuestionId = question.Id,
                    AnswerText = answerDto.AnswerText,
                    SelectedOptionId = answerDto.SelectedOptionId,
                    BooleanAnswer = answerDto.BooleanAnswer
                };

                await _unitOfWork.StudentExamAnswers.AddAsync(answer);
            }

            attempt.SubmittedAt = DateTime.UtcNow;

            _unitOfWork.StudentExamAttempts.Update(attempt);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            // reload with answers
            var updatedAttempt = await _unitOfWork.StudentExamAttempts
                .GetWithAnswersAsync(attempt.Id, CancellationToken.None);

            var resultDto = _mapper.Map<GetExamAttemptDto>(updatedAttempt!);
            return OperationResult<GetExamAttemptDto>.Success(resultDto, "تم تقديم الامتحان بنجاح");
        }

        public async Task<OperationResult<List<ExamAttemptSummaryDto>>> GetStudentAttemptsAsync(int enrollmentId, int examId)
    {
        var enrollment = await _unitOfWork.StudentEnrollments.GetByIdAsync(enrollmentId);
        if (enrollment == null || enrollment.IsDeleted)
            return OperationResult<List<ExamAttemptSummaryDto>>.Failure("التسجيل غير موجود", 404);

        var allAttempts = await _unitOfWork.StudentExamAttempts.GetByEnrollmentIdAsync(enrollmentId);
        var filtered = allAttempts.Where(a => a.ExamId == examId && !a.IsDeleted).ToList();

        var dtos = _mapper.Map<List<ExamAttemptSummaryDto>>(filtered);
        return OperationResult<List<ExamAttemptSummaryDto>>.Success(dtos, "تم جلب محاولات الطالب بنجاح");
    }

    public async Task<OperationResult> AutoGradeAsync(int attemptId)
    {
        var attempt = await _unitOfWork.StudentExamAttempts
            .GetWithAnswersAsync(attemptId, CancellationToken.None);

        if (attempt == null || attempt.IsDeleted)
            return OperationResult.Failure("المحاولة غير موجودة", 404);

        if (attempt.SubmittedAt == null)
            return OperationResult.Failure("لم يتم تقديم المحاولة بعد");

        if (attempt.IsGraded)
            return OperationResult.Failure("تم تصحيح المحاولة بالفعل");

        var exam = await _unitOfWork.Exams.GetWithQuestionsAsync(attempt.ExamId, CancellationToken.None);
        if (exam == null || exam.IsDeleted)
            return OperationResult.Failure("الامتحان غير موجود", 404);

        decimal totalScore = 0;
        // عَلَّم: هل فيه إجابة أكمل-فراغ غير مطابقة نصياً وتنتظر مراجعة يدوية؟
        bool hasPendingFillBlank = false;

        foreach (var answer in attempt.Answers)
        {
            var question = exam.Questions.FirstOrDefault(q => q.Id == answer.QuestionId);
            if (question == null) continue;

            if (question.QuestionType == QuestionType.MultipleChoice)
            {
                var isCorrect = !string.IsNullOrEmpty(question.CorrectAnswer) &&
                                answer.AnswerText?.Trim().Equals(question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase) == true;

                answer.IsCorrect = isCorrect;
                answer.PointsEarned = isCorrect ? question.Points : 0;
                totalScore += answer.PointsEarned;
                _unitOfWork.StudentExamAnswers.Update(answer);
            }
            else if (question.QuestionType == QuestionType.TrueFalse)
            {
                // تطبيع كلا الجانبين قبل المقارنة لضمان تطابق "صح"/"True"/"نعم"... إلخ
                var correctBool = BooleanNormalizer.NormalizeBoolean(question.CorrectAnswer);
                var answerBool = BooleanNormalizer.NormalizeBoolean(answer.AnswerText);
                var isCorrect = correctBool.HasValue && answerBool.HasValue && correctBool.Value == answerBool.Value;

                answer.IsCorrect = isCorrect;
                answer.PointsEarned = isCorrect ? question.Points : 0;
                totalScore += answer.PointsEarned;
                _unitOfWork.StudentExamAnswers.Update(answer);
            }
            else if (question.QuestionType == QuestionType.FillBlank)
            {
                // لو الطالب لمّا يكتب إجابة مطابقة تماماً = تصحيح تلقائي (الدرجة كاملة)
                // لو مش مطابقة = نعلّمها بانتظار مراجعة المعلم ولا نعتبرها graded نهائياً
                if (string.IsNullOrEmpty(question.CorrectAnswer))
                {
                    hasPendingFillBlank = true;
                    continue;
                }

                var isMatch = answer.AnswerText?.Trim()
                    .Equals(question.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase) == true;

                if (isMatch)
                {
                    answer.IsCorrect = true;
                    answer.PointsEarned = question.Points;
                    totalScore += answer.PointsEarned;
                    _unitOfWork.StudentExamAnswers.Update(answer);
                }
                else
                {
                    // غير مطابقة نصياً → بانتظار مراجعة المعلم (يبقى IsCorrect=null)
                    hasPendingFillBlank = true;
                }
            }
            // Essay: لا يُصحّح تلقائياً (بانتظار المعلم)
        }

        attempt.Score = totalScore;
        // المرحلة 1: لا نعلّم graded لو فيه fill-blank/essay بانتظار المراجعة اليدوية
        attempt.IsGraded = !hasPendingFillBlank
            && !attempt.Answers
                .Join(exam.Questions, a => a.QuestionId, q => q.Id, (a, q) => new { a, q })
                .Any(x => x.q.QuestionType == QuestionType.Essay && !x.a.IsCorrect.HasValue);
        attempt.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StudentExamAttempts.Update(attempt);
        await _unitOfWork.SaveChangesAsync(CancellationToken.None);

        return OperationResult.Success("تم تصحيح المحاولة تلقائياً بنجاح");
    }

    public async Task<OperationResult> GradeEssayAnswersAsync(int attemptId, GradeEssayAttemptDto dto, int teacherId)
        {
            var attempt = await _unitOfWork.StudentExamAttempts
                .GetWithAnswersAsync(attemptId, CancellationToken.None);

            if (attempt == null || attempt.IsDeleted)
                return OperationResult.Failure("المحاولة غير موجودة", 404);

            if (attempt.SubmittedAt == null)
                return OperationResult.Failure("لم يتم تقديم المحاولة بعد");

            // فحص ملكية/الصلاحية للامتحان (CST محدد → صاحبه، CST=null → من يُدرّس المادة)
            var examForAuth = await _unitOfWork.Exams.GetWithClassSubjectTeacherAsync(attempt.ExamId);
            if (examForAuth == null)
                return OperationResult.Failure("الامتحان غير موجود", 404);

            var authorized = await IsTeacherAuthorizedForExamAsync(examForAuth, teacherId);
            if (!authorized)
                return OperationResult.Failure("غير مصرح لك بتصحيح هذه المحاولة", 403);

            var exam = await _unitOfWork.Exams.GetWithQuestionsAsync(attempt.ExamId, CancellationToken.None);
            if (exam == null) return OperationResult.Failure("الامتحان غير موجود", 404);

            foreach (var ansDto in dto.Answers)
            {
                var answer = attempt.Answers.FirstOrDefault(a => a.Id == ansDto.AnswerId);
                if (answer == null) continue;

                var question = exam.Questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                // نسمح بتصحيح الأسئلة المقالية وأسئلة أكمل-الفراغ يدوياً
                if (question == null
                    || (question.QuestionType != Project.Domain.Enums.QuestionType.Essay
                        && question.QuestionType != Project.Domain.Enums.QuestionType.FillBlank)) continue;

                // منع إدخال درجة أعلى من الحد الأقصى
                var earned = Math.Min(ansDto.PointsEarned, question.Points);
                answer.PointsEarned = earned;
                answer.IsCorrect    = earned > 0;
                answer.AIFeedback   = ansDto.Feedback;   // يُستخدم كملاحظة المعلم
                _unitOfWork.StudentExamAnswers.Update(answer);
            }

            // إعادة حساب مجموع الدرجة
            attempt.Score = attempt.Answers.Sum(a => a.PointsEarned);

            // تحقق: هل لسه أسئلة مقالية/أكمل-فراغ بدون درجة محددة
            var hasUngradedManual = attempt.Answers
                .Join(exam.Questions, a => a.QuestionId, q => q.Id, (a, q) => new { a, q })
                .Any(x => (x.q.QuestionType == Project.Domain.Enums.QuestionType.Essay
                        || x.q.QuestionType == Project.Domain.Enums.QuestionType.FillBlank)
                       && !x.a.IsCorrect.HasValue);

            if (!hasUngradedManual)
                attempt.IsGraded = true;

            attempt.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.StudentExamAttempts.Update(attempt);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            return OperationResult.Success("تم حفظ التصحيح بنجاح");
        }

        /// <summary>
        /// تصحيح الأسئلة المقالية وأكمل-الفراغ باستخدام AI.
        /// يبني برومبت لـ LLM مع كل سؤال + إجابة الطالب + الإجابة النموذجية + الدرجة القصوى
        /// ويطلب اقتراح درجة لكل سؤال مع مبرر مختصر.
        /// </summary>
        public async Task<OperationResult<AiGradeResponseDto>> AiGradeSuggestionsAsync(int attemptId, int teacherId)
        {
            var attempt = await _unitOfWork.StudentExamAttempts
                .GetWithAnswersAsync(attemptId, CancellationToken.None);

            if (attempt == null || attempt.IsDeleted)
                return OperationResult<AiGradeResponseDto>.Failure("المحاولة غير موجودة", 404);

            if (attempt.SubmittedAt == null)
                return OperationResult<AiGradeResponseDto>.Failure("لم يتم تقديم المحاولة بعد");

            // فحص صلاحية المعلم
            var examForAuth = await _unitOfWork.Exams.GetWithClassSubjectTeacherAsync(attempt.ExamId);
            if (examForAuth == null)
                return OperationResult<AiGradeResponseDto>.Failure("الامتحان غير موجود", 404);

            var authorized = await IsTeacherAuthorizedForExamAsync(examForAuth, teacherId);
            if (!authorized)
                return OperationResult<AiGradeResponseDto>.Failure("غير مصرح لك بتصحيح هذه المحاولة", 403);

            var exam = await _unitOfWork.Exams.GetWithQuestionsAsync(attempt.ExamId, CancellationToken.None);
            if (exam == null)
                return OperationResult<AiGradeResponseDto>.Failure("الامتحان غير موجود", 404);

            // تجميع الأسئلة المقالية وأكمل-الفراغ فقط
            var manualQuestions = exam.Questions
                .Where(q => q.QuestionType == QuestionType.Essay || q.QuestionType == QuestionType.FillBlank)
                .ToList();

            if (manualQuestions.Count == 0)
                return OperationResult<AiGradeResponseDto>.Failure("لا توجد أسئلة مقالية أو أكمل-فراغ في هذا الامتحان");

            // بناء قائمة الأسئلة للـ LLM
            var questionsForAi = new List<object>();
            foreach (var question in manualQuestions)
            {
                var answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == question.Id);
                if (answer == null) continue;

                questionsForAi.Add(new
                {
                    answerId = answer.Id,
                    questionText = question.QuestionText,
                    studentAnswer = answer.AnswerText ?? "(لم يجب)",
                    correctAnswer = question.CorrectAnswer ?? "(غير محدد)",
                    maxPoints = (double)question.Points
                });
            }

            if (questionsForAi.Count == 0)
                return OperationResult<AiGradeResponseDto>.Failure("لم يتم العثور على إجابات للتصحيح");

            // بناء البرومبت
            var jsonQuestions = JsonSerializer.Serialize(questionsForAi, new JsonSerializerOptions { WriteIndented = true });

            var systemPrompt = @"أنت معلم خبير في التصحيح والتقييم. مهمتك هي تقييم إجابات الطلاب للأسئلة المقالية وأسئلة أكمل-الفراغ.

لكل سؤال، ستحصل على:
- questionText: نص السؤال
- studentAnswer: إجابة الطالب
- correctAnswer: الإجابة النموذجية
- maxPoints: الدرجة القصوى للسؤال

المطلوب منك:
1. قارن إجابة الطالب بالإجابة النموذجية.
2. قيم مدى صحة الإجابة واكتمالها.
3. اقترح درجة من 0 إلى maxPoints بناءً على جودة الإجابة.
4. قدم مبرراً مختصراً جداً بالعربية للتقييم (جملة واحدة).

يجب أن يكون الرد بصيغة JSON Array فقط:
[
  {
    ""answerId"": رقم الإجابة,
    ""suggestedPoints"": الدرجة المقترحة (رقم عشري),
    ""justification"": ""المبرر بالعربية""
  }
]

لا تضف أي نص خارج الـ JSON.
كن دقيقاً ومنصفاً في التقييم. الطالب الذي أجاب إجابة صحيحة كاملة يستحق الدرجة كاملة.";
            var userMessage = $"قم بتقييم الإجابات التالية:\n\n{jsonQuestions}";

            string llmResponse;
            try
            {
                llmResponse = await _llmRouter.GenerateAsync(systemPrompt, userMessage, preferredProvider: null, ct: CancellationToken.None);
            }
            catch (Exception ex)
            {
                return OperationResult<AiGradeResponseDto>.Failure($"فشل الاتصال بخدمة الذكاء الاصطناعي: {ex.Message}", 500);
            }

            // محاولة استخراج JSON من الرد
            var json = llmResponse.Trim();
            // قد يحيط LLM الرد بـ ```json ... ```
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('\n');
                var end = json.LastIndexOf("```");
                if (start > 0 && end > start)
                    json = json[(start + 1)..end].Trim();
            }

            List<AiGradeSuggestionDto>? suggestions;
            try
            {
                suggestions = JsonSerializer.Deserialize<List<AiGradeSuggestionDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return OperationResult<AiGradeResponseDto>.Failure("تعذر تحليل رد الذكاء الاصطناعي، حاول مرة أخرى", 500);
            }

            if (suggestions == null || suggestions.Count == 0)
                return OperationResult<AiGradeResponseDto>.Failure("لم يتمكن الذكاء الاصطناعي من تقديم اقتراحات، حاول مرة أخرى", 500);

            // التحقق من صحة الدرجات (لا تتجاوز maxPoints)
            foreach (var suggestion in suggestions)
            {
                var answer = attempt.Answers.FirstOrDefault(a => a.Id == suggestion.AnswerId);
                if (answer == null) continue;
                var question = exam.Questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                if (question == null) continue;

                if (suggestion.SuggestedPoints < 0)
                    suggestion.SuggestedPoints = 0;
                if (suggestion.SuggestedPoints > (decimal)question.Points)
                    suggestion.SuggestedPoints = (decimal)question.Points;
            }

            var result = new AiGradeResponseDto
            {
                AttemptId = attemptId,
                Suggestions = suggestions
            };

            return OperationResult<AiGradeResponseDto>.Success(result, "تم الحصول على اقتراحات التصحيح بنجاح");
        }


        ///   - CST موجود → المعلم صاحب الـ CST.
        ///   - CST=null (نشر للصف) → المعلم يُدرّس المادة (SubjectId).
        /// </summary>
        private async Task<bool> IsTeacherAuthorizedForExamAsync(Project.Domain.Entities.Exam exam, int teacherId)
        {
            // امتحان مربوط بفصل محدد
            if (exam.ClassSubjectTeacher is not null)
                return exam.ClassSubjectTeacher.TeacherId == teacherId;

            if (exam.ClassSubjectTeacherId.HasValue)
            {
                var cst = await _unitOfWork.ClassSubjectTeachers.GetByIdAsync(exam.ClassSubjectTeacherId.Value);
                return cst is not null && !cst.IsDeleted && cst.TeacherId == teacherId;
            }

            // امتحان CST=null (نشر للصف كله) → المعلم لازم يُدرّس المادة
            if (exam.SubjectId.HasValue)
            {
                var csts = await _unitOfWork.ClassSubjectTeachers
                    .FindAsync(c => c.SubjectId == exam.SubjectId.Value
                                 && c.TeacherId == teacherId
                                 && !c.IsDeleted, CancellationToken.None);
                return csts.Count > 0;
            }

            return false;
        }

        // Legacy — kept to avoid breaking the auto-grade endpoint; candidates for removal
        public async Task<OperationResult> GradeAttemptAsync_Legacy(int attemptId)
        {
            var attempt = await _unitOfWork.StudentExamAttempts
                .GetWithAnswersAsync(attemptId, CancellationToken.None);

            if (attempt == null || attempt.IsDeleted)
                return OperationResult.Failure("المحاولة غير موجودة");

            if (attempt.SubmittedAt == null)
                return OperationResult.Failure("لم يتم تقديم المحاولة بعد");

            if (attempt.IsGraded)
                return OperationResult.Failure("تم تصحيح المحاولة بالفعل");

            decimal totalScore = 0;

            foreach (var answer in attempt.Answers)
            {
                if (answer.Question == null) continue;

                var isCorrect = false;
                if (answer.Question.QuestionType == QuestionType.TrueFalse)
                {
                    // تطبيع كلا الجانبين للأسئلة صح/خطأ
                    var correctBool = BooleanNormalizer.NormalizeBoolean(answer.Question.CorrectAnswer);
                    var answerBool = BooleanNormalizer.NormalizeBoolean(answer.AnswerText);
                    isCorrect = correctBool.HasValue && answerBool.HasValue && correctBool.Value == answerBool.Value;
                }
                else
                {
                    // باقي الأنواع: مقارنة نصية مباشرة
                    isCorrect = !string.IsNullOrEmpty(answer.Question.CorrectAnswer) &&
                                answer.AnswerText?.Trim().ToLower() ==
                                answer.Question.CorrectAnswer.Trim().ToLower();
                }

                answer.IsCorrect = isCorrect;
                answer.PointsEarned = isCorrect ? answer.Question.Points : 0;
                totalScore += answer.PointsEarned;

                _unitOfWork.StudentExamAnswers.Update(answer);
            }

            attempt.Score = totalScore;
            attempt.IsGraded = true;
            attempt.UpdatedAt = DateTime.UtcNow;

            _unitOfWork.StudentExamAttempts.Update(attempt);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);

            return OperationResult.Success("تم تصحيح المحاولة بنجاح");
        }
    }
}