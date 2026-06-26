using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project.BLL.DTOs;
using Project.BLL.Interfaces;

namespace Project.API.Controllers;

[ApiController]
[Route("api/class-management")]
public class ClassController : ControllerBase
{
    private readonly IClassService _classService;
    private readonly IAcademicYearService _academicYearService;

    public ClassController(IClassService classService, IAcademicYearService academicYearService)
    {
        _classService = classService;
        _academicYearService = academicYearService;
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] GetClassesFilter filter)
    {
        if (User.Identity?.IsAuthenticated != true || User.IsInRole("Admin"))
        {
            var result = await _classService.GetAllClassesAsync(filter);
            return Ok(result);
        }

        var yearId = filter.AcademicYearId;
        if (yearId is null)
        {
            var currentYearResult = await _academicYearService.GetCurrentAcademicYearAsync();
            if (!currentYearResult.IsSuccess || currentYearResult.Data is null)
                return NotFound(currentYearResult);

            yearId = currentYearResult.Data.Id;
        }

        var classesResult = await _classService.GetAllClassesAsync(filter);
        if (!classesResult.IsSuccess)
            return BadRequest(classesResult);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim is null)
            return Ok(classesResult);

        var teacherClassesResult = await _classService.GetClassesByTeacherAsync(
            int.Parse(userIdClaim.Value),
            yearId.Value);
        if (!teacherClassesResult.IsSuccess)
            return BadRequest(teacherClassesResult);

        var allowedIds = teacherClassesResult.Data?.Select(c => c.Id).ToHashSet() ?? new HashSet<int>();
        var filteredClasses = classesResult.Data?.Where(c => allowedIds.Contains(c.Id)).ToList()
                              ?? new List<Project.BLL.DTOs.ClassDto>();

        return Ok(filteredClasses);
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!User.IsInRole("Admin"))
        {
            var currentYearResult = await _academicYearService.GetCurrentAcademicYearAsync();
            if (!currentYearResult.IsSuccess || currentYearResult.Data is null)
                return NotFound(currentYearResult);

            var teacherClassesResult = await _classService.GetClassesByTeacherAsync(
                int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value),
                currentYearResult.Data.Id);
            if (!teacherClassesResult.IsSuccess)
                return BadRequest(teacherClassesResult);

            if (!(teacherClassesResult.Data?.Any(c => c.Id == id) ?? false))
                return Forbid();
        }

        var result = await _classService.GetClassByIdAsync(id);
        if (!result.IsSuccess)
            return NotFound(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpGet("{id:int}/students")]
    public async Task<IActionResult> GetWithStudents(int id)
    {
        if (!User.IsInRole("Admin"))
        {
            var currentYearResult = await _academicYearService.GetCurrentAcademicYearAsync();
            if (!currentYearResult.IsSuccess || currentYearResult.Data is null)
                return NotFound(currentYearResult);

            var teacherClassesResult = await _classService.GetClassesByTeacherAsync(
                int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value),
                currentYearResult.Data.Id);
            if (!teacherClassesResult.IsSuccess)
                return BadRequest(teacherClassesResult);

            if (!(teacherClassesResult.Data?.Any(c => c.Id == id) ?? false))
                return Forbid();
        }

        var result = await _classService.GetClassWithStudentsAsync(id);
        if (!result.IsSuccess)
            return NotFound(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("by-grade-level/{gradeLevelId:int}")]
    public async Task<IActionResult> GetByGradeLevel(int gradeLevelId)
    {
        var result = await _classService.GetClassesByGradeLevelAsync(gradeLevelId);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("by-teacher/{teacherId:int}")]
    public async Task<IActionResult> GetByTeacher(int teacherId, [FromQuery] int academicYearId)
    {
        var result = await _classService.GetClassesByTeacherAsync(teacherId, academicYearId);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpGet("my-classes")]
    public async Task<IActionResult> GetMyClasses([FromQuery] int academicYearId)
    {
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var result = await _classService.GetClassesByTeacherAsync(currentUserId, academicYearId);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Teacher")]
    [HttpGet("my-classes/current-year")]
    public async Task<IActionResult> GetMyClassesCurrentYear()
    {
        var currentYearResult = await _academicYearService.GetCurrentAcademicYearAsync();
        if (!currentYearResult.IsSuccess || currentYearResult.Data is null)
            return NotFound(currentYearResult);

        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var result = await _classService.GetClassesByTeacherAsync(currentUserId, currentYearResult.Data.Id);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClassRequest request)
    {
        var result = await _classService.CreateClassAsync(request);
        if (!result.IsSuccess)
            return BadRequest(result);
        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("with-students")]
    public async Task<IActionResult> CreateWithStudents([FromBody] CreateClassWithStudentsRequest request)
    {
        var result = await _classService.CreateClassWithStudentsAsync(request);
        if (!result.IsSuccess)
            return BadRequest(result);
        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("copy-from-year/preview")]
    public async Task<IActionResult> PreviewCopyFromYear([FromBody] CopyClassesFromYearRequest request)
    {
        var result = await _classService.PreviewCopyClassesFromYearAsync(request);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("copy-from-year")]
    public async Task<IActionResult> CopyFromYear([FromBody] CopyClassesFromYearRequest request)
    {
        var result = await _classService.CopyClassesFromYearAsync(request);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateClassRequest request)
    {
        if (id != request.Id)
            return BadRequest("معرّف الرابط لا يطابق معرّف الطلب.");

        var result = await _classService.UpdateClassAsync(request);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int? academicYearId = null)
    {
        var result = await _classService.GetClassStatsAsync(academicYearId);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _classService.DeleteClassAsync(id);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }
}
