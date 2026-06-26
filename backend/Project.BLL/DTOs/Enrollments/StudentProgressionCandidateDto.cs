using Project.Domain.Enums;

namespace Project.BLL.DTOs.Enrollments;

/// <summary>
/// الحالة الأكاديمية للطالب المحسوبة من درجاته النهائية.
/// </summary>
public enum AcademicStatus
{
    /// <summary>لا توجد درجات نهائية لهذا الطالب بعد.</summary>
    NoGrades = 0,

    /// <summary>توجد درجات لكنها لم تُنشر بعد.</summary>
    Unpublished = 1,

    /// <summary>الطالب ناجح (نجح في كل المواد).</summary>
    Passed = 2,

    /// <summary>الطالب راسب (رسب في مادة واحدة على الأقل).</summary>
    Failed = 3
}

/// <summary>
/// ملخص درجة طالب في مادة واحدة (بعد التطبيع لنسبة من 100).
/// </summary>
public class SubjectGradeDto
{
    public int? SubjectId { get; set; }
    public string SubjectName { get; set; } = "غير محدد";

    /// <summary>الدرجة النهائية كنسبة مئوية من 100 (تطبيع من MaxTotal).</summary>
    public decimal Percentage { get; set; }

    public bool IsPublished { get; set; }

    public bool IsPassed { get; set; }

    /// <summary>الترم الذي تخصه هذه الدرجة (مفيد عند عرض الترمين مجتمعين).</summary>
    public AcademicTermLabel? Term { get; set; }
}

/// <summary>
/// تسمية الترم الدراسي كما تُعرض في الواجهة.
/// </summary>
public enum AcademicTermLabel
{
    FirstSemester = 1,
    SecondSemester = 2
}

public class StudentProgressionCandidateDto
{
    public int EnrollmentId { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;

    public int CurrentClassId { get; set; }
    public string CurrentClassName { get; set; } = string.Empty;

    public int CurrentGradeLevelId { get; set; }
    public string CurrentGradeLevelName { get; set; } = string.Empty;

    public int AcademicYearId { get; set; }
    public string AcademicYearName { get; set; } = string.Empty;

    public bool StudentIsActive { get; set; }
    public StudentLifecycleStatus StudentLifecycleStatus { get; set; }
    public string StudentLifecycleStatusName { get; set; } = string.Empty;
    public bool HasStudentAccount { get; set; }

    public bool HasFinalGrade { get; set; }
    public decimal? FinalTotal { get; set; }
    public bool HasPublishedFinalGrade { get; set; }

    /// <summary>
    /// الحالة الأكاديمية المُحتسبة (ناجح / راسب / بدون درجات / غير منشورة).
    /// يُحتسب على أساس قاعدة "النجاح في كل مادة على حدة".
    /// </summary>
    public AcademicStatus AcademicStatus { get; set; }

    /// <summary>عدد المواد الناجحة (منشورة وتجاوزت الحد).</summary>
    public int PassedSubjectsCount { get; set; }

    /// <summary>عدد المواد الراسبة (منشورة وتحت الحد).</summary>
    public int FailedSubjectsCount { get; set; }

    /// <summary>تفصيل درجات المواد لعرضه في نافذة التفاصيل.</summary>
    public List<SubjectGradeDto> SubjectGrades { get; set; } = new();
}
