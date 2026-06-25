using System.Text.Json;
using Project.BLL.AI.Interfaces;
using Project.BLL.AI.Models;
using Project.BLL.DTOs;
using Project.BLL.DTOs.Exam;
using Project.BLL.DTOs.QuestionBank;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.BLL.Services;
using Project.Domain.Enums;

namespace Project.BLL.AI.Tools;

public class TeacherToolService : ITeacherToolService
{
    private readonly ISubjectService _subjectService;
    private readonly ILessonRepository _lessonRepo;
    private readonly IExamService _examService;
    private readonly IAiExamGeneratorService _aiExamGen;
    private readonly IQuestionBankService _questionBankService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUnitService _unitService;
    private readonly IStudentEvaluationService _evalService;
    private readonly IQuestionEmbeddingService _questionEmbeddingService;

    public TeacherToolService(
        ISubjectService subjectService,
        ILessonRepository lessonRepo,
        IExamService examService,
        IAiExamGeneratorService aiExamGen,
        IQuestionBankService questionBankService,
        IUnitOfWork unitOfWork,
        IUnitService unitService,
        IStudentEvaluationService evalService,
        IQuestionEmbeddingService questionEmbeddingService)
    {
        _subjectService = subjectService;
        _lessonRepo = lessonRepo;
        _examService = examService;
        _aiExamGen = aiExamGen;
        _questionBankService = questionBankService;
        _unitOfWork = unitOfWork;
        _unitService = unitService;
        _evalService = evalService;
        _questionEmbeddingService = questionEmbeddingService;
    }

