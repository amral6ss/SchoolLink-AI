using System.ComponentModel.DataAnnotations;

namespace Project.BLL.DTOs.Enrollments;

public enum StudentProgressionActionType
{
    Promote = 1,
    Retain = 2,
    Graduate = 3
}

public class StudentProgressionRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "يجب اختيار طالب واحد على الأقل")]
    public List<int> EnrollmentIds { get; set; } = new();

    [EnumDataType(typeof(StudentProgressionActionType), ErrorMessage = "نوع العملية غير صالح")]
    public StudentProgressionActionType Action { get; set; }

    public int? TargetClassId { get; set; }
    public int? TargetAcademicYearId { get; set; }
    public List<StudentProgressionClassMappingRequest> ClassMappings { get; set; } = new();

    [Required]
    public DateOnly EffectiveDate { get; set; }

    public decimal PassingThreshold { get; set; } = 50m;

    [MaxLength(500)]
    public string? Note { get; set; }
}

public class StudentProgressionClassMappingRequest
{
    public int SourceClassId { get; set; }
    public int TargetClassId { get; set; }
}
