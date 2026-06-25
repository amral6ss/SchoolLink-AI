namespace Project.BLL.DTOs;

public class TimetableDto
{
    public int    Id             { get; set; }
    public int    ClassId        { get; set; }
    public string ClassName      { get; set; } = string.Empty;
    public int    AcademicYearId { get; set; }
    public bool   IsActive       { get; set; }
    public string Status         { get; set; } = string.Empty;
    public int    StatusValue    { get; set; }
    public int    VersionNumber  { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ArchivedAt  { get; set; }
    public int?   SourceTimetableId { get; set; }
    public DateTime CreatedAt    { get; set; }
    public DateTime UpdatedAt    { get; set; }
    public List<TimetableSlotDto> Slots { get; set; } = new();
}
