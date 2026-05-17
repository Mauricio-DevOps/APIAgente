using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/ia")]
public sealed class ChatWhatsappController : ControllerBase
{
    [HttpPost("chat-whatsapp")]
    [Consumes("application/json")]
    [Produces("text/plain")]
    public async Task<IActionResult> ChatWhatsapp(
        [FromBody] ChatWhatsappRequest request,
        [FromServices] WhatsappChatService chatService,
        CancellationToken cancellationToken)
    {
        try
        {
            var outputText = await chatService.ProcessAsync(
                request.Message.Trim(),
                request.PhoneNumber.Trim(),
                request.StoreId.Trim(),
                cancellationToken);

            return Content(outputText, "text/plain; charset=utf-8");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                title: "Store is not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ExternalApiException ex)
        {
            return Problem(
                title: "Failed to call prompt API",
                detail: ex.ResponseBody,
                statusCode: ex.StatusCode);
        }
    }
}
