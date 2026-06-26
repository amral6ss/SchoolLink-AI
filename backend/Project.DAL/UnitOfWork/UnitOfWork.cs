using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Project.DAL.Interfaces;
using Project.DAL.Interfaces.Repositories.Communication;
using Project.DAL.Interfaces.Repositories.Core;
using Project.DAL.Interfaces.Repositories.Evaluation;
using Project.DAL.Interfaces.Repositories.Feedback;
using Project.DAL.Interfaces.Repositories.Learning;
using Project.DAL.Interfaces.Repositories.Library;
using Project.DAL.Interfaces.Repositories.Settings;
using Project.DAL.Interfaces.Repositories.StudyPlans;
using Project.DAL.Interfaces.Repositories.Timetable;
using Project.DAL.Context;
using Project.DAL.Interfaces.Repositories;
using Project.Domain.Entities;

namespace Project.DAL.UnitOfWork;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(
        AppDbContext context,

        IRepository<Unit>                      units,
        IRepository<Lesson>                    lessons,

        IUserRepository                        users,

        IRefreshTokenRepository                refreshTokens,
        IRepository<EmailOtp>                  emailOtps,
        IAcademicYearRepository                academicYears,
        IGradeLevelRepository                  gradeLevels,
        ISubjectRepository                     subjects,
        ISchoolClassRepository                 classes,
        IStudentRepository                     students,
        IStudentEnrollmentRepository           studentEnrollments,
        IParentStudentRepository               parentStudents,
        IClassSubjectTeacherRepository         classSubjectTeachers,
        ITeacherSubjectRepository              teacherSubjects,

        IEvaluationTemplateRepository          evaluationTemplates,
        IEvaluationItemRepository              evaluationItems,
        IEvaluationPeriodRepository            evaluationPeriods,
        IStudentEvaluationRepository           studentEvaluations,
        IDailyAbsenceRepository                dailyAbsences,
        IPeriodAverageRepository               periodAverages,
        IPeriodicAssessmentRepository          periodicAssessments,
        IFinalGradeRepository                  finalGrades,

        IAssignmentRepository                  assignments,
        IAssignmentQuestionRepository          assignmentQuestions,
        IAssignmentQuestionOptionRepository    assignmentQuestionOptions,
        IStudentAssignmentSubmissionRepository studentAssignmentSubmissions,
        IStudentAssignmentAnswerRepository     studentAssignmentAnswers,
        IRepository<ExamQuestionGroup>         examQuestionGroups,
        IExamRepository                        exams,
        IExamQuestionRepository                examQuestions,
        IExamQuestionOptionRepository          examQuestionOptions,
        IStudentExamAttemptRepository          studentExamAttempts,
        IStudentExamAnswerRepository           studentExamAnswers,

        IConversationRepository                conversations,
        IConversationParticipantRepository     conversationParticipants,
        IMessageRepository                     messages,
        IBlockedUserRepository                 blockedUsers,
        INotificationRepository                notifications,
        IAnnouncementRepository                announcements,
        IRepository<AnnouncementUser>          announcementUsers,
        IRepository<ParentMeetingRequest>      parentMeetingRequests,

        ILibraryItemRepository                 libraryItems,

        ILessonFeedbackRepository              lessonFeedbacks,

        IResultVisibilitySettingRepository     resultVisibilitySettings,
        IAIGenerationLogRepository             aiGenerationLogs,
        ISchoolProfileRepository               schoolProfiles,

        IRoomRepository                        rooms,
        ITimetableRepository                   timetables,
        ITimetableSlotRepository               timetableSlots,

        IStudyPlanRepository                   studyPlans,
        IStudyPlanItemRepository               studyPlanItems,
        IRepository<ClassTemplateLink>         classTemplateLinks,

        // Section J: Question Bank
        IRepository<QuestionBank>              questionBank,
        IRepository<ExamQuestionBankItem>      examQuestionBankItems,

        // AI Reports
        IRepository<AIReport>                  aiReports,

        // Certificates
        IRepository<Certificate>               certificates,
        IRepository<CertificateSubject>        certificateSubjects
        )
    {
        _context = context;

        // Section A
        Users                        = users;
        RefreshTokens                = refreshTokens;
        EmailOtps                    = emailOtps;
        AcademicYears                = academicYears;
        GradeLevels                  = gradeLevels;
        Subjects                     = subjects;
        Classes                      = classes;
        Students                     = students;
        StudentEnrollments           = studentEnrollments;
        ParentStudents               = parentStudents;
        ClassSubjectTeachers         = classSubjectTeachers;
        Units                        = units;
        Lessons                      = lessons;
        TeacherSubjects              = teacherSubjects;

        // Section B
        EvaluationTemplates          = evaluationTemplates;
        EvaluationItems              = evaluationItems;
        EvaluationPeriods            = evaluationPeriods;
        StudentEvaluations           = studentEvaluations;
        DailyAbsences                = dailyAbsences;
        PeriodAverages               = periodAverages;
        PeriodicAssessments          = periodicAssessments;
        FinalGrades                  = finalGrades;

        // Section C
        Assignments                  = assignments;
        AssignmentQuestions          = assignmentQuestions;
        AssignmentQuestionOptions    = assignmentQuestionOptions;
        StudentAssignmentSubmissions = studentAssignmentSubmissions;
        StudentAssignmentAnswers     = studentAssignmentAnswers;
        ExamQuestionGroups           = examQuestionGroups;
        Exams                        = exams;
        ExamQuestions                = examQuestions;
        ExamQuestionOptions          = examQuestionOptions;
        StudentExamAttempts          = studentExamAttempts;
        StudentExamAnswers           = studentExamAnswers;

        // Section D
        Conversations                = conversations;
        ConversationParticipants     = conversationParticipants;
        Messages                     = messages;
        BlockedUsers                 = blockedUsers;
        Notifications                = notifications;
        Announcements                = announcements;
        AnnouncementUsers            = announcementUsers;
        ParentMeetingRequests        = parentMeetingRequests;

        // Section E
        LibraryItems                 = libraryItems;

        // Section F
        LessonFeedbacks              = lessonFeedbacks;

        // Section G
        ResultVisibilitySettings     = resultVisibilitySettings;
        AIGenerationLogs             = aiGenerationLogs;
        SchoolProfiles               = schoolProfiles;

        // Section H
        Rooms                        = rooms;
        Timetables                   = timetables;
        TimetableSlots               = timetableSlots;

        // Section I
        StudyPlans                   = studyPlans;
        StudyPlanItems               = studyPlanItems;
        ClassTemplateLinks           = classTemplateLinks;

        // Section J
        QuestionBank                 = questionBank;
        ExamQuestionBankItems        = examQuestionBankItems;

        // AI Reports
        AIReports                    = aiReports;

        // Certificates
        Certificates                 = certificates;
        CertificateSubjects          = certificateSubjects;
    }

    public IRepository<ClassTemplateLink>         ClassTemplateLinks           { get; }

    // Section A: Core
    public IUserRepository                        Users                        { get; }
    public IRefreshTokenRepository                RefreshTokens                { get; }
    public IRepository<EmailOtp>                  EmailOtps                    { get; }
    public IAcademicYearRepository                AcademicYears                { get; }
    public IGradeLevelRepository                  GradeLevels                  { get; }
    public ISubjectRepository                     Subjects                     { get; }
    public ISchoolClassRepository                 Classes                      { get; }
    public IStudentRepository                     Students                     { get; }
    public IStudentEnrollmentRepository           StudentEnrollments           { get; }
    public IParentStudentRepository               ParentStudents               { get; }
    public IClassSubjectTeacherRepository         ClassSubjectTeachers         { get; }
    public ITeacherSubjectRepository              TeacherSubjects              { get; }
    public IRepository<Unit>                      Units                        { get; }
    public IRepository<Lesson>                    Lessons                      { get; }

    // Section B: Evaluation (Academic)
    public IEvaluationTemplateRepository          EvaluationTemplates          { get; }
    public IEvaluationItemRepository              EvaluationItems              { get; }
    public IEvaluationPeriodRepository            EvaluationPeriods            { get; }
    public IStudentEvaluationRepository           StudentEvaluations           { get; }
    public IDailyAbsenceRepository                DailyAbsences                { get; }
    public IPeriodAverageRepository               PeriodAverages               { get; }
    public IPeriodicAssessmentRepository          PeriodicAssessments          { get; }
    public IFinalGradeRepository                  FinalGrades                  { get; }

    // Section C: Learning (Training)
    public IAssignmentRepository                  Assignments                  { get; }
    public IAssignmentQuestionRepository          AssignmentQuestions          { get; }
    public IAssignmentQuestionOptionRepository    AssignmentQuestionOptions    { get; }
    public IStudentAssignmentSubmissionRepository StudentAssignmentSubmissions { get; }
    public IStudentAssignmentAnswerRepository     StudentAssignmentAnswers     { get; }
    public IRepository<ExamQuestionGroup>         ExamQuestionGroups           { get; }
    public IExamRepository                        Exams                        { get; }
    public IExamQuestionRepository                ExamQuestions                { get; }
    public IExamQuestionOptionRepository          ExamQuestionOptions          { get; }
    public IStudentExamAttemptRepository          StudentExamAttempts          { get; }
    public IStudentExamAnswerRepository           StudentExamAnswers           { get; }

    // Section D: Communication
    public IConversationRepository                Conversations                { get; }
    public IConversationParticipantRepository     ConversationParticipants     { get; }
    public IMessageRepository                     Messages                     { get; }
    public IBlockedUserRepository                 BlockedUsers                 { get; }
    public INotificationRepository                Notifications                { get; }
    public IAnnouncementRepository                Announcements                { get; }
    public IRepository<AnnouncementUser>           AnnouncementUsers            { get; }
    public IRepository<ParentMeetingRequest>      ParentMeetingRequests        { get; }

    // Section E: Library
    public ILibraryItemRepository                 LibraryItems                 { get; }

    // Section F: Feedback
    public ILessonFeedbackRepository              LessonFeedbacks              { get; }

    // Section G: Settings
    public IResultVisibilitySettingRepository     ResultVisibilitySettings     { get; }
    public IAIGenerationLogRepository             AIGenerationLogs             { get; }
    public ISchoolProfileRepository               SchoolProfiles               { get; }

    // Section H: Certificates
    public IRepository<Certificate>               Certificates                 { get; }
    public IRepository<CertificateSubject>        CertificateSubjects          { get; }

    // Section H: Timetable
    public IRoomRepository                        Rooms                        { get; }
    public ITimetableRepository                   Timetables                   { get; }
    public ITimetableSlotRepository               TimetableSlots               { get; }

    // Section I: Study Plans
    public IStudyPlanRepository                   StudyPlans                   { get; }
    public IStudyPlanItemRepository               StudyPlanItems               { get; }

    // Section J: Question Bank
    public IRepository<QuestionBank>              QuestionBank                 { get; }
    public IRepository<ExamQuestionBankItem>      ExamQuestionBankItems        { get; }

    // AI Reports
    public IRepository<AIReport>                  AIReports                    { get; }

    // Persistence

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);

    public void ClearChangeTracker()
        => _context.ChangeTracker.Clear();

    // Transaction Management

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction is not null)
            throw new InvalidOperationException(
                "There is already an active transaction. Complete it before starting a new one.");

        _currentTransaction = await _context.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction is null)
            throw new InvalidOperationException(
                "There is no active transaction to commit.");

        await _currentTransaction.CommitAsync(ct);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction is null) return;

        await _currentTransaction.RollbackAsync(ct);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
    }

    public async Task ExecuteInTransactionAsync(
        Func<Task> action,
        CancellationToken ct = default)
    {
        await BeginTransactionAsync(ct);
        try
        {
            await action();
            await SaveChangesAsync(ct);
            await CommitTransactionAsync(ct);
        }
        catch
        {
            await RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken ct = default)
    {
        await BeginTransactionAsync(ct);
        try
        {
            var result = await action();
            await SaveChangesAsync(ct);
            await CommitTransactionAsync(ct);
            return result;
        }
        catch
        {
            await RollbackTransactionAsync(ct);
            throw;
        }
    }

    // Dispose

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _context.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction is not null)
            await _currentTransaction.DisposeAsync();

        await _context.DisposeAsync();
    }
}

