using Project.Domain.Enums;

namespace Project.Domain.Entities
{
    public class SchoolClass : BaseEntity
    {
        public int GradeLevelId { get; set; }
        public int AcademicYearId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Capacity { get; set; }
        public ClassStatus Status { get; set; } = ClassStatus.Active;

        // Navigation Properties
        public GradeLevel GradeLevel { get; set; } = null!;
        public AcademicYear AcademicYear { get; set; } = null!;
        public ICollection<StudentEnrollment> Enrollments { get; set; } = new List<StudentEnrollment>();
    }
}
