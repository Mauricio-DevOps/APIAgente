using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/agent")]
public sealed class AdminAgentController : ControllerBase
{
    [HttpGet("persona")]
    public async Task<IActionResult> GetPersona(
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid agent persona query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(await repository.GetAgentPersonaAsync(storeId.Trim(), cancellationToken));
    }

    [HttpPut("persona")]
    public async Task<IActionResult> SavePersona(
        [FromBody] AgentPersonaSettingsUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.Tone) ||
            !AgentPersonaTones.IsValid(request.Tone.Trim().ToUpperInvariant()))
        {
            return Problem(
                title: "Invalid agent persona request",
                detail: "storeId and a valid tone are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        foreach (var faq in request.Faqs ?? Array.Empty<AgentPersonaFaqUpsert>())
        {
            var hasQuestion = !string.IsNullOrWhiteSpace(faq.Question);
            var hasAnswer = !string.IsNullOrWhiteSpace(faq.Answer);
            if (hasQuestion != hasAnswer)
            {
                return Problem(
                    title: "Invalid agent persona FAQ",
                    detail: "Each FAQ must have both question and answer.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var saved = await repository.UpsertAgentPersonaAsync(
            request with
            {
                StoreId = request.StoreId.Trim(),
                Tone = AgentPersonaTones.Normalize(request.Tone)
            },
            cancellationToken);

        return Ok(saved);
    }

    [HttpGet("notifications/settings")]
    public async Task<IActionResult> GetNotificationSettings(
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid agent notification settings query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(await repository.GetAgentNotificationSettingsAsync(storeId.Trim(), cancellationToken));
    }

    [HttpPut("notifications/settings")]
    public async Task<IActionResult> SaveNotificationSettings(
        [FromBody] AgentNotificationSettingsUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId))
        {
            return Problem(
                title: "Invalid agent notification settings request",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var saved = await repository.UpsertAgentNotificationSettingsAsync(
            request with
            {
                StoreId = request.StoreId.Trim(),
                StaffNotificationPhoneNumber = request.StaffNotificationPhoneNumber?.Trim()
            },
            cancellationToken);

        return Ok(saved);
    }

    [HttpGet("feedback/settings")]
    public async Task<IActionResult> GetFeedbackSettings(
        [FromQuery] string? storeId,
        [FromServices] AgentFeedbackService feedbackService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid feedback settings query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(await feedbackService.GetSettingsAsync(storeId.Trim(), cancellationToken));
    }

    [HttpPut("feedback/settings")]
    public async Task<IActionResult> SaveFeedbackSettings(
        [FromBody] AgentFeedbackSettingsUpsertRequest request,
        [FromServices] AgentFeedbackService feedbackService,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateFeedbackSettingsRequest(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var saved = await feedbackService.SaveSettingsAsync(
            request with
            {
                StoreId = request.StoreId.Trim(),
                AcceptedFormat = AgentFeedbackFormats.Normalize(request.AcceptedFormat),
                RequestMessage = request.RequestMessage?.Trim()
            },
            cancellationToken);

        return Ok(saved);
    }

    [HttpGet("feedback/solicitations")]
    public async Task<IActionResult> ListFeedbackSolicitations(
        [FromQuery] string? storeId,
        [FromServices] AgentFeedbackService feedbackService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid feedback solicitation query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(await feedbackService.GetSolicitationsAsync(storeId.Trim(), cancellationToken));
    }

    [HttpPost("feedback/solicitations/{solicitationId}/send")]
    public async Task<IActionResult> SendFeedbackSolicitation(
        [FromRoute] string solicitationId,
        [FromQuery] string? storeId,
        [FromServices] AgentFeedbackService feedbackService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(solicitationId))
        {
            return Problem(
                title: "Invalid feedback solicitation send",
                detail: "storeId and solicitationId are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var solicitation = await feedbackService.SendSolicitationAsync(
            storeId.Trim(),
            solicitationId.Trim(),
            cancellationToken);

        return solicitation is null ? NotFound() : Ok(solicitation);
    }

    [HttpGet("automated-campaigns")]
    public async Task<IActionResult> ListAutomatedCampaigns(
        [FromQuery] string? storeId,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid automated campaign query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(await agentService.GetAutomatedCampaignsAsync(storeId.Trim(), cancellationToken));
    }

    [HttpPost("automated-campaigns")]
    public async Task<IActionResult> CreateAutomatedCampaign(
        [FromBody] AgentAutomatedCampaignUpsertRequest request,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        var validationProblem = ValidateAutomatedCampaignRequest(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var campaign = await agentService.SaveAutomatedCampaignAsync(
            request with
            {
                Id = null,
                StoreId = request.StoreId.Trim(),
                Type = AgentAutomatedCampaignTypes.Normalize(request.Type),
                Name = request.Name.Trim(),
                ProductId = string.IsNullOrWhiteSpace(request.ProductId) ? null : request.ProductId.Trim(),
                Message = request.Message.Trim()
            },
            cancellationToken);

        return campaign is null ? NotFound() : Ok(campaign);
    }

    [HttpPut("automated-campaigns/{campaignId}")]
    public async Task<IActionResult> UpdateAutomatedCampaign(
        [FromRoute] string campaignId,
        [FromBody] AgentAutomatedCampaignUpsertRequest request,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
        {
            return Problem(
                title: "Invalid automated campaign",
                detail: "campaignId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var validationProblem = ValidateAutomatedCampaignRequest(request);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var campaign = await agentService.SaveAutomatedCampaignAsync(
            request with
            {
                Id = campaignId.Trim(),
                StoreId = request.StoreId.Trim(),
                Type = AgentAutomatedCampaignTypes.Normalize(request.Type),
                Name = request.Name.Trim(),
                ProductId = string.IsNullOrWhiteSpace(request.ProductId) ? null : request.ProductId.Trim(),
                Message = request.Message.Trim()
            },
            cancellationToken);

        return campaign is null ? NotFound() : Ok(campaign);
    }

    [HttpPost("automated-campaigns/{campaignId}/run")]
    public async Task<IActionResult> RunAutomatedCampaign(
        [FromRoute] string campaignId,
        [FromQuery] string? storeId,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(campaignId))
        {
            return Problem(
                title: "Invalid automated campaign run",
                detail: "storeId and campaignId are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var run = await agentService.RunAutomatedCampaignAsync(
            storeId.Trim(),
            campaignId.Trim(),
            cancellationToken);

        return run is null ? NotFound() : Ok(run);
    }

    [HttpDelete("automated-campaigns/{campaignId}")]
    public async Task<IActionResult> DeleteAutomatedCampaign(
        [FromRoute] string campaignId,
        [FromQuery] string? storeId,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(campaignId))
        {
            return Problem(
                title: "Invalid automated campaign delete",
                detail: "storeId and campaignId are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var deleted = await agentService.DeleteAutomatedCampaignAsync(
            storeId.Trim(),
            campaignId.Trim(),
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("product-campaign/preview")]
    public async Task<IActionResult> PreviewProductCampaign(
        [FromQuery] string? storeId,
        [FromQuery] string? productId,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(productId))
        {
            return Problem(
                title: "Invalid campaign preview query",
                detail: "storeId and productId are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var preview = await agentService.GetProductCampaignPreviewAsync(
            storeId.Trim(),
            productId.Trim(),
            cancellationToken);

        return preview is null
            ? NotFound()
            : Ok(preview);
    }

    [HttpPost("product-campaign/send")]
    public async Task<IActionResult> SendProductCampaign(
        [FromBody] AgentProductCampaignSendRequest request,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.ProductId) ||
            string.IsNullOrWhiteSpace(request.Message))
        {
            return Problem(
                title: "Invalid campaign send request",
                detail: "storeId, productId and message are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await agentService.SendProductCampaignAsync(
            request.StoreId.Trim(),
            request.ProductId.Trim(),
            request.Message.Trim(),
            cancellationToken);

        return result is null
            ? NotFound()
            : Ok(result);
    }

    [HttpGet("customers")]
    public async Task<IActionResult> ListCustomers(
        [FromQuery] string? storeId,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid customer recurrence query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var customers = await agentService.GetCustomerRecurrencesAsync(storeId.Trim(), cancellationToken);
        return Ok(customers);
    }

    [HttpPost("customer-reminder/send")]
    public async Task<IActionResult> SendCustomerReminder(
        [FromBody] AgentCustomerReminderSendRequest request,
        [FromServices] AgentService agentService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.PhoneNumber) ||
            string.IsNullOrWhiteSpace(request.Message))
        {
            return Problem(
                title: "Invalid customer reminder request",
                detail: "storeId, phoneNumber and message are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var outcome = await agentService.SendCustomerReminderAsync(
            request.StoreId.Trim(),
            request.PhoneNumber.Trim(),
            request.Message.Trim(),
            cancellationToken);

        if (!outcome.CustomerFound)
        {
            return NotFound();
        }

        return Ok(outcome.Result);
    }

    private ObjectResult? ValidateFeedbackSettingsRequest(AgentFeedbackSettingsUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId))
        {
            return Problem(
                title: "Invalid feedback settings",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.PostOrderDelayMinutes is <= 0 ||
            request.PeriodicSurveyDays is <= 0 ||
            request.PeriodicSurveySampleSize is <= 0)
        {
            return Problem(
                title: "Invalid feedback settings",
                detail: "Delay, periodicity and sample size must be greater than zero.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var acceptedFormat = AgentFeedbackFormats.Normalize(request.AcceptedFormat);
        if (acceptedFormat is not (AgentFeedbackFormats.Text or AgentFeedbackFormats.Audio or AgentFeedbackFormats.Both))
        {
            return Problem(
                title: "Invalid feedback settings",
                detail: "Accepted format must be TEXT, AUDIO or BOTH.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private ObjectResult? ValidateAutomatedCampaignRequest(AgentAutomatedCampaignUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Message) ||
            !AgentAutomatedCampaignTypes.IsValid(request.Type?.Trim().ToUpperInvariant()))
        {
            return Problem(
                title: "Invalid automated campaign",
                detail: "storeId, type, name and message are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var type = AgentAutomatedCampaignTypes.Normalize(request.Type);
        if (type == AgentAutomatedCampaignTypes.ProductStock &&
            string.IsNullOrWhiteSpace(request.ProductId))
        {
            return Problem(
                title: "Invalid automated campaign",
                detail: "Product stock campaigns require productId.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!string.IsNullOrWhiteSpace(request.DailyRunTime) &&
            !TimeOnly.TryParse(request.DailyRunTime.Trim(), out _))
        {
            return Problem(
                title: "Invalid automated campaign",
                detail: "DailyRunTime must be a valid time.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.CooldownDays is < 1 ||
            request.InactiveDaysThreshold is < 1)
        {
            return Problem(
                title: "Invalid automated campaign",
                detail: "CooldownDays and inactiveDaysThreshold must be greater than zero.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }
}