    public Dictionary<string, AiTool> CreateTools(UserContext context, CancellationToken ct = default)
    {
        var list = new List<AiTool>();

        if (context.TeacherId.HasValue && context.AcademicYearId.HasValue)
        {
            var tId = context.TeacherId.Value;
            var yId = context.AcademicYearId.Value;

            list.Add(new AiTool
            {
                Name = "get_subjects",
                Description = "جلب المواد المتاحة للمدرس (المواد اللي بيدرسها المدرس فقط)",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new object(),
                    required = Array.Empty<string>()
                }),
                ExecuteAsync = async (args) =>
                {
                    var result = await _subjectService.GetSubjectsByTeacherAsync(tId, yId);
                    return JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = true });
                }
            });

            list.Add(new AiTool
            {
                Name = "get_classes",
                Description = "جلب الفصول المتاحة للمدرس (الفصول التي يدرسها المدرس).",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new object(),
                    required = Array.Empty<string>()
                }),
                ExecuteAsync = async (args) =>
                {
                    var csts = await _unitOfWork.ClassSubjectTeachers.FindAsync(cst =>
                        cst.TeacherId == tId && cst.AcademicYearId == yId && !cst.IsDeleted);
                    var classIds = csts.Select(c => c.ClassId).Distinct().ToList();
                    var allClasses = await _unitOfWork.Classes.FindAsync(c => classIds.Contains(c.Id) && !c.IsDeleted);
                    var result = allClasses.Select(c => new { classId = c.Id, className = c.Name }).ToList();
                    return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                }
            });

            list.Add(new AiTool
            {
                Name = "get_my_grade_levels",
                Description = "جلب الصفوف الدراسية التي يدرسها المدرس (مثلاً: الأول الإعدادي، الثاني الإعدادي). استخدمها لمعرفة gradeLevelId المطلوب.",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new object(),
                    required = Array.Empty<string>()
                }),
                ExecuteAsync = async (args) =>
                {
                    var csts = await _unitOfWork.ClassSubjectTeachers.FindAsync(cst =>
                        cst.TeacherId == tId && cst.AcademicYearId == yId && !cst.IsDeleted);
                    var classIds = csts.Select(c => c.ClassId).Distinct().ToList();
                    var allClasses = await _unitOfWork.Classes.FindAsync(c => classIds.Contains(c.Id) && !c.IsDeleted);
                    var gradeLevelIds = allClasses.Select(c => c.GradeLevelId).Distinct().ToList();
                    var gradeLevels = await _unitOfWork.GradeLevels.FindAsync(g => gradeLevelIds.Contains(g.Id) && !g.IsDeleted);
                    var result = gradeLevels.Select(g => new { id = g.Id, name = g.Name }).ToList();
                    return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                }
            });

            list.Add(new AiTool
            {
                Name = "get_evaluation_periods",
                Description = "جلب فترات التقييم المتاحة للسنة الدراسية. يمكن تحديد الفصل الدراسي (term) لتصفية النتائج.",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        term = new { type = "integer", description = "الفصل الدراسي (اختياري): 1 = الترم الأول, 2 = الترم الثاني, 3 = النهائي (افتراضي = الترم الحالي)" }
                    },
                    required = Array.Empty<string>()
                }),
                ExecuteAsync = async (args) =>
                {
                    var periods = await _unitOfWork.EvaluationPeriods.GetOrderedByYearAsync(yId);

                    AcademicTerm? term = context.CurrentTerm;
                    using var doc = JsonDocument.Parse(args);
                    if (doc.RootElement.TryGetProperty("term", out var termEl) && termEl.ValueKind == JsonValueKind.Number)
                        term = (AcademicTerm)termEl.GetInt32();

                    var result = periods
                        .Where(p => !p.IsDeleted)
                        .Where(p => !term.HasValue || (p.SemesterNumber.HasValue && p.SemesterNumber.Value == (int)term.Value))
                        .Select(p => new
                        {
                            id = p.Id,
                            name = p.Name,
                            type = p.PeriodType.ToString(),
                            order = p.OrderNum,
                            semesterNumber = p.SemesterNumber
                        }).ToList();
                    return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                }
            });

            list.Add(new AiTool
            {
                Name = "get_class_evaluations",
                Description = "جلب تقييمات الطلاب لفصل معين وفترة تقييم محددة (غياب، سلوك، واجبات، تفاعل)",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        classId = new { type = "integer", description = "معرف الفصل (من get_classes)" },
                        periodId = new { type = "integer", description = "معرف فترة التقييم (من get_evaluation_periods)" },
                        term = new { type = "integer", description = "الفصل الدراسي (اختياري): 1 = الترم الأول, 2 = الترم الثاني, 3 = النهائي (افتراضي = الترم الحالي)" }
                    },
                    required = new[] { "classId", "periodId" }
                }),
                ExecuteAsync = async (args) =>
                {
                    using var doc = JsonDocument.Parse(args);
                    var classId = doc.RootElement.GetProperty("classId").GetInt32();
                    var periodId = doc.RootElement.GetProperty("periodId").GetInt32();

                    AcademicTerm? term = context.CurrentTerm;
                    if (doc.RootElement.TryGetProperty("term", out var termEl) && termEl.ValueKind == JsonValueKind.Number)
                        term = (AcademicTerm)termEl.GetInt32();

                    var result = await _evalService.GetByClassAndPeriodAsync(classId, periodId, term);

                    // فلترة التقييمات — نعرض بس مواد المدرس في الفصل ده
                    if (result.IsSuccess && result.Data is not null)
                    {
                        // نجيب المواد اللي المدرس بيدرسها في الفصل ده
                        var cstList = await _unitOfWork.ClassSubjectTeachers.FindAsync(cst =>
                            cst.TeacherId == tId &&
                            cst.ClassId == classId &&
                            cst.AcademicYearId == yId &&
                            !cst.IsDeleted);

                        var subjectIds = cstList.Select(c => c.SubjectId).Distinct().ToList();
                        if (subjectIds.Count > 0)
                        {
                            var subjects = await _unitOfWork.Subjects.FindAsync(s => subjectIds.Contains(s.Id) && !s.IsDeleted);
                            var subjectNames = subjects.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet();

                            var filtered = result.Data
                                .Select(c => new Project.BLL.DTOs.StudentEvaluations.ClassEvaluationDto
                                {
                                    EnrollmentId = c.EnrollmentId,
                                    StudentName = c.StudentName,
                                    Evaluations = c.Evaluations.Where(e =>
                                        e.SubjectName is not null && subjectNames.Contains(e.SubjectName))
                                })
                                .Where(c => c.Evaluations.Any())
                                .ToList();

                            return JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true });
                        }
                    }

                    return JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = true });
                }
            });
        }

            list.Add(new AiTool
            {
                Name = "get_units",
                Description = "جلب أسماء الوحدات والدروس لمادة معينة (بدون المحتوى — فقط للتصفح والاختيار). بعدها استخدم get_unit_content لجلب محتوى وحدة محددة.",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        subjectId = new { type = "integer", description = "معرف المادة (من get_subjects)" },
                        gradeLevelId = new { type = "integer", description = "معرف الصف الدراسي (اختياري)" }
                    },
                    required = new[] { "subjectId" }
                }),
                ExecuteAsync = async (args) =>
                {
                    using var doc = JsonDocument.Parse(args);
                    var subjectId = doc.RootElement.GetProperty("subjectId").GetInt32();
                    var hasGradeLevel = doc.RootElement.TryGetProperty("gradeLevelId", out var glEl) && glEl.ValueKind == JsonValueKind.Number;
                    var gradeLevelId = hasGradeLevel ? glEl.GetInt32() : 0;
                    var result = hasGradeLevel
                        ? await _unitService.GetUnitsByGradeLevelAndSubjectAsync(gradeLevelId, subjectId, context.CurrentTerm)
                        : await _unitService.GetUnitsWithLessonsBySubjectAsync(subjectId, context.CurrentTerm);
                    // إرجاع الأسماء فقط بدون محتوى للحفاظ على حجم الـ context
                    var light = (result.Data ?? []).Select(u => new
                    {
                        id = u.Id,
                        name = u.Name,
                        gradeLevelId = u.GradeLevelId,
                        hasContent = !string.IsNullOrWhiteSpace(u.Content) ||
                                     (u.Lessons is not null && u.Lessons.Any(l => !string.IsNullOrWhiteSpace(l.Content))),
                        lessons = (u.Lessons ?? []).Select(l => new { id = l.Id, title = l.Title }).ToList()
                    }).ToList();
                    return JsonSerializer.Serialize(light, new JsonSerializerOptions { WriteIndented = true });
                }
            });

            list.Add(new AiTool
            {
                Name = "get_unit_content",
                Description = "جلب المحتوى الكامل لوحدة معينة (بما في ذلك محتوى الوحدة ودروسها). استخدم get_units أولاً لمعرفة unitId.",
                Parameters = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        subjectId = new { type = "integer", description = "معرف المادة (من get_subjects)" },
                        unitId = new { type = "integer", description = "معرف الوحدة (من get_units)" },
                        gradeLevelId = new { type = "integer", description = "معرف الصف الدراسي (اختياري — لتصفية الوحدات)" }
                    },
                    required = new[] { "subjectId", "unitId" }
                }),
                ExecuteAsync = async (args) =>
                {
                    using var doc = JsonDocument.Parse(args);
                    var subjectId = doc.RootElement.GetProperty("subjectId").GetInt32();
                    var unitId = doc.RootElement.GetProperty("unitId").GetInt32();

                    var hasGradeLevel = doc.RootElement.TryGetProperty("gradeLevelId", out var glEl) && glEl.ValueKind == JsonValueKind.Number;
                    var gradeLevelId = hasGradeLevel ? glEl.GetInt32() : 0;

                    List<UnitDto>? unitsData;
                    if (hasGradeLevel)
                    {
                        var result = await _unitService.GetUnitsByGradeLevelAndSubjectAsync(gradeLevelId, subjectId, context.CurrentTerm);
                        unitsData = result.Data;
                    }
                    else
                    {
                        var result = await _unitService.GetUnitsWithLessonsBySubjectAsync(subjectId, context.CurrentTerm);
                        unitsData = result.Data;
                    }

                    var unit = (unitsData ?? []).FirstOrDefault(u => u.Id == unitId);
                    if (unit is null)
                        return JsonSerializer.Serialize(new { error = "الوحدة غير موجودة" });
                    return JsonSerializer.Serialize(unit, new JsonSerializerOptions { WriteIndented = true });
                }
            });

        list.Add(new AiTool
        {
            Name = "get_lessons",
            Description = "جلب دروس مادة معينة (بحث باسم المادة)",
            Parameters = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    subject = new { type = "string", description = "اسم المادة للبحث (مثل: رياضيات)" }
                },
                required = new[] { "subject" }
            }),
            ExecuteAsync = async (args) =>
            {
                using var doc = JsonDocument.Parse(args);
                var subject = doc.RootElement.GetProperty("subject").GetString() ?? "";
                var lessons = await _lessonRepo.SearchAsync(subject);
                return JsonSerializer.Serialize(lessons, new JsonSerializerOptions { WriteIndented = true });
            }
        });

        list.Add(new AiTool
        {
            Name = "update_lesson",
            Description = "تعديل محتوى درس موجود",
            Parameters = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    lessonId = new { type = "integer", description = "معرف الدرس" },
                    title = new { type = "string", description = "العنوان الجديد" },
                    content = new { type = "string", description = "المحتوى الجديد" }
                },
                required = new[] { "lessonId", "title", "content" }
            }),
            ExecuteAsync = async (args) =>
            {
                using var doc = JsonDocument.Parse(args);
                var id = doc.RootElement.GetProperty("lessonId").GetInt32();
                var title = doc.RootElement.GetProperty("title").GetString() ?? "";
                var content = doc.RootElement.GetProperty("content").GetString() ?? "";
                var ok = await _lessonRepo.UpdateAsync(id, title, content);
                return JsonSerializer.Serialize(new { success = ok });
            }
        });

        list.Add(new AiTool
        {
            Name = "generate_exam_with_ai",
            Description = "توليد أسئلة امتحان بالذكاء الاصطناعي بناءً على المحتوى الدراسي للوحدة/الدروس وحفظها مباشرة. استخدم get_subjects أولاً لمعرفة subjectId.",
            Parameters = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    subjectId = new { type = "integer", description = "معرف المادة (من get_subjects) — إجباري" },
                    classSubjectTeacherId = new { type = "integer", description = "معرف المادة-الفصل-المدرس (اختياري — لو محططش، الامتحان مش بيتقيد بفصل)" },
                    gradeLevelId = new { type = "integer", description = "معرف الصف الدراسي (اختياري — إذا لم تحدد، سيتم استنتاجه تلقائياً من الفصل إن وجد أو من موادك)" },
                    title = new { type = "string", description = "عنوان الامتحان" },
                    mcqCount = new { type = "integer", description = "عدد أسئلة الاختيار من متعدد (اختياري، افتراضي 5)" },
                    trueFalseCount = new { type = "integer", description = "عدد أسئلة صح/خطأ (اختياري، افتراضي 0)" },
                    fillBlankCount = new { type = "integer", description = "عدد أسئلة أكمل الفراغ (اختياري، افتراضي 0)" },
                    essayCount = new { type = "integer", description = "عدد الأسئلة المقالية (اختياري، افتراضي 0)" },
                    totalScore = new { type = "number", description = "الدرجة الكلية (اختياري، افتراضي 100)" },
                    durationMinutes = new { type = "integer", description = "المدة بالدقائق (اختياري، افتراضي 60)" },
                    unitId = new { type = "integer", description = "معرف الوحدة (اختياري)" },
                    lessonIds = new { type = "array", items = new { type = "integer" }, description = "مصفوفة معرفات الدروس (اختياري)" },
                    topic = new { type = "string", description = "موضوع محدد للامتحان (اختياري)" }
                },
                required = new[] { "title", "subjectId" }
            }),
            ExecuteAsync = async (args) =>
            {
                using var doc = JsonDocument.Parse(args);

                int? cstId = null;
                int? resolvedSubjectId = null;
                if (doc.RootElement.TryGetProperty("classSubjectTeacherId", out var cstEl) && cstEl.ValueKind == JsonValueKind.Number)
                {
                    cstId = cstEl.GetInt32();
                }
                else if (doc.RootElement.TryGetProperty("subjectId", out var subjEl) && subjEl.ValueKind == JsonValueKind.Number)
                {
                    resolvedSubjectId = subjEl.GetInt32();
                }
                else
                {
                    return JsonSerializer.Serialize(new { error = "يجب توفير subjectId أو classSubjectTeacherId" });
                }

                var title = doc.RootElement.GetProperty("title").GetString() ?? "امتحان من AI";

                var questionCounts = new Dictionary<int, int>
                {
                    [1] = doc.RootElement.TryGetProperty("mcqCount", out var mcq) ? mcq.GetInt32() : 5,
                    [2] = doc.RootElement.TryGetProperty("trueFalseCount", out var tf) ? tf.GetInt32() : 0,
                    [3] = doc.RootElement.TryGetProperty("fillBlankCount", out var fb) ? fb.GetInt32() : 0,
                    [4] = doc.RootElement.TryGetProperty("essayCount", out var essay) ? essay.GetInt32() : 0
                };

                var totalScore = doc.RootElement.TryGetProperty("totalScore", out var ts) ? ts.GetDecimal() : 100m;
                var durationMinutes = doc.RootElement.TryGetProperty("durationMinutes", out var dur) ? dur.GetInt32() : 60;

                int? unitId = null;
                if (doc.RootElement.TryGetProperty("unitId", out var uid) && uid.ValueKind == JsonValueKind.Number)
                    unitId = uid.GetInt32();

                var lessonIds = new List<int>();
                if (doc.RootElement.TryGetProperty("lessonIds", out var lids) && lids.ValueKind == JsonValueKind.Array)
                    lessonIds = lids.EnumerateArray().Select(x => x.GetInt32()).ToList();

                var topic = doc.RootElement.TryGetProperty("topic", out var top) ? top.GetString() : null;

                var gradeLevelId = doc.RootElement.TryGetProperty("gradeLevelId", out var gl) ? gl.GetInt32() : 0;

                // Auto-resolve gradeLevelId if not provided
                if (gradeLevelId == 0)
                {
                    if (cstId.HasValue)
                    {
                        var cst = await _unitOfWork.ClassSubjectTeachers.GetByIdAsync(cstId.Value);
                        if (cst is not null)
                        {
                            var cls = await _unitOfWork.Classes.GetByIdAsync(cst.ClassId);
                            if (cls is not null)
                                gradeLevelId = cls.GradeLevelId;
                        }
                    }
                    else if (resolvedSubjectId.HasValue && context.TeacherId.HasValue && context.AcademicYearId.HasValue)
                    {
                        var cstsForSubject = await _unitOfWork.ClassSubjectTeachers.FindAsync(cst =>
                            cst.TeacherId == context.TeacherId.Value &&
                            cst.AcademicYearId == context.AcademicYearId.Value &&
                            cst.SubjectId == resolvedSubjectId.Value && !cst.IsDeleted);

                        if (cstsForSubject.Count > 0)
                        {
                            var validClasses = new List<Project.Domain.Entities.SchoolClass>();
                            foreach (var classId in cstsForSubject.Select(cst => cst.ClassId).Distinct())
                            {
                                var cls = await _unitOfWork.Classes.GetByIdAsync(classId);
                                if (cls is not null)
                                    validClasses.Add(cls);
                            }

                            if (validClasses.Count > 0)
                            {
                                gradeLevelId = validClasses[0]!.GradeLevelId;
                            }
                        }

                        // لو ملاقيش CST للمادة (لأن المدرس اختار مادة مش بتاعته من get_subjects)
                        // نحاول نستنتج gradeLevelId من أي فصل المدرس بيدرسه
                        if (gradeLevelId == 0)
                        {
                            var allCsts = await _unitOfWork.ClassSubjectTeachers.FindAsync(cst =>
                                cst.TeacherId == context.TeacherId.Value &&
                                cst.AcademicYearId == context.AcademicYearId.Value && !cst.IsDeleted);
                            var firstClassId = allCsts.Select(c => c.ClassId).FirstOrDefault();
                            if (firstClassId > 0)
                            {
                                var cls = await _unitOfWork.Classes.GetByIdAsync(firstClassId);
                                if (cls is not null)
                                    gradeLevelId = cls.GradeLevelId;
                            }
                        }
                    }
                }

                if (gradeLevelId == 0)
                {
                    return JsonSerializer.Serialize(new { error = "لم نتمكن من تحديد الصف الدراسي تلقائياً. الرجاء إعادة المحاولة مع تحديد الصف يدوياً: \n" +
                        $"استخدم get_my_grade_levels أولاً لرؤية الصفوف المتاحة، ثم أعد المحاولة مع إضافة gradeLevelId." });
                }

                var request = new AiGenerateExamRequest
                {
                    ClassSubjectTeacherId = cstId,
                    SubjectId = resolvedSubjectId,
                    GradeLevelId = gradeLevelId,
                    Title = title,
                    DurationMinutes = durationMinutes,
                    TotalScore = totalScore,
                    Category = Domain.Enums.EvaluationCategory.Training,
                    QuestionCounts = questionCounts,
                    Topic = topic,
                    UnitId = unitId,
                    LessonIds = lessonIds
                };

                var savedExam = await _aiExamGen.GenerateExamAsync(request, ct);
                if (!savedExam.IsSuccess || savedExam.Data is null)
                    return JsonSerializer.Serialize(new { error = savedExam.Message ?? "فشل إنشاء الامتحان" });

                var exam = savedExam.Data;

                // Check if curriculum content exists
                string contentNote = "";
                if (resolvedSubjectId.HasValue)
                {
                    var unitResult = gradeLevelId > 0
                        ? await _unitService.GetUnitsByGradeLevelAndSubjectAsync(gradeLevelId, resolvedSubjectId.Value, context.CurrentTerm)
                        : await _unitService.GetUnitsWithLessonsBySubjectAsync(resolvedSubjectId.Value, context.CurrentTerm);
                    var hasContent = unitResult.IsSuccess && unitResult.Data is { Count: > 0 };
                    if (!hasContent)
                        contentNote = "\n⚠️ ملاحظة: لم يتم العثور على المحتوى الدراسي للدرس في قاعدة البيانات. تم إنشاء الأسئلة بناءً على معرفتي بالمنهج الدراسي.";
                }

                return JsonSerializer.Serialize(new
                {
                    examId = exam.Id,
                    title = exam.Title,
                    totalScore = exam.TotalScore,
                    questionsCount = exam.QuestionsCount,
                    viewUrl = $"/api/exam/{exam.Uid}/html",
                    message = $"تم إنشاء الامتحان وحفظه بنجاح. يمكنك معاينته من الرابط أعلاه.{contentNote}"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        });

        list.Add(new AiTool
        {
            Name = "save_to_question_bank",
            Description = "حفظ سؤال واحد أو أكثر في بنك الأسئلة (الموضوع مطلوب). يستخدم لتجميع الأسئلة في بنك مركزي لاستخدامها لاحقاً.",
            Parameters = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    subjectId = new { type = "integer", description = "معرف المادة (من get_subjects: الـ subjectId)" },
                    gradeLevelId = new { type = "integer", description = "معرف الصف الدراسي (إجباري)" },
                    sourceExamId = new { type = "integer", description = "معرف الامتحان المصدر (اختياري)" },
                    questions = new
                    {
                        type = "array",
                        description = "مصفوفة الأسئلة المراد حفظها",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                questionText = new { type = "string", description = "نص السؤال" },
                                questionType = new { type = "integer", description = "نوع السؤال: 1=اختيار من متعدد, 2=صح/خطأ, 3=أكمل الفراغ, 4=مقالي" },
                                correctAnswer = new { type = "string", description = "الإجابة الصحيحة (للأكمل والمقالي)" },
                                options = new
                                {
                                    type = "array",
                                    description = "الخيارات (لـ MCQ وصح/خطأ)",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            text = new { type = "string", description = "نص الخيار" },
                                            isCorrect = new { type = "boolean", description = "هل هذا هو الخيار الصحيح؟" },
                                            displayOrder = new { type = "integer", description = "ترتيب العرض" }
                                        }
                                    }
                                }
                            },
                            required = new[] { "questionText", "questionType" }
                        }
                    }
                },
                required = new[] { "subjectId", "gradeLevelId", "questions" }
            }),
            ExecuteAsync = async (args) =>
            {
                using var doc = JsonDocument.Parse(args);
                var subjectId = doc.RootElement.GetProperty("subjectId").GetInt32();
                var gradeLevelId = doc.RootElement.TryGetProperty("gradeLevelId", out var gl) ? gl.GetInt32() : 0;
                var sourceExamId = doc.RootElement.TryGetProperty("sourceExamId", out var se) ? se.GetInt32() : (int?)null;

                var questionsArray = doc.RootElement.GetProperty("questions").EnumerateArray();

                var addedCount = 0;
                var duplicateCount = 0;

                foreach (var qEl in questionsArray)
                {
                    var dto = new DTOs.QuestionBank.AddToQuestionBankDto
                    {
                        QuestionText = qEl.GetProperty("questionText").GetString() ?? "",
                        QuestionType = qEl.TryGetProperty("questionType", out var qt) ? qt.GetInt32() : 1,
                        CorrectAnswer = qEl.TryGetProperty("correctAnswer", out var ca) ? ca.GetString() : null,
                        SubjectId = subjectId,
                        GradeLevelId = gradeLevelId,
                        SourceExamId = sourceExamId,
                        Options = new()
                    };

                    if (qEl.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var opt in opts.EnumerateArray())
                        {
                            dto.Options.Add(new DTOs.QuestionBank.AddOptionDto
                            {
                                Text = opt.GetProperty("text").GetString() ?? "",
                                IsCorrect = opt.TryGetProperty("isCorrect", out var ic) && ic.GetBoolean(),
                                DisplayOrder = opt.TryGetProperty("displayOrder", out var od) ? od.GetInt32() : 0
                            });
                        }
                    }

                    var result = await _questionBankService.AddQuestionAsync(dto);
                    if (result.IsSuccess)
                    {
                        if (result.Message?.Contains("موجود مسبقاً") == true)
                            duplicateCount++;
                        else
                            addedCount++;
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    addedCount,
                    duplicateCount,
                    message = $"تم إضافة {addedCount} سؤال جديد إلى بنك الأسئلة" +
                              (duplicateCount > 0 ? $"، و{duplicateCount} أسئلة موجودة مسبقاً (تم زيادة عدد استخداماتها)" : "")
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        });

        list.Add(new AiTool
        {
            Name = "add_exam_to_question_bank",
            Description = "إضافة جميع أسئلة امتحان موجود إلى بنك الأسئلة (يستخدم بعد توليد امتحان أو من الامتحانات السابقة)",
            Parameters = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    examUid = new { type = "string", description = "UID الامتحان (من رابط المعاينة)" },
                    subjectId = new { type = "integer", description = "معرف المادة (من get_subjects: الـ subjectId)" }
                },
                required = new[] { "examUid", "subjectId" }
            }),
            ExecuteAsync = async (args) =>
            {
                using var doc = JsonDocument.Parse(args);
                var examUid = doc.RootElement.GetProperty("examUid").GetString() ?? "";
                var subjectId = doc.RootElement.GetProperty("subjectId").GetInt32();

                if (!Guid.TryParse(examUid, out var uid))
                    return JsonSerializer.Serialize(new { error = "UID غير صحيح" });

                var exam = await _examService.GetByUidAsync(uid);
                if (!exam.IsSuccess || exam.Data is null)
                    return JsonSerializer.Serialize(new { error = "الامتحان غير موجود" });

                var result = await _questionBankService.BulkAddFromExamAsync(exam.Data.Id, subjectId);
                return JsonSerializer.Serialize(new
                {
                    success = result.IsSuccess,
                    addedCount = result.Data,
                    message = result.Message ?? "تم إضافة أسئلة الامتحان إلى بنك الأسئلة"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        });

        // ─────────────────────────────────────────
        // TOOL 13 — save_to_question_embeddings 🆕
        // ─────────────────────────────────────────
        list.Add(new AiTool
        {
            Name = "save_to_question_embeddings",
            Description = "حفظ أسئلة امتحان في بنك الأسئلة مع تفعيل البحث الذكي للطلاب. يستخدم بعد توليد الامتحان لتمكين الطلاب من البحث عن أسئلة مشابهة.",
            Parameters = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    examUid = new { type = "string", description = "UID الامتحان (من رابط المعاينة)" },
                    subjectId = new { type = "integer", description = "معرف المادة (من get_subjects)" }
                },
                required = new[] { "examUid", "subjectId" }
            }),
            ExecuteAsync = async (args) =>
            {
                using var doc = JsonDocument.Parse(args);
                var examUid = doc.RootElement.GetProperty("examUid").GetString() ?? "";
                var subjectId = doc.RootElement.GetProperty("subjectId").GetInt32();

                if (!Guid.TryParse(examUid, out var uid))
                    return JsonSerializer.Serialize(new { error = "UID غير صحيح" });

                var exam = await _examService.GetByUidAsync(uid);
                if (!exam.IsSuccess || exam.Data is null)
                    return JsonSerializer.Serialize(new { error = "الامتحان غير موجود" });

                var examId = exam.Data.Id;

                // 1. Save to QuestionBank (inline duplicate detection handles existing questions)
                var saveResult = await _questionBankService.BulkAddFromExamAsync(examId, subjectId);
                var examAlreadyInBank = saveResult.Message?.Contains("موجودة مسبقاً") == true;

                // 2. Get QB IDs
                var links = await _unitOfWork.ExamQuestionBankItems
                    .FindAsync(l => l.ExamId == examId && !l.IsDeleted);
                var qbIds = links.Select(l => l.QuestionBankId).Distinct().ToList();

                if (qbIds.Count == 0)
                    return JsonSerializer.Serialize(new { success = false, message = "لم يتم العثور على أسئلة للحفظ" });

                // 3. Embed
                var embedResult = await _questionEmbeddingService.EmbedQuestionBankItemsAsync(qbIds);
                var alreadyEmbedded = embedResult.Message?.Contains("موجودة مسبقاً") == true;

                // Build response
                string resultMessage;
                if (examAlreadyInBank && alreadyEmbedded)
                {
                    resultMessage = "الامتحان موجود مسبقاً بالكامل في بنك الأسئلة والبحث الذكي ✅";
                }
                else if (examAlreadyInBank)
                {
                    resultMessage = $"تم تجهيز {embedResult.Data} سؤال للبحث الذكي (الأسئلة كانت موجودة مسبقاً في بنك الأسئلة)";
                }
                else if (alreadyEmbedded)
                {
                    resultMessage = $"تم حفظ {saveResult.Data} سؤال في بنك الأسئلة (كانت موجودة مسبقاً في البحث الذكي)";
                }
                else
                {
                    resultMessage = $"تم حفظ {saveResult.Data} سؤال في بنك الأسئلة وتجهيز {embedResult.Data} سؤال للبحث الذكي";
                }

                return JsonSerializer.Serialize(new
                {
                    success = embedResult.IsSuccess,
                    savedCount = saveResult.Data,
                    embeddedCount = embedResult.Data,
                    alreadyExists = examAlreadyInBank && alreadyEmbedded,
                    message = embedResult.IsSuccess
                        ? resultMessage
                        : embedResult.Message ?? "تم الحفظ لكن فشل التضمين"
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        });

        return list.ToDictionary(t => t.Name);
    }
}
