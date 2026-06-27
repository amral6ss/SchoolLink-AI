using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Project.API.Services;
using Project.BLL.AI.Agents;
using Project.BLL.AI.Infrastructure;
using Project.BLL.AI.Interfaces;
using Project.BLL.AI.Services;
using Project.BLL.AI.Tools;
using Project.BLL.AI.Providers;
using Project.BLL.Interfaces;
using Project.BLL.Mapping;
using Project.BLL.Services;
using Project.BLL.Validators;
using Project.BLL.Embedding;
using Project.DAL.Context;
using Project.DAL.Interfaces;
using Project.DAL.Interfaces.Repositories;
using Project.DAL.MongoDb;
using Project.DAL.Repositories;
using Project.DAL.UnitOfWork;

namespace Project.API.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(config.GetConnectionString("DefaultConnection")));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        RegisterRepositories(services);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAccountGenerationService, AccountGenerationService>();
        services.AddScoped<ITeacherService, TeacherService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationPushService, SignalRNotificationPushService>();
        services.AddScoped<IAIReportService, AIReportService>();
        services.AddScoped<IParentMeetingService, ParentMeetingService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IResultVisibilityService, ResultVisibilityService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<ISchoolProfileService, SchoolProfileService>();
        services.AddHttpClient<IDropboxService, DropboxService>();
        services.AddHttpClient<ITextToSpeechService, TextToSpeechService>(c =>
        {
            c.Timeout = TimeSpan.FromMinutes(3);
        });
        services.AddHttpClient<IEmailService, BrevoEmailService>(client =>
        {
            client.BaseAddress = new Uri("https://api.brevo.com/v3/");
        });
        services.AddScoped<IEmailOtpService, EmailOtpService>();


        services.AddScoped<IAcademicYearService, AcademicYearService>();
        services.AddScoped<IGradeLevelService, GradeLevelService>();
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<IClassService, ClassService>();
        services.AddScoped<IClassStudentsBrowserService, ClassStudentsBrowserService>();
        services.AddScoped<IClassSubjectTeacherService, ClassSubjectTeacherService>();
        services.AddScoped<ITimetableService, TimetableService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IAssignmentManagerService, AssignmentManagerService>();
        services.AddScoped<IAssignmentSubmissionService, AssignmentSubmissionService>();
        services.AddScoped<IStudentAssignmentService, StudentAssignmentService>();
        services.AddScoped<IExamService, ExamService>();
        services.AddScoped<IExamManagerService, ExamManagerService>();
        services.AddScoped<IExamAttemptService, ExamAttemptService>();
        services.AddScoped<IStudentExamService, StudentExamService>();
        services.AddScoped<IExamHtmlRenderer, ExamHtmlRenderer>();
        services.AddScoped<IQuestionBankService, QuestionBankService>();
        services.AddScoped<IExamMediaService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(IExamMediaService));
            var logger = sp.GetRequiredService<ILogger<ExamMediaService>>();
            var config = sp.GetRequiredService<IConfiguration>();
            var env = sp.GetRequiredService<IWebHostEnvironment>();

            var apiKey = config["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");
            var mediaFolder = Path.Combine(env.WebRootPath, "exam-media");

            return new ExamMediaService(httpClient, logger, apiKey, mediaFolder);
        });
        services.AddScoped<IAIGenerationLogService, AIGenerationLogService>();
        services.AddScoped<IEvaluationTemplateService, EvaluationTemplateService>();
        services.AddScoped<IEvaluationPeriodService, EvaluationPeriodService>();
        services.AddScoped<IEvaluationItemService, EvaluationItemService>();
        services.AddScoped<IStudentEvaluationService, StudentEvaluationService>();
        services.AddScoped<IDailyAbsenceService, DailyAbsenceService>();
        services.AddScoped<IPeriodAverageService, PeriodAverageService>();
        services.AddScoped<IPeriodicAssessmentService, PeriodicAssessmentService>();
        services.AddScoped<IFinalGradeService, FinalGradeService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<IStudentEnrollmentService, StudentEnrollmentService>();
        services.AddScoped<IStudentProgressionService, StudentProgressionService>();
        services.AddScoped<IParentStudentService, ParentStudentService>();
        services.AddScoped<IStudyPlanService, StudyPlanService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IParentDashboardService, ParentDashboardService>();
        services.AddScoped<IChildProgressService, ChildProgressService>();
        services.AddScoped<IAcademicReportService, AcademicReportService>();
        services.AddScoped<ITeacherDashboardService, TeacherDashboardService>();
        services.AddScoped<IClassAnalysisService, ClassAnalysisService>();
        services.AddScoped<ILessonFeedbackService, LessonFeedbackService>();
        services.AddScoped<IHistoricalDataService, HistoricalDataService>();
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<WhisperTranscriptionService>();

        // AI Services
        RegisterAiServices(services, config);

        services.AddAutoMapper(cfg => cfg.AddMaps(typeof(MappingProfile).Assembly));

        services.AddValidatorsFromAssemblyContaining<CreateUserValidator>();

        return services;
    }

    private static void RegisterRepositories(IServiceCollection services)
    {
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        var dalAssembly = typeof(Repository<>).Assembly;
        var repositoryTypes = dalAssembly.GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                type.Name.EndsWith("Repository", StringComparison.Ordinal) &&
                !type.IsGenericTypeDefinition);

        foreach (var repositoryType in repositoryTypes)
        {
            var repositoryInterface = repositoryType.GetInterfaces()
                .FirstOrDefault(i => i.Name == $"I{repositoryType.Name}");

            if (repositoryInterface is not null)
                services.AddScoped(repositoryInterface, repositoryType);
        }
    }

    private static void RegisterAiServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("Gemini", c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient("DeepSeek", c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient("MistralOcr", c =>
        {
            c.Timeout = TimeSpan.FromMinutes(5);
        });

        RegisterProvider<GeminiProvider>(services, "Gemini");
        RegisterProvider<DeepSeekProvider>(services, "DeepSeek");
        RegisterProvider<OpenRouterProvider>(services, null);
        RegisterProvider<HuggingFaceProvider>(services, null);
        RegisterProvider<CloudflareAIProvider>(services, null);
        RegisterProvider<OpenCodeAIProvider>(services, null);

        services.RegisterLlmClient(config);

        services.AddScoped<ILLMRouter, LLMRouter>();

        // Tool Services
        services.AddScoped<ITeacherToolService, TeacherToolService>();
        services.AddScoped<IStudentToolService, StudentToolService>();
        services.AddScoped<IParentToolService, ParentToolService>();

        // Agents
        services.AddScoped<IStudentAssistantAgent, StudentAssistantAgent>();
        services.AddScoped<ITeacherAssistantAgent, TeacherAssistantAgent>();
        services.AddScoped<IParentAssistantAgent, ParentAssistantAgent>();

        services.AddScoped<IExamGeneratorService, ExamGeneratorService>();
        services.AddScoped<IAiExamGeneratorService, AiExamGeneratorService>();
        services.AddScoped<IEvaluationReportService, EvaluationReportService>();
        services.AddScoped<IStudyScheduleOptimizerService, StudyScheduleOptimizerService>();
        services.AddScoped<IStudentImportService, StudentImportService>();
        services.AddScoped<IBookParserService, BookParserService>();

        services.AddScoped<ILessonRepository, DbLessonRepository>();
        services.AddScoped<IExamGenerator, LlmExamGenerator>();
        services.AddScoped<IAgentChatStore, AgentChatStore>();
        services.AddScoped<IClassEnrollmentPickerService, ClassEnrollmentPickerService>();

        // MongoDB & Embedding
        services.Configure<MongoDbSettings>(config.GetSection("MongoDb"));
        services.AddScoped<IMongoDbContext, MongoDbContext>();
        services.AddScoped<IEmbeddingService, HuggingFaceEmbeddingService>();
        services.AddScoped<IQuestionEmbeddingService, QuestionEmbeddingService>();
    }

    private static void RegisterProvider<T>(IServiceCollection services, string? httpClientName) where T : class, ILLMProvider
    {
        services.AddScoped<T>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = httpClientName != null ? factory.CreateClient(httpClientName) : factory.CreateClient();
            var logger = sp.GetRequiredService<ILogger<T>>();
            var config = sp.GetRequiredService<IConfiguration>();
            return (T)ActivatorUtilities.CreateInstance(sp, typeof(T), http, logger, config);
        });
        services.AddScoped<ILLMProvider>(sp => sp.GetRequiredService<T>());
    }


}

