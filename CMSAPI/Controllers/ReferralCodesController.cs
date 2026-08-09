using System;
using System.Security.Claims;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMSAPI.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class ReferralCodesController : ControllerBase
    {
        private readonly IReferralCodeService _referralCodeService;
        private readonly IConfiguration _configuration;

        public ReferralCodesController(IReferralCodeService referralCodeService, IConfiguration configuration)
        {
            _referralCodeService = referralCodeService;
            _configuration = configuration;
        }

        private Guid? GetCreatedByUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var uid) ? uid : null;
        }

        [HttpGet("types")]
        [HasPermission("referral-codes.manage")]
        public async Task<IActionResult> GetAllTypes()
        {
            var types = await _referralCodeService.GetAllTypesAsync();
            return Ok(new { success = true, data = types });
        }

        [HttpPost("types")]
        [HasPermission("referral-codes.manage")]
        public async Task<IActionResult> CreateType([FromBody] CreateReferralCodeTypeRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.RewardValue <= 0)
                return BadRequest(new { success = false, message = "Invalid referral code type data." });

            if (request.RewardKind != "PercentageOff" && request.RewardKind != "ExtraMonths")
                return BadRequest(new { success = false, message = "RewardKind must be 'PercentageOff' or 'ExtraMonths'." });

            var type = await _referralCodeService.CreateTypeAsync(request, GetCreatedByUserId());
            return Ok(new { success = true, data = type, message = "Referral code type created." });
        }

        [HttpPut("types/{id}")]
        [HasPermission("referral-codes.manage")]
        public async Task<IActionResult> UpdateType(Guid id, [FromBody] UpdateReferralCodeTypeRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.RewardValue <= 0)
                return BadRequest(new { success = false, message = "Invalid referral code type data." });

            if (request.RewardKind != "PercentageOff" && request.RewardKind != "ExtraMonths")
                return BadRequest(new { success = false, message = "RewardKind must be 'PercentageOff' or 'ExtraMonths'." });

            var type = await _referralCodeService.UpdateTypeAsync(id, request);
            if (type == null) return NotFound(new { success = false, message = "Referral code type not found." });
            return Ok(new { success = true, data = type, message = "Referral code type updated." });
        }

        [HttpGet]
        [HasPermission("referral-codes.manage")]
        public async Task<IActionResult> GetAllCodes()
        {
            var codes = await _referralCodeService.GetAllCodesAsync();
            return Ok(new { success = true, data = codes });
        }

        [HttpPost]
        [HasPermission("referral-codes.manage")]
        public async Task<IActionResult> CreateCode([FromBody] CreateReferralCodeRequest request)
        {
            try
            {
                var code = await _referralCodeService.CreateCodeAsync(request, GetCreatedByUserId());
                return Ok(new { success = true, data = code, message = "Referral code created." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("{id}/deactivate")]
        [HasPermission("referral-codes.manage")]
        public async Task<IActionResult> DeactivateCode(Guid id)
        {
            var code = await _referralCodeService.SetCodeActiveAsync(id, false);
            if (code == null) return NotFound(new { success = false, message = "Referral code not found." });
            return Ok(new { success = true, data = code, message = "Referral code deactivated." });
        }

        [HttpPost("{id}/activate")]
        [HasPermission("referral-codes.manage")]
        public async Task<IActionResult> ActivateCode(Guid id)
        {
            var code = await _referralCodeService.SetCodeActiveAsync(id, true);
            if (code == null) return NotFound(new { success = false, message = "Referral code not found." });
            return Ok(new { success = true, data = code, message = "Referral code activated." });
        }

        // Server-to-server only: easyHMSAPI's hospital registration validates an entered referral
        // code through this, so browsers never need a CMS credential. Same shared-key convention as
        // EasyHmsSubscriptionPlansController's /service endpoint. Read-only -- does not reserve the
        // code (see plan's documented redemption-race limitation).
        [HttpGet("service/validate")]
        [AllowAnonymous]
        public async Task<IActionResult> ValidateForService([FromHeader(Name = "X-Service-Key")] string? serviceKey, [FromQuery] string code)
        {
            var expectedKey = _configuration["ServiceAuth:EasyHmsServiceKey"];
            if (string.IsNullOrEmpty(expectedKey) || serviceKey != expectedKey)
                return Unauthorized();

            var result = await _referralCodeService.ValidateAsync(code);
            return Ok(result);
        }
    }
}
