using System.Text.Json;
using FlexiSpace.API.Authorization;
using FlexiSpace.Core.DTOs.Audit;
using FlexiSpace.Core.Entities;
using FlexiSpace.Core.Enums;
using FlexiSpace.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexiSpace.API.Controllers
{
    // Read-only view over the AuditLogs table (see IAuditService - rows are
    // written as a side effect of other controllers' mutating actions, not
    // created directly). Administrator-only: this is exactly the "who did
    // what, at what time" screen the team scoped as part of Denzel's admin
    // dashboard work.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [AuthorizeRoles(UserRole.Administrator)]
    public class AuditLogController : ControllerBase
    {
        private readonly IAuditService _auditService;

        public AuditLogController(IAuditService auditService)
        {
            _auditService = auditService;
        }

        // Retrieves the most recent audit entries across every entity
        // type, newest first - the default "activity feed" view.
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentLogs([FromQuery] int count = 50)
        {
            // Keep the page and the response a sensible size.
            count = Math.Clamp(count, 1, 500);

            var logs = await _auditService.GetRecentLogsAsync(count);

            return Ok(logs.Select(MapToResponseDto));
        }

        // Retrieves the audit history for one specific record - e.g. every
        // change ever made to Booking 42, or Boardroom 7 - for a "history"
        // tab on a detail screen.
        [HttpGet("entity/{entityName}/{entityId}")]
        public async Task<IActionResult> GetLogsForEntity(string entityName, string entityId)
        {
            var logs = await _auditService.GetLogsForEntityAsync(entityName, entityId);

            return Ok(logs.Select(MapToResponseDto));
        }

        private static string? GetActorName(AuditLog log)
        {
            if (log.Action == AuditAction.Login && !string.IsNullOrWhiteSpace(log.NewValues))
            {
                try
                {
                    using var snapshot = JsonDocument.Parse(log.NewValues);
                    var name = snapshot.RootElement.TryGetProperty("name", out var value)
                        ? value.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                    if (snapshot.RootElement.TryGetProperty("email", out var email))
                    {
                        var emailValue = email.GetString();
                        if (!string.IsNullOrWhiteSpace(emailValue))
                            return emailValue;
                    }
                }
                catch (JsonException) { }
            }

            return log.User is null ? null : $"{log.User.FirstName} {log.User.LastName}";
        }
        private static AuditLogResponseDto MapToResponseDto(AuditLog log)
        {
            return new AuditLogResponseDto
            {
                Id = log.Id,
                ActingUserId = log.UserId,
                ActingUserName = GetActorName(log),
                Action = log.Action.ToString(),
                EntityName = log.EntityName,
                EntityId = log.EntityId,
                Timestamp = log.Timestamp,
                OldValues = log.OldValues,
                NewValues = log.NewValues
            };
        }
    }
}
