using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.ClaudeService.Configuration;
using VoiceCode.ClaudeService.Services;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.ClaudeService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class CodeGenerationController : ControllerBase
{
    private readonly IClaudeService _claudeService;
    private readonly IResponseInterpreterService _interpreter;
    private readonly ITokenCounterService _tokenCounter;
    private readonly ILogger<CodeGenerationController> _logger;

    public CodeGenerationController(
        IClaudeService claudeService,
        IResponseInterpreterService interpreter,
        ITokenCounterService tokenCounter,
        ILogger<CodeGenerationController> logger)
    {
        _claudeService = claudeService;
        _interpreter = interpreter;
        _tokenCounter = tokenCounter;
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<CodeGenerationResponse>> GenerateCode(
        [FromBody] CodeGenerationRequest request)
    {
        var requestId = Guid.NewGuid().ToString();
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("request.id", requestId);
        activity?.SetTag("request.type", request.Type);

        try
        {
            _logger.LogInformation("Processing code generation request {RequestId} of type {Type}",
                requestId, request.Type);

            // Validate token limits
            var inputTokens = _tokenCounter.EstimateTokens(request.Code + request.Instructions);
            if (_tokenCounter.WillExceedLimit(request.Code + request.Instructions, 50000))
            {
                return BadRequest(new { error = "Input exceeds token limit" });
            }

            // Generate code
            var response = await _claudeService.GenerateCodeAsync(request);
            response.RequestId = requestId;

            // Get voice-friendly interpretation if requested
            if (request.Context?.ContainsKey("voice_response") == true)
            {
                var personality = Enum.TryParse<PersonalityProfile>(
                    request.Context.GetValueOrDefault("personality")?.ToString(), 
                    out var p) ? p : PersonalityProfile.FriendlyAssistant;

                var voiceResponse = await _interpreter.InterpretForVoiceAsync(
                    response.RawResponse ?? response.Explanation,
                    request.Type,
                    personality);

                response.VoiceResponse = voiceResponse;
            }

            _logger.LogInformation("Code generation completed for request {RequestId}: {Tokens} tokens used",
                requestId, response.Tokens);

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation for request {RequestId}", requestId);
            return BadRequest(new { error = ex.Message, requestId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code generation failed for request {RequestId}", requestId);
            return StatusCode(500, new { error = "Code generation failed", requestId });
        }
    }

    [HttpPost("explain")]
    public async Task<ActionResult<ExplainResponse>> ExplainCode(
        [FromBody] ExplainRequest request)
    {
        try
        {
            var explanation = await _claudeService.ExplainCodeAsync(
                request.Code, 
                request.Language);

            var response = new ExplainResponse
            {
                Explanation = explanation,
                Language = request.Language
            };

            // Add voice response if requested
            if (request.VoiceResponse)
            {
                var voiceResponse = await _interpreter.InterpretForVoiceAsync(
                    explanation,
                    "explain",
                    request.Personality);

                response.VoiceText = voiceResponse.Text;
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code explanation failed");
            return StatusCode(500, new { error = "Explanation failed" });
        }
    }

    [HttpPost("fix")]
    public async Task<ActionResult<FixResponse>> FixCode(
        [FromBody] FixRequest request)
    {
        try
        {
            var fixedCode = await _claudeService.FixCodeAsync(
                request.Code,
                request.Error,
                request.Language);

            var response = new FixResponse
            {
                FixedCode = fixedCode,
                Language = request.Language,
                OriginalError = request.Error
            };

            // Add voice response if requested
            if (request.VoiceResponse)
            {
                var errorSummary = await _interpreter.GetErrorSummaryAsync(
                    request.Error,
                    "fix");

                response.VoiceText = $"Fixed the {errorSummary}";
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code fix failed");
            return StatusCode(500, new { error = "Fix failed" });
        }
    }

    [HttpPost("refactor")]
    public async Task<ActionResult<RefactorResponse>> RefactorCode(
        [FromBody] RefactorRequest request)
    {
        try
        {
            var refactoredCode = await _claudeService.RefactorCodeAsync(
                request.Code,
                request.Instructions,
                request.Language);

            var response = new RefactorResponse
            {
                RefactoredCode = refactoredCode,
                Language = request.Language,
                Instructions = request.Instructions
            };

            // Add voice response if requested
            if (request.VoiceResponse)
            {
                var voiceResponse = await _interpreter.GetProgressUpdateAsync(
                    $"Refactored code according to: {request.Instructions}");

                response.VoiceText = voiceResponse;
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code refactor failed");
            return StatusCode(500, new { error = "Refactor failed" });
        }
    }

    [HttpPost("voice/interpret")]
    public async Task<ActionResult<VoiceResponse>> InterpretForVoice(
        [FromBody] VoiceInterpretRequest request)
    {
        try
        {
            var response = await _interpreter.InterpretForVoiceAsync(
                request.TechnicalResponse,
                request.Context,
                request.Personality);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice interpretation failed");
            return StatusCode(500, new { error = "Interpretation failed" });
        }
    }

    [HttpGet("personality")]
    public ActionResult<PersonalityProfile[]> GetPersonalities()
    {
        return Ok(Enum.GetValues<PersonalityProfile>());
    }
}

// Request/Response DTOs
public class ExplainRequest
{
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool VoiceResponse { get; set; }
    public PersonalityProfile? Personality { get; set; }
}

public class ExplainResponse
{
    public string Explanation { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? VoiceText { get; set; }
}

public class FixRequest
{
    public string Code { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool VoiceResponse { get; set; }
}

public class FixResponse
{
    public string FixedCode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string OriginalError { get; set; } = string.Empty;
    public string? VoiceText { get; set; }
}

public class RefactorRequest
{
    public string Code { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool VoiceResponse { get; set; }
}

public class RefactorResponse
{
    public string RefactoredCode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string? VoiceText { get; set; }
}

public class VoiceInterpretRequest
{
    public string TechnicalResponse { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public PersonalityProfile? Personality { get; set; }
}