using System.Security;
using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("whatsapp")]
public sealed class WhatsappWebhookController : ControllerBase
{
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    [Produces("application/xml")]
    public async Task<IActionResult> Receive(
        [FromForm] TwilioWhatsappWebhookRequest request,
        [FromServices] WhatsappRepository repository,
        [FromServices] AgentFeedbackService feedbackService,
        CancellationToken cancellationToken)
    {
        var message = request.Body?.Trim();
        var phoneNumber = request.From?.Trim();
        var storeId = request.To?.Trim();
        var mediaUrl = request.MediaUrl0?.Trim();
        var mediaContentType = request.MediaContentType0?.Trim();
        var hasAudio = request.NumMedia.GetValueOrDefault() > 0 &&
            !string.IsNullOrWhiteSpace(mediaUrl) &&
            mediaContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

        if (string.IsNullOrWhiteSpace(storeId))
        {
            return TwimlMessage("WhatsApp store key is missing.");
        }

        if ((string.IsNullOrWhiteSpace(message) && !hasAudio) || string.IsNullOrWhiteSpace(phoneNumber))
        {
            return TwimlMessage("Invalid WhatsApp payload.");
        }

        var messageId = string.IsNullOrWhiteSpace(request.MessageSid)
            ? Guid.NewGuid().ToString("N")
            : request.MessageSid.Trim();

        try
        {
            await repository.RecordWhatsappConversationMessageAsync(
                storeId,
                phoneNumber,
                WhatsappConversationMessageDirections.Inbound,
                WhatsappConversationMessageTypes.Customer,
                string.IsNullOrWhiteSpace(message) ? "Audio recebido." : message,
                messageId,
                sourceJobId: null,
                WhatsappConversationMessageStatuses.Received,
                error: null,
                cancellationToken);

            var isAgentEnabled = await repository.IsWhatsappAgentEnabledAsync(
                storeId,
                phoneNumber,
                cancellationToken);
            if (!isAgentEnabled)
            {
                return EmptyTwiml();
            }

            var feedbackResult = await feedbackService.TryPrepareIncomingResponseAsync(
                storeId,
                phoneNumber,
                message,
                mediaUrl,
                mediaContentType,
                cancellationToken);

            if (feedbackResult.ImmediateResponse is not null)
            {
                await RecordSystemTwimlResponseAsync(
                    repository,
                    storeId,
                    phoneNumber,
                    feedbackResult.ImmediateResponse,
                    cancellationToken);
                return TwimlMessage(feedbackResult.ImmediateResponse);
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                const string audioFallbackMessage = "Recebemos seu audio, mas nao encontramos uma solicitacao de feedback aberta. Para pedidos, envie uma mensagem de texto.";
                await RecordSystemTwimlResponseAsync(
                    repository,
                    storeId,
                    phoneNumber,
                    audioFallbackMessage,
                    cancellationToken);
                return TwimlMessage(audioFallbackMessage);
            }

            await repository.EnqueueWhatsappMessageJobAsync(
                messageId,
                storeId,
                phoneNumber,
                message,
                feedbackResult.ShouldAnalyzeText ? feedbackResult.SolicitationId : null,
                cancellationToken);

            return EmptyTwiml();
        }
        catch (InvalidOperationException ex)
        {
            return TwimlMessage(ex.Message);
        }
        catch (ExternalApiException ex)
        {
            return TwimlMessage(ex.ResponseBody);
        }
    }

    private ContentResult TwimlMessage(string message)
    {
        var escapedText = SecurityElement.Escape(message) ?? string.Empty;
        return Content($"<Response><Message>{escapedText}</Message></Response>", "application/xml");
    }

    private ContentResult EmptyTwiml()
    {
        return Content("<Response></Response>", "application/xml");
    }

    private static async Task RecordSystemTwimlResponseAsync(
        WhatsappRepository repository,
        string storeId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken)
    {
        await repository.RecordWhatsappConversationMessageAsync(
            storeId,
            phoneNumber,
            WhatsappConversationMessageDirections.Outbound,
            WhatsappConversationMessageTypes.System,
            message,
            twilioMessageSid: null,
            sourceJobId: null,
            WhatsappConversationMessageStatuses.Sent,
            error: null,
            cancellationToken);
    }
}
