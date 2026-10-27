using System.Security.Claims;
using System.Text.Json;
using FlexiSpace.Core.Common;
using FlexiSpace.Core.Enums;
using FlexiSpace.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexiSpace.API.Controllers
{
    [ApiController]
    [Route("api/sessionaudit")]
    [Authorize]
    public sealed class SessionAuditController : ControllerBase
    {
        private readonly IAuditService _audit;
        private readonly ICurrentUserService _currentUser;

        public SessionAuditController(IAuditService audit, ICurrentUserService currentUser)
        {
            _audit = audit;
            _currentUser = currentUser;
        }

        public sealed record SignInAuditRequest(string? EventId);

        [HttpPost("login")]
        public async Task<IActionResult> RecordLogin([FromBody] SignInAuditRequest? request)
        {
            if (string.IsNullOrWhiteSpace(request?.EventId) || request.EventId.Length > 100)
                return BadRequest(new { message = "A valid sign-in event ID is required." });

            var user = await _currentUser.GetCurrentUserAsync();
            if (user is null)
                return Unauthorized();

            // Save the validated sign-in name with the event as well as the
            // local user relation, so the audit page can identify the person
            // even if a directory row's display fields are stale.
            var actorName = User.FindFirst("name")?.Value
                            ?? User.FindFirst(ClaimTypes.Name)?.Value
                            ?? User.Identity?.Name;
            var actorEmail = User.FindFirst("preferred_username")?.Value
                             ?? User.FindFirst("upn")?.Value
                             ?? User.FindFirst("email")?.Value
                             ?? User.FindFirst(ClaimTypes.Email)?.Value;
            var snapshot = JsonSerializer.Serialize(new { name = actorName, email = actorEmail });
            await _audit.LogSignInOnceAsync(user.Id, request.EventId, snapshot);
            return NoContent();
        }

        [HttpPost("logout")]
        public async Task<IActionResult> RecordLogout()
        {
            var userId = await _currentUser.GetCurrentUserIdAsync();
            if (userId.HasValue)
                await _audit.LogAsync(userId.Value, AuditAction.Logout, "Session", userId.Value.ToString());
            return NoContent();
        }
    }
}