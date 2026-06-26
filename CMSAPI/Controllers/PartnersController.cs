using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class PartnersController : ControllerBase
    {
        private readonly ICmsPartnerService _partnerService;

        public PartnersController(ICmsPartnerService partnerService)
        {
            _partnerService = partnerService;
        }

        [HttpGet]
        [Authorize] // Assume CMS Admins can view this
        public async Task<IActionResult> GetAllPartners()
        {
            var partners = await _partnerService.GetAllPartnersAsync();
            return Ok(new { success = true, data = partners });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreatePartner([FromBody] CreatePartnerRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? createdBy = null;
            if (Guid.TryParse(userIdClaim, out var uid))
                createdBy = uid;

            var partner = await _partnerService.CreatePartnerAsync(request, createdBy);
            return Ok(new { success = true, data = partner, message = "Partner created successfully." });
        }

        // Public endpoint, secured by the unguessable token
        [HttpGet("dashboard/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPartnerDashboard(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return BadRequest(new { success = false, message = "Token is required." });

            var stats = await _partnerService.GetDashboardStatsByTokenAsync(token);
            if (stats == null)
                return NotFound(new { success = false, message = "Invalid or expired partner token." });

            return Ok(new { success = true, data = stats });
        }
    }
}
