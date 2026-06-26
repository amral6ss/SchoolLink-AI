using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Project.BLL.DTOs.Certificates;
using Project.BLL.Interfaces;

namespace Project.API.Controllers
{
    [ApiController]
    [Route("api/certificates")]
    [Authorize]
    public class CertificatesController : ControllerBase
    {
        private readonly ICertificateService _certificateService;

        public CertificatesController(ICertificateService certificateService)
        {
            _certificateService = certificateService;
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetAll()
        {
            var result = await _certificateService.GetAllCertificatesAsync();
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _certificateService.GetCertificateByIdAsync(id);
            if (!result.IsSuccess)
                return NotFound(result);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([FromBody] CreateCertificateRequest request)
        {
            var result = await _certificateService.CreateCertificateAsync(request);
            if (!result.IsSuccess)
                return BadRequest(result);
            return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCertificateRequest request)
        {
            if (id != request.Id)
                return BadRequest("معرّف الرابط لا يطابق معرّف الطلب.");

            var result = await _certificateService.UpdateCertificateAsync(request);
            if (!result.IsSuccess)
                return BadRequest(result);
            return Ok(result);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _certificateService.DeleteCertificateAsync(id);
            if (!result.IsSuccess)
                return BadRequest(result);
            return Ok(result);
        }

        // ════════════════════════════════════════════════════════════════
        //  GENERATE ENDPOINTS
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// GET /api/certificates/{id}/generate?classIds=1,2,3&amp;term=1
        /// Returns per-student certificate data filled from the database.
        /// classIds is a comma-separated list of class IDs.
        /// </summary>
        [HttpGet("{id:int}/generate")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> Generate(int id, [FromQuery] string classIds, [FromQuery] int term = 1)
        {
            var ids = (classIds ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return BadRequest(new { message = "يرجى تحديد فصل واحد على الأقل" });

            var result = await _certificateService.GenerateCertificateDataAsync(id, ids, term);
            if (!result.IsSuccess)
                return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// GET /api/certificates/{id}/grade-sheet?classIds=1,2,3&amp;term=1
        /// Returns a grade sheet (كشف بالدرجات) for all students in the given classes.
        /// </summary>
        [HttpGet("{id:int}/grade-sheet")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> GradeSheet(int id, [FromQuery] string classIds, [FromQuery] int term = 1)
        {
            var ids = (classIds ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return BadRequest(new { message = "يرجى تحديد فصل واحد على الأقل" });

            var result = await _certificateService.GenerateGradeSheetAsync(id, ids, term);
            if (!result.IsSuccess)
                return BadRequest(result);
            return Ok(result);
        }

        /// <summary>
        /// GET /api/certificates/{id}/honor-roll?classIds=1,2,3&amp;term=1&amp;top=10
        /// Returns an honor roll (كشف بأوائل الطلاب) — top N students ranked by total score.
        /// </summary>
        [HttpGet("{id:int}/honor-roll")]
        [Authorize(Roles = "Admin,Teacher")]
        public async Task<IActionResult> HonorRoll(int id, [FromQuery] string classIds, [FromQuery] int term = 1, [FromQuery] int top = 10)
        {
            var ids = (classIds ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return BadRequest(new { message = "يرجى تحديد فصل واحد على الأقل" });

            var result = await _certificateService.GenerateHonorRollAsync(id, ids, term, top);
            if (!result.IsSuccess)
                return BadRequest(result);
            return Ok(result);
        }
    }
}
