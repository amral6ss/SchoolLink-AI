using System.ComponentModel.DataAnnotations;

namespace Project.BLL.DTOs.Certificates
{
    public class CreateCertificateRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string GradeLevel { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Term { get; set; }

        [MaxLength(100)]
        public string? ExamRole { get; set; }

        [MaxLength(20)]
        public string? Year { get; set; }

        public List<CertificateSubjectDto> Subjects { get; set; } = new();
    }

    public class UpdateCertificateRequest
    {
        [Range(1, int.MaxValue)]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string GradeLevel { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Term { get; set; }

        [MaxLength(100)]
        public string? ExamRole { get; set; }

        [MaxLength(20)]
        public string? Year { get; set; }

        public List<CertificateSubjectDto> Subjects { get; set; } = new();
    }

    public class CertificateSubjectDto
    {
        public int? Id { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int MaxScore { get; set; }
        public int MinScore { get; set; }
        public bool IsCountedInTotal { get; set; } = true;
        public int SortOrder { get; set; }
    }

    public class CertificateDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string GradeLevel { get; set; } = string.Empty;
        public string Term { get; set; } = string.Empty;
        public string ExamRole { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public List<CertificateSubjectDto> Subjects { get; set; } = new();
    }

    // ─── Generate / Grade Sheet DTOs ───

    public class CertificateGenerateResponse
    {
        public CertificateDto Certificate { get; set; } = null!;
        public string ClassName { get; set; } = string.Empty;
        public List<CertificateStudentData> Students { get; set; } = new();
    }

    public class CertificateStudentData
    {
        public string StudentName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public int EnrollmentId { get; set; }
        public string? SeatNumber { get; set; }
        public List<CertificateStudentSubject> Subjects { get; set; } = new();
        public decimal TotalBasic { get; set; }
        public decimal TotalMaxBasic { get; set; }
        public decimal TotalWithActivities { get; set; }
        public decimal TotalMaxWithActivities { get; set; }
        public int Rank { get; set; }
        public decimal Percentage { get; set; }
    }

    public class CertificateStudentSubject
    {
        public string SubjectName { get; set; } = string.Empty;
        public decimal MaxScore { get; set; }
        public decimal MinScore { get; set; }
        public decimal? Score { get; set; }
        public bool IsCountedInTotal { get; set; }
    }

    public class CertificateGradeSheetResponse
    {
        public CertificateDto Certificate { get; set; } = null!;
        public string ClassName { get; set; } = string.Empty;
        public string GradeLevelName { get; set; } = string.Empty;
        public string AcademicYearName { get; set; } = string.Empty;
        public List<GradeSheetStudentRow> Students { get; set; } = new();
    }

    public class GradeSheetStudentRow
    {
        public int RowNumber { get; set; }
        public string? SeatNumber { get; set; }
        public string StudentName { get; set; } = string.Empty;
        public DateOnly? BirthDate { get; set; }
        public decimal TotalScore { get; set; }
        public decimal MaxTotal { get; set; }
        public int Rank { get; set; }
        public string ClassName { get; set; } = string.Empty;
    }

    // ─── Honor Roll (كشف بأوائل الطلاب) ───

    public class CertificateHonorRollResponse
    {
        public string GradeLevelName { get; set; } = string.Empty;
        public string AcademicYearName { get; set; } = string.Empty;
        public string Term { get; set; } = string.Empty;
        public string ExamRole { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public int TopCount { get; set; }
        public decimal MaxTotal { get; set; }
        public List<GradeSheetStudentRow> Students { get; set; } = new();
    }
}
