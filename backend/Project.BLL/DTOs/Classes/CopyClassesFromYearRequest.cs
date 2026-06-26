using System.ComponentModel.DataAnnotations;

namespace Project.BLL.DTOs;

public class CopyClassesFromYearRequest
{
    [Range(1, int.MaxValue)]
    public int SourceAcademicYearId { get; set; }

    [Range(1, int.MaxValue)]
    public int TargetAcademicYearId { get; set; }
}

public class CopyClassesFromYearPreviewDto
{
    public int SourceClassId { get; set; }
    public int GradeLevelId { get; set; }
    public string GradeLevelName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public int Status { get; set; }
    public bool AlreadyExists { get; set; }
    public int? TargetClassId { get; set; }
    public string Action => AlreadyExists ? "AlreadyExists" : "Create";
}

public class CopyClassesFromYearResultDto
{
    public int SourceAcademicYearId { get; set; }
    public int TargetAcademicYearId { get; set; }
    public int TotalSourceClasses { get; set; }
    public int CreatedCount { get; set; }
    public int SkippedExistingCount { get; set; }
    public List<CopyClassesFromYearPreviewDto> Items { get; set; } = new();
}
