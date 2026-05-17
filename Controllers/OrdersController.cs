using System.Text.Json;
using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterOrderRequest request,
        [FromServices] OrderRegistrationService orderRegistrationService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.PhoneNumber) ||
            string.IsNullOrWhiteSpace(request.SourceMessageId) ||
            string.IsNullOrWhiteSpace(request.Texto))
        {
            return Problem(
                title: "Invalid order",
                detail: "StoreId, phoneNumber, sourceMessageId and texto are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var aiResponseText = request.Texto.Trim();
        var aiOutputJson = JsonSerializer.Serialize(
            new
            {
                texto = aiResponseText,
                tipo = 2,
                pedido = request.Pedido
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await orderRegistrationService.RegistrarPedidoAsync(
            new OrderRegistrationCommand(
                request.StoreId.Trim(),
                request.PhoneNumber.Trim(),
                request.SourceMessageId.Trim(),
                null,
                null,
                request.CustomerMessage,
                aiResponseText,
                aiOutputJson,
                request.Pedido),
            cancellationToken);

        return Ok(result);
    }
}
