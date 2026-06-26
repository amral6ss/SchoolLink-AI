using Microsoft.EntityFrameworkCore;
using Project.Domain.Entities;

namespace Project.DAL.Context;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<GradeLevel> GradeLevels => Set<GradeLevel>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();
    public DbSet<ClassSubjectTeacher> ClassSubjectTeachers => Set<ClassSubjectTeacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<ClassTemplateLink> ClassTemplateLinks => Set<ClassTemplateLink>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    
    public DbSet<EvaluationTemplate> EvaluationTemplates => Set<EvaluationTemplate>();
    public DbSet<EvaluationItem> EvaluationItems => Set<EvaluationItem>();
    public DbSet<EvaluationPeriod> EvaluationPeriods => Set<EvaluationPeriod>();
    public DbSet<StudentEvaluation> StudentEvaluations => Set<StudentEvaluation>();
    public DbSet<DailyAbsence> DailyAbsences => Set<DailyAbsence>();
    public DbSet<PeriodAverage> PeriodAverages => Set<PeriodAverage>();
    public DbSet<PeriodicAssessment> PeriodicAssessments => Set<PeriodicAssessment>();
    public DbSet<FinalGrade> FinalGrades => Set<FinalGrade>();
    
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentQuestion> AssignmentQuestions => Set<AssignmentQuestion>();
    public DbSet<AssignmentQuestionOption> AssignmentQuestionOptions => Set<AssignmentQuestionOption>();
    public DbSet<StudentAssignmentSubmission> StudentAssignmentSubmissions => Set<StudentAssignmentSubmission>();
    public DbSet<StudentAssignmentAnswer> StudentAssignmentAnswers => Set<StudentAssignmentAnswer>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamQuestionGroup> ExamQuestionGroups => Set<ExamQuestionGroup>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamQuestionOption> ExamQuestionOptions => Set<ExamQuestionOption>();
    public DbSet<StudentExamAttempt> StudentExamAttempts => Set<StudentExamAttempt>();
    public DbSet<StudentExamAnswer> StudentExamAnswers => Set<StudentExamAnswer>();
    
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<BlockedUser> BlockedUsers => Set<BlockedUser>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementUser> AnnouncementUsers => Set<AnnouncementUser>();
    public DbSet<ParentMeetingRequest> ParentMeetingRequests => Set<ParentMeetingRequest>();
    
    public DbSet<LibraryItem> LibraryItems => Set<LibraryItem>();
    
    public DbSet<LessonFeedback> LessonFeedbacks => Set<LessonFeedback>();
    
    public DbSet<ResultVisibilitySetting> ResultVisibilitySettings => Set<ResultVisibilitySetting>();
    public DbSet<AIGenerationLog> AIGenerationLogs => Set<AIGenerationLog>();
    public DbSet<AIReport> AIReports => Set<AIReport>();
    public DbSet<SchoolProfile> SchoolProfiles => Set<SchoolProfile>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<CertificateSubject> CertificateSubjects => Set<CertificateSubject>();

    public DbSet<QuestionBank> QuestionBank => Set<QuestionBank>();
    public DbSet<ExamQuestionBankItem> ExamQuestionBankItems => Set<ExamQuestionBankItem>();
    
    public DbSet<Room>          Rooms          => Set<Room>();
    public DbSet<Timetable>     Timetables     => Set<Timetable>();
    public DbSet<TimetableSlot> TimetableSlots => Set<TimetableSlot>();
    
    public DbSet<StudyPlan> StudyPlans => Set<StudyPlan>();
    public DbSet<StudyPlanItem> StudyPlanItems => Set<StudyPlanItem>();

    public DbSet<AgentConversationMessage> AgentConversationMessages => Set<AgentConversationMessage>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Lesson> Lessons => Set<Lesson>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
