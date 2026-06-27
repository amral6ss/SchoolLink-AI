using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project.BLL.DTOs.HistoricalData;
using Project.BLL.Interfaces;
using Project.Domain.Enums;

namespace Project.API.Controllers;

[ApiController]
[Route("api/historical-data")]
[Authorize]
public class HistoricalDataController : ControllerBase
{
    private readonly IHistoricalDataService _service;

    public HistoricalDataController(IHistoricalDataService service)
    {
        _service = service;
    }

    private int UserId => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private UserRole Role => Enum.Parse<UserRole>(User.FindFirst(ClaimTypes.Role)!.Value);

    [HttpGet("years")]
    public async Task<IActionResult> GetYears()
    {
        var result = await _service.GetAccessibleYearsAsync(UserId, Role);
        return Ok(result);
    }

    [HttpGet("classes")]
    public async Task<IActionResult> GetClasses([FromQuery] int academicYearId)
    {
        var result = await _service.GetClassesByYearAsync(UserId, Role, academicYearId);
        return Ok(result);
    }

    [HttpGet("students")]
    public async Task<IActionResult> GetStudents([FromQuery] int classId)
    {
        var result = await _service.GetStudentsByClassAsync(UserId, Role, classId);
        return Ok(result);
    }

    [HttpGet("students/by-year")]
    public async Task<IActionResult> GetStudentsByYear([FromQuery] int academicYearId)
    {
        var result = await _service.GetStudentsByYearAsync(UserId, Role, academicYearId);
        return Ok(result);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromQuery] int classId, [FromQuery] AcademicTerm? term = null)
    {
        var result = await _service.GetClassOverviewAsync(UserId, Role, classId, term);
        return Ok(result);
    }

    [HttpGet("final-grades")]
    public async Task<IActionResult> GetFinalGrades([FromQuery] int classId, [FromQuery] AcademicTerm? term = null, [FromQuery] int? subjectId = null)
    {
        var result = await _service.GetFinalGradesAsync(UserId, Role, classId, term, subjectId);
        return Ok(result);
    }

    [HttpGet("evaluations")]
    public async Task<IActionResult> GetEvaluations([FromQuery] int classId, [FromQuery] int? periodId = null)
    {
        var result = await _service.GetEvaluationsAsync(UserId, Role, classId, periodId);
        return Ok(result);
    }

    [HttpGet("assessments")]
    public async Task<IActionResult> GetAssessments([FromQuery] int classId, [FromQuery] int? subjectId = null, [FromQuery] AcademicTerm? term = null)
    {
        var result = await _service.GetAssessmentsAsync(UserId, Role, classId, subjectId, term);
        return Ok(result);
    }

    [HttpGet("exams")]
    public async Task<IActionResult> GetExams([FromQuery] int enrollmentId)
    {
        var result = await _service.GetExamsAsync(UserId, Role, enrollmentId);
        return Ok(result);
    }

    [HttpGet("assignments")]
    public async Task<IActionResult> GetAssignments([FromQuery] int enrollmentId)
    {
        var result = await _service.GetAssignmentsAsync(UserId, Role, enrollmentId);
        return Ok(result);
    }

    [HttpGet("absences")]
    public async Task<IActionResult> GetAbsences([FromQuery] int classId, [FromQuery] int? subjectId = null)
    {
        var result = await _service.GetAbsencesAsync(UserId, Role, classId, subjectId);
        return Ok(result);
    }

    [HttpGet("student-summary")]
    public async Task<IActionResult> GetStudentSummary([FromQuery] int studentId, [FromQuery] int academicYearId)
    {
        var result = await _service.GetStudentSummaryAsync(UserId, Role, studentId, academicYearId);
        return Ok(result);
    }
}
