using Project.Domain.Enums;

namespace Project.Domain.Entities
{
    public class Timetable : BaseEntity
    {
        public int ClassId { get; set; }
        public int AcademicYearId { get; set; }
        public TimetableStatus Status { get; set; } = TimetableStatus.Draft;
        public int VersionNumber { get; set; } = 1;
        public DateTime? PublishedAt { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public int? SourceTimetableId { get; set; }

        public SchoolClass Class { get; set; } = null!;
        public AcademicYear AcademicYear { get; set; } = null!;
        public Timetable? SourceTimetable { get; set; }
        public ICollection<TimetableSlot> Slots { get; set; } = new List<TimetableSlot>();
    }
}
