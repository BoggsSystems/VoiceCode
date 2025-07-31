using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;
using VoiceCode.OrchestratorService.Services;

namespace VoiceCode.OrchestratorService.Controllers
{
    [ApiController]
    [Route("api/v2/orchestrate")]
    public class PhaseAwareOrchestrationController : ControllerBase
    {
        private readonly ILogger<PhaseAwareOrchestrationController> _logger;
        private readonly IPhaseAwareOrchestrationService _orchestrationService;

        public PhaseAwareOrchestrationController(
            ILogger<PhaseAwareOrchestrationController> logger,
            IPhaseAwareOrchestrationService orchestrationService)
        {
            _logger = logger;
            _orchestrationService = orchestrationService;
        }

        [HttpPost("voice-command")]
        public async Task<IActionResult> ProcessVoiceCommand([FromBody] VoiceCommandRequest request)
        {
            if (string.IsNullOrEmpty(request?.TranscribedText))
            {
                return BadRequest(new { error = "Transcribed text is required" });
            }

            try
            {
                _logger.LogInformation("Received voice command: {Command} from user {UserId}", 
                    request.TranscribedText, request.UserId);

                var result = await _orchestrationService.ProcessVoiceCommandAsync(
                    request.UserId ?? "default-user",
                    request.TranscribedText);

                return Ok(new VoiceCommandResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    VoiceSummary = result.VoiceSummary,
                    SessionId = result.SessionId,
                    Phase = result.Phase,
                    WorkerId = result.WorkerId,
                    Metadata = result.Metadata,
                    AudioUrl = result.AudioUrl,
                    ResponseFormat = DetermineResponseFormat(result)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing voice command");
                return StatusCode(500, new 
                { 
                    error = "Failed to process voice command",
                    message = ex.Message
                });
            }
        }

        [HttpGet("sessions")]
        public async Task<IActionResult> GetUserSessions([FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest(new { error = "UserId is required" });
            }

            try
            {
                var sessions = await _orchestrationService.GetUserSessionsAsync(userId);
                
                var response = sessions.Select(s => new SessionSummary
                {
                    SessionId = s.SessionId,
                    Title = s.Title,
                    Description = s.Description,
                    CurrentPhase = s.CurrentPhase.ToString(),
                    CreatedAt = s.CreatedAt,
                    LastUpdatedAt = s.LastUpdatedAt,
                    WorkerId = s.AssignedWorkerId,
                    Status = s.WorkerStatus?.ToString() ?? "Active"
                }).ToList();

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user sessions");
                return StatusCode(500, new { error = "Failed to get sessions" });
            }
        }

        [HttpGet("sessions/{sessionId}")]
        public async Task<IActionResult> GetSessionDetails(string sessionId)
        {
            try
            {
                var session = await _orchestrationService.GetSessionStatusAsync(sessionId);
                
                if (session == null)
                {
                    return NotFound(new { error = "Session not found" });
                }

                return Ok(new SessionDetails
                {
                    SessionId = session.SessionId,
                    Title = session.Title,
                    Description = session.Description,
                    CurrentPhase = session.CurrentPhase.ToString(),
                    CreatedAt = session.CreatedAt,
                    LastUpdatedAt = session.LastUpdatedAt,
                    WorkerId = session.AssignedWorkerId,
                    Status = session.WorkerStatus?.ToString() ?? "Active",
                    ConversationHistory = session.History.Select(h => new ConversationTurnSummary
                    {
                        Timestamp = h.Timestamp,
                        Phase = h.Phase.ToString(),
                        UserInput = h.UserInput,
                        SystemResponse = h.SystemResponse
                    }).ToList(),
                    BusinessContext = session.BusinessContext,
                    ProductContext = session.ProductContext,
                    TechnicalContext = session.TechnicalContext,
                    ArchitectureContext = session.ArchitectureContext
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting session details");
                return StatusCode(500, new { error = "Failed to get session details" });
            }
        }

        [HttpPost("sessions/{sessionId}/continue")]
        public async Task<IActionResult> ContinueSession(string sessionId, [FromBody] ContinueSessionRequest request)
        {
            try
            {
                var session = await _orchestrationService.GetSessionStatusAsync(sessionId);
                
                if (session == null)
                {
                    return NotFound(new { error = "Session not found" });
                }

                var result = await _orchestrationService.ProcessVoiceCommandAsync(
                    session.UserId,
                    request.Command);

                return Ok(new VoiceCommandResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    SessionId = result.SessionId,
                    Phase = result.Phase,
                    WorkerId = result.WorkerId,
                    Metadata = result.Metadata,
                    ResponseFormat = DetermineResponseFormat(result)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error continuing session");
                return StatusCode(500, new { error = "Failed to continue session" });
            }
        }

        private string DetermineResponseFormat(OrchestrationResult result)
        {
            // Determine if response should be spoken, displayed, or both
            if (result.Phase == ConversationPhase.Implementation.ToString())
            {
                return "status"; // Brief status update
            }
            else if (result.Metadata?.ContainsKey("businessAnalysis") == true ||
                     result.Metadata?.ContainsKey("technicalDesign") == true)
            {
                return "detailed"; // Show full analysis
            }
            else
            {
                return "conversational"; // Normal conversation
            }
        }
    }

    public class VoiceCommandRequest
    {
        public string UserId { get; set; }
        public string TranscribedText { get; set; }
        public string AudioUrl { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }

    public class VoiceCommandResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string VoiceSummary { get; set; }
        public string SessionId { get; set; }
        public string Phase { get; set; }
        public string WorkerId { get; set; }
        public string AudioUrl { get; set; }
        public string ResponseFormat { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }

    public class SessionSummary
    {
        public string SessionId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string CurrentPhase { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public string WorkerId { get; set; }
        public string Status { get; set; }
    }

    public class SessionDetails : SessionSummary
    {
        public List<ConversationTurnSummary> ConversationHistory { get; set; }
        public BusinessAnalysis BusinessContext { get; set; }
        public ProductDesign ProductContext { get; set; }
        public TechnicalDesign TechnicalContext { get; set; }
        public ArchitectureDesign ArchitectureContext { get; set; }
    }

    public class ConversationTurnSummary
    {
        public DateTime Timestamp { get; set; }
        public string Phase { get; set; }
        public string UserInput { get; set; }
        public string SystemResponse { get; set; }
    }

    public class ContinueSessionRequest
    {
        public string Command { get; set; }
    }
}