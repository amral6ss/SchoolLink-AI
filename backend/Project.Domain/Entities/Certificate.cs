namespace Project.Domain.Entities
{
    public class Certificate : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string GradeLevel { get; set; } = string.Empty;
        public string Term { get; set; } = string.Empty;
        public string ExamRole { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;

        public ICollection<CertificateSubject> Subjects { get; set; } = new List<CertificateSubject>();
    }
}
