using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.BLL.DTOs.Assignment;
using Project.BLL.Interfaces;
using Project.DAL.Context;
using Project.DAL.Interfaces;

namespace Project.API.Controllers;

[ApiController]
[Route("api/assignment-manager")]
[Authorize]
public class AssignmentManagerController : ControllerBase
{
    private readonly IAssignmentManagerService _service;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AppDbContext _context;

    public AssignmentManagerController(
        IAssignmentManagerService service,
        IUnitOfWork unitOfWork,
        AppDbContext context)
    {
        _service = service;
        _unitOfWork = unitOfWork;
        _context = context;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string GetUserRole() =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, int? subjectId, string? status, string? sortBy, int page = 1, int pageSize = 20, int? academicYearId = null)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        int yearId;
        if (academicYearId.HasValue)
        {
            yearId = academicYearId.Value;
        }
        else
        {
            var currentYear = await _context.AcademicYears
                .Where(y => y.IsCurrent && !y.IsDeleted)
                .FirstOrDefaultAsync();
            yearId = currentYear?.Id ?? 0;
        }

        if (role == "Teacher" || role == "Admin")
        {
            var filter = new AssignmentFilterDto
            {
                Search = search,
                SubjectId = subjectId,
                Status = status,
                SortBy = sortBy,
                Page = page,
                PageSize = pageSize,
                TeacherId = role == "Teacher" ? userId : null,
                AcademicYearId = yearId,
            };

            var result = await _service.GetFilteredAsync(filter);
            return Ok(result);
        }

        // Fallback for other roles — basic unfiltered
        var result2 = await _service.GetAllAsync();
        return Ok(result2);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (!result.IsSuccess)
            return NotFound(result);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentManagerDto dto)
    {
        var teacherId = GetUserId();
        var result = await _service.CreateAsync(dto, teacherId);
        if (!result.IsSuccess)
            return BadRequest(result);
        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAssignmentManagerDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var result = await _service.DeleteAsync(id, userId, role);
        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, result);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int? academicYearId)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        int yearId;
        if (academicYearId.HasValue)
        {
            yearId = academicYearId.Value;
        }
        else
        {
            var currentYear = await _context.AcademicYears
                .Where(y => y.IsCurrent && !y.IsDeleted)
                .FirstOrDefaultAsync();
            yearId = currentYear?.Id ?? 0;
        }

        // نستخدم نفس منطق تصفية GetAll (TeacherId + AcademicYearId) حتى تعكس
        // الإحصائيات واجبات المُعلِّم الحالي فقط — بدلاً من الاعتماد على شرط
        // CST واحد كان يفشل لأي معلِّم يُدرِّس أكثر من فصل.
        // PageSize كبيرة لجلب كل الواجبات دفعة واحدة (الإحصائيات على الإجمالي، لا على صفحة واحدة).
        var filter = new AssignmentFilterDto
        {
            TeacherId = role == "Teacher" ? userId : 0,
            AcademicYearId = yearId,
            Page = 1,
            PageSize = 1000,
        };

        var result = await _service.GetStatsAsync(filter);
        return Ok(result);
    }

    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects()
    {
        var userId = GetUserId();
        var role   = GetUserRole();

        if (role == "Teacher")
        {
            // نرجّع بس المواد التي يُدرّسها المعلم الحالي
            var subjectIds = await _context.ClassSubjectTeachers
                .Where(c => c.TeacherId == userId && !c.IsDeleted)
                .Select(c => c.SubjectId)
                .Distinct()
                .ToListAsync();

            var subjects = await _context.Subjects
                .Where(s => subjectIds.Contains(s.Id) && !s.IsDeleted)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync();

            return Ok(subjects);
        }

        var allSubjects = await _context.Subjects
            .Where(s => !s.IsDeleted)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();
        return Ok(allSubjects);
    }

    [HttpGet("classes")]
    public async Task<IActionResult> GetClasses([FromQuery] int? subjectId = null)
    {
        var userId = GetUserId();
        var role   = GetUserRole();

        if (role == "Teacher")
        {
            // نرجّع بس الفصول التي يُدرّسها المعلم الحالي (اختيارياً في مادة محددة)
            var cstQuery = _context.ClassSubjectTeachers
                .Where(c => c.TeacherId == userId && !c.IsDeleted);

            if (subjectId.HasValue)
                cstQuery = cstQuery.Where(c => c.SubjectId == subjectId.Value);

            var classIds = await cstQuery
                .Select(c => c.ClassId)
                .Distinct()
                .ToListAsync();

            var classes = await _context.Classes
                .Include(c => c.GradeLevel)
                .Where(c => classIds.Contains(c.Id) && !c.IsDeleted)
                .Select(c => new { c.Id, Name = c.GradeLevel.Name + " - " + c.Name, c.GradeLevelId })
                .ToListAsync();

            return Ok(classes);
        }

        var allClassesQuery = _context.Classes
            .Include(c => c.GradeLevel)
            .Where(c => !c.IsDeleted);

        if (subjectId.HasValue)
        {
            var classIdsForSubject = await _context.ClassSubjectTeachers
                .Where(c => c.SubjectId == subjectId.Value && !c.IsDeleted)
                .Select(c => c.ClassId)
                .Distinct()
                .ToListAsync();
            allClassesQuery = allClassesQuery.Where(c => classIdsForSubject.Contains(c.Id));
        }

        var allClasses = await allClassesQuery
            .Select(c => new { c.Id, Name = c.GradeLevel.Name + " - " + c.Name, c.GradeLevelId })
            .ToListAsync();
        return Ok(allClasses);
    }

    [HttpGet("{id:int}/submissions")]
    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> GetSubmissions(int id)
    {
        var result = await _service.GetSubmissionsAsync(id);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("{id:int}/submissions/{submissionId:int}")]
    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> GetSubmissionDetail(int id, int submissionId)
    {
        var result = await _service.GetSubmissionDetailAsync(id, submissionId);
        if (!result.IsSuccess)
            return NotFound(result);
        return Ok(result);
    }

    [HttpPost("{id:int}/submissions/{submissionId:int}/grade")]
    [Authorize(Roles = "Admin,Teacher")]
    public async Task<IActionResult> GradeSubmission(int id, int submissionId, [FromBody] GradeAssignmentSubmissionDto dto)
    {
        var result = await _service.GradeSubmissionAsync(id, submissionId, dto);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }
}
