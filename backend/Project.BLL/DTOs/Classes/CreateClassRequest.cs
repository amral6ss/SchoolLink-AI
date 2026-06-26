using System.ComponentModel.DataAnnotations;
using Project.Domain.Enums;

namespace Project.BLL.DTOs;

public class CreateClassRequest
{
    [Range(1, int.MaxValue)]
    public int GradeLevelId { get; set; }

    [Range(1, int.MaxValue)]
    public int AcademicYearId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 500)]
    public int? Capacity { get; set; }

    [EnumDataType(typeof(ClassStatus))]
    public ClassStatus Status { get; set; } = ClassStatus.Active;
}
