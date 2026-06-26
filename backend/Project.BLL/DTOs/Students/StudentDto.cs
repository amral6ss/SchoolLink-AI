using Project.Domain.Enums;

namespace Project.BLL.DTOs.Students;

public class StudentDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? BirthDate { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    public bool IsActive { get; set; }
    public StudentLifecycleStatus LifecycleStatus { get; set; }
    public string LifecycleStatusName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
