using Project.DAL.Interfaces.Repositories.Core;
using Project.DAL.Interfaces.Repositories.Evaluation;
using Project.DAL.Interfaces.Repositories.Learning;
using Project.DAL.Interfaces.Repositories.Communication;
using Project.DAL.Interfaces.Repositories.Library;
using Project.DAL.Interfaces.Repositories.Feedback;
using Project.DAL.Interfaces.Repositories.Settings;
using Project.DAL.Interfaces.Repositories.Timetable;
using Project.DAL.Interfaces.Repositories;
using Project.DAL.Interfaces.Repositories.StudyPlans;
using Project.Domain.Entities;

namespace Project.DAL.Interfaces;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    IRepository<ClassTemplateLink>         ClassTemplateLinks           { get; }

    // Section A: Core
    IUserRepository                        Users                        { get; }
    IRefreshTokenRepository                RefreshTokens                { get; }
    IRepository<EmailOtp>                  EmailOtps                    { get; }
    IAcademicYearRepository                AcademicYears                { get; }
    IGradeLevelRepository                  GradeLevels                  { get; }
    ISubjectRepository                     Subjects                     { get; }
    ISchoolClassRepository                 Classes                      { get; }
    IStudentRepository                     Students                     { get; }
    IStudentEnrollmentRepository           StudentEnrollments           { get; }
    IParentStudentRepository               ParentStudents               { get; }
    IClassSubjectTeacherRepository         ClassSubjectTeachers         { get; }
    IRepository<Unit>                      Units                        { get; }
    IRepository<Lesson>                    Lessons                      { get; }
    ITeacherSubjectRepository              TeacherSubjects              { get; }

    // Section B: Evaluation (Academic)
    IEvaluationTemplateRepository          EvaluationTemplates          { get; }
    IEvaluationItemRepository              EvaluationItems              { get; }
    IEvaluationPeriodRepository            EvaluationPeriods            { get; }
    IStudentEvaluationRepository           StudentEvaluations           { get; }
    IDailyAbsenceRepository                DailyAbsences                { get; }
    IPeriodAverageRepository               PeriodAverages               { get; }
    IPeriodicAssessmentRepository          PeriodicAssessments          { get; }
    IFinalGradeRepository                  FinalGrades                  { get; }

    // Section C: Learning (Training)
    IAssignmentRepository                  Assignments                  { get; }
    IAssignmentQuestionRepository          AssignmentQuestions          { get; }
    IAssignmentQuestionOptionRepository    AssignmentQuestionOptions    { get; }
    IStudentAssignmentSubmissionRepository StudentAssignmentSubmissions { get; }
    IStudentAssignmentAnswerRepository     StudentAssignmentAnswers     { get; }
    IRepository<ExamQuestionGroup>         ExamQuestionGroups           { get; }
    IExamRepository                        Exams                        { get; }
    IExamQuestionRepository                ExamQuestions                { get; }
    IExamQuestionOptionRepository          ExamQuestionOptions          { get; }
    IStudentExamAttemptRepository          StudentExamAttempts          { get; }
    IStudentExamAnswerRepository           StudentExamAnswers           { get; }

    // Section D: Communication
    IConversationRepository                Conversations                { get; }
    IConversationParticipantRepository     ConversationParticipants     { get; }
    IMessageRepository                     Messages                     { get; }
    IBlockedUserRepository                 BlockedUsers                 { get; }
    INotificationRepository                Notifications                { get; }
    IAnnouncementRepository                Announcements                { get; }
    IRepository<AnnouncementUser>          AnnouncementUsers            { get; }
    IRepository<ParentMeetingRequest>      ParentMeetingRequests        { get; }

    // Section E: Library
    ILibraryItemRepository                 LibraryItems                 { get; }

    // Section F: Feedback
    ILessonFeedbackRepository              LessonFeedbacks              { get; }

    // Section G: Settings
    IResultVisibilitySettingRepository     ResultVisibilitySettings     { get; }
    IAIGenerationLogRepository             AIGenerationLogs             { get; }
    ISchoolProfileRepository               SchoolProfiles               { get; }
    IRepository<AIReport>                  AIReports                    { get; }

    // Section H: Certificates
    IRepository<Certificate>               Certificates                 { get; }
    IRepository<CertificateSubject>        CertificateSubjects          { get; }

    // Section H: Timetable
    IRoomRepository                        Rooms                        { get; }
    ITimetableRepository                   Timetables                   { get; }
    ITimetableSlotRepository               TimetableSlots               { get; }

    // Section I: Study Plans
    IStudyPlanRepository                   StudyPlans                   { get; }
    IStudyPlanItemRepository               StudyPlanItems               { get; }

    // Section J: Question Bank
    IRepository<QuestionBank>              QuestionBank                 { get; }
    IRepository<ExamQuestionBankItem>      ExamQuestionBankItems        { get; }

    // Persistence

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    void ClearChangeTracker();

    // Transaction Management

    Task BeginTransactionAsync(CancellationToken ct = default);

    Task CommitTransactionAsync(CancellationToken ct = default);

    Task RollbackTransactionAsync(CancellationToken ct = default);

    Task    ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
}

