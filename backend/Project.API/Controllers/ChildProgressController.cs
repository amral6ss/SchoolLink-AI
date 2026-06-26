using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project.BLL.Interfaces;
using Project.Domain.Enums;

namespace Project.API.Controllers;

[Route("api/child-progress")]
[ApiController]
[Authorize(Roles = "Parent")]
public class ChildProgressController : ControllerBase
{
    private readonly IChildProgressService _service;

    public ChildProgressController(IChildProgressService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? term = null)
    {
        var parentId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _service.GetChildProgressAsync(parentId, term);
        return Ok(result);
    }

    [HttpGet("exam-attempt/{examId}")]
    public async Task<IActionResult> GetExamAttempt(int examId)
    {
        var parentId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _service.GetExamAttemptResultAsync(parentId, examId);
        return Ok(result);
    }
}
