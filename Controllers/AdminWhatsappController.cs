using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/whatsapp/conversations")]
public sealed class AdminWhatsappController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListConversations(
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid WhatsApp conversation query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var conversations = await repository.ListWhatsappConversationsAsync(
            storeId.Trim(),
            cancellationToken);
        return Ok(conversations);
    }

    [HttpGet("{phoneNumber}/messages")]
    public async Task<IActionResult> ListMessages(
        [FromRoute] string phoneNumber,
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        return await ListMessagesCoreAsync(storeId, phoneNumber, repository, cancellationToken);
    }

    [HttpGet("messages")]
    public async Task<IActionResult> ListMessagesByQuery(
        [FromQuery] string? storeId,
        [FromQuery] string? phoneNumber,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        return await ListMessagesCoreAsync(storeId, phoneNumber, repository, cancellationToken);
    }

    private async Task<IActionResult> ListMessagesCoreAsync(
        string? storeId,
        string? phoneNumber,
        WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(phoneNumber))
        {
            return Problem(
                title: "Invalid WhatsApp message query",
                detail: "storeId and phoneNumber are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var messages = await repository.GetWhatsappConversationMessagesAsync(
            storeId.Trim(),
            phoneNumber.Trim(),
            limit: 200,
            cancellationToken);
        return Ok(messages);
    }

    [HttpPatch("{phoneNumber}/agent")]
    public async Task<IActionResult> UpdateAgent(
        [FromRoute] string phoneNumber,
        [FromBody] WhatsappContactAgentUpdateRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        return await UpdateAgentCoreAsync(phoneNumber, request, repository, cancellationToken);
    }

    [HttpPatch("agent")]
    public async Task<IActionResult> UpdateAgentByQuery(
        [FromQuery] string? phoneNumber,
        [FromBody] WhatsappContactAgentUpdateRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        return await UpdateAgentCoreAsync(phoneNumber, request, repository, cancellationToken);
    }

    private async Task<IActionResult> UpdateAgentCoreAsync(
        string? phoneNumber,
        WhatsappContactAgentUpdateRequest request,
        WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(request.StoreId))
        {
            return Problem(
                title: "Invalid WhatsApp agent request",
                detail: "storeId and phoneNumber are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await repository.SetWhatsappAgentEnabledAsync(
            request.StoreId.Trim(),
            phoneNumber.Trim(),
            request.IsAgentEnabled,
            cancellationToken);

        return Ok(new WhatsappContactAgentResponse(phoneNumber.Trim(), request.IsAgentEnabled));
    }

    [HttpPost("{phoneNumber}/reset")]
    public async Task<IActionResult> ResetConversation(
        [FromRoute] string phoneNumber,
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        return await ResetConversationCoreAsync(storeId, phoneNumber, repository, cancellationToken);
    }

    [HttpPost("reset")]
    public async Task<IActionResult> ResetConversationByQuery(
        [FromQuery] string? storeId,
        [FromQuery] string? phoneNumber,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        return await ResetConversationCoreAsync(storeId, phoneNumber, repository, cancellationToken);
    }

    private async Task<IActionResult> ResetConversationCoreAsync(
        string? storeId,
        string? phoneNumber,
        WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(phoneNumber))
        {
            return Problem(
                title: "Invalid WhatsApp conversation reset",
                detail: "storeId and phoneNumber are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await repository.ClearWhatsappConversationHistoryAsync(
            storeId.Trim(),
            phoneNumber.Trim(),
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{phoneNumber}/messages")]
    public async Task<IActionResult> SendMessage(
        [FromRoute] string phoneNumber,
        [FromBody] WhatsappManualMessageRequest request,
        [FromServices] WhatsappRepository repository,
        [FromServices] TwilioMessageClient twilioMessageClient,
        CancellationToken cancellationToken)
    {
        return await SendMessageCoreAsync(phoneNumber, request, repository, twilioMessageClient, cancellationToken);
    }

    [HttpPost("messages")]
    public async Task<IActionResult> SendMessageByQuery(
        [FromQuery] string? phoneNumber,
        [FromBody] WhatsappManualMessageRequest request,
        [FromServices] WhatsappRepository repository,
        [FromServices] TwilioMessageClient twilioMessageClient,
        CancellationToken cancellationToken)
    {
        return await SendMessageCoreAsync(phoneNumber, request, repository, twilioMessageClient, cancellationToken);
    }

    private async Task<IActionResult> SendMessageCoreAsync(
        string? phoneNumber,
        WhatsappManualMessageRequest request,
        WhatsappRepository repository,
        TwilioMessageClient twilioMessageClient,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) ||
            string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.Message))
        {
            return Problem(
                title: "Invalid WhatsApp manual message",
                detail: "storeId, phoneNumber and message are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var storeId = request.StoreId.Trim();
        var normalizedPhoneNumber = phoneNumber.Trim();
        var message = request.Message.Trim();

        TwilioMessageSendResult sendResult;
        try
        {
            sendResult = await twilioMessageClient.SendWhatsappMessageAsync(
                from: storeId,
                to: normalizedPhoneNumber,
                body: message,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await TryRecordManualMessageAsync(
                repository,
                storeId,
                normalizedPhoneNumber,
                message,
                twilioMessageSid: null,
                WhatsappConversationMessageStatuses.Failed,
                ex.Message,
                cancellationToken);

            var statusCode = ex is ExternalApiException externalApiException
                ? externalApiException.StatusCode
                : StatusCodes.Status502BadGateway;

            return Problem(
                title: "Failed to send WhatsApp message",
                detail: ex is ExternalApiException external ? external.ResponseBody : ex.Message,
                statusCode: statusCode);
        }

        try
        {
            var storedMessage = await repository.RecordWhatsappConversationMessageAsync(
                storeId,
                normalizedPhoneNumber,
                WhatsappConversationMessageDirections.Outbound,
                WhatsappConversationMessageTypes.Agent,
                message,
                sendResult.Sid,
                sourceJobId: null,
                WhatsappConversationMessageStatuses.Sent,
                error: null,
                cancellationToken);

            return Ok(storedMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Ok(new WhatsappConversationMessageResponse(
                Guid.NewGuid().ToString("N"),
                normalizedPhoneNumber,
                WhatsappConversationMessageDirections.Outbound,
                WhatsappConversationMessageTypes.Agent,
                message,
                sendResult.Sid,
                null,
                WhatsappConversationMessageStatuses.Sent,
                "Mensagem enviada, mas nao foi registrada no historico.",
                DateTime.UtcNow.ToString("O")));
        }
    }

    private static async Task TryRecordManualMessageAsync(
        WhatsappRepository repository,
        string storeId,
        string phoneNumber,
        string message,
        string? twilioMessageSid,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.RecordWhatsappConversationMessageAsync(
                storeId,
                phoneNumber,
                WhatsappConversationMessageDirections.Outbound,
                WhatsappConversationMessageTypes.Agent,
                message,
                twilioMessageSid,
                sourceJobId: null,
                status,
                error,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Preserve the Twilio error response even if the failed-send history write also fails.
        }
    }
}
