namespace Project.Domain.Entities
{
    public class CertificateSubject : BaseEntity
    {
        public int CertificateId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int MaxScore { get; set; }
        public int MinScore { get; set; }
        public bool IsCountedInTotal { get; set; } = true;
        public int SortOrder { get; set; }

        public Certificate Certificate { get; set; } = null!;
    }
}
