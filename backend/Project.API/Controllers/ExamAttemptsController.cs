using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project.BLL.DTOs.ExamAttempt;
using Project.BLL.Interfaces;

namespace Project.API.Controllers;

[ApiController]
[Route("api/exam-attempts")]
[Authorize]
public class ExamAttemptsController : ControllerBase
{
    private readonly IExamAttemptService _service;

    public ExamAttemptsController(IExamAttemptService service)
    {
        _service = service;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (!result.IsSuccess)
            return NotFound(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpGet("by-exam/{examId:int}")]
    public async Task<IActionResult> GetByExam(int examId)
    {
        var result = await _service.GetByExamIdAsync(examId, GetUserId());
        return Ok(result);
    }

    [Authorize(Roles = "Student")]
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] CreateExamAttemptDto dto)
    {
        var result = await _service.StartAttemptAsync(dto);
        if (!result.IsSuccess)
            return BadRequest(result);
        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
    }

    [Authorize(Roles = "Student")]
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitExamAttemptDto dto)
    {
        var result = await _service.SubmitAttemptAsync(dto);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpPatch("{attemptId:int}/grade")]
    public async Task<IActionResult> Grade(int attemptId, [FromBody] GradeEssayAttemptDto dto)
    {
        var result = await _service.GradeEssayAnswersAsync(attemptId, dto, GetUserId());
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    // Legacy endpoint — kept for backward compat, candidates for removal
    [Authorize(Roles = "Admin,Teacher")]
    [HttpPatch("{attemptId:int}/auto-grade")]
    public async Task<IActionResult> AutoGrade(int attemptId)
    {
        var result = await _service.AutoGradeAsync(attemptId);
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpPost("{attemptId:int}/ai-grade-suggestions")]
    public async Task<IActionResult> AiGradeSuggestions(int attemptId)
    {
        var result = await _service.AiGradeSuggestionsAsync(attemptId, GetUserId());
        if (!result.IsSuccess)
            return BadRequest(result);
        return Ok(result);
    }
}