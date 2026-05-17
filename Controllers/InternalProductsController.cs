using System.Security.Cryptography;
using System.Text;
using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Options;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/internal/products")]
public sealed class InternalProductsController : ControllerBase
{
    private const string ServiceKeyHeaderName = "X-Internal-Service-Key";

    [HttpPost("sync-from-menu")]
    public async Task<IActionResult> SyncFromMenu(
        [FromBody] ProductSyncFromMenuRequest request,
        [FromServices] WhatsappRepository repository,
        [FromServices] IOptions<InternalApiOptions> internalApiOptions,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(Request.Headers[ServiceKeyHeaderName].ToString(), internalApiOptions.Value.ServiceKey))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            request.RetailPrice < 0)
        {
            return Problem(
                title: "Invalid product sync",
                detail: "StoreId, Name and a non-negative RetailPrice are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await repository.SyncProductFromMenuAsync(request, cancellationToken);
        return result.Status switch
        {
            ProductSaveStatus.Saved => Ok(result.Product),
            ProductSaveStatus.Conflict => Problem(
                title: "Duplicate product",
                detail: "Another product already uses this name for the selected store.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Problem(
                title: "Product sync failed",
                detail: "The product could not be synchronized.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private static bool IsAuthorized(string providedKey, string configuredKey)
    {
        if (string.IsNullOrWhiteSpace(providedKey) || string.IsNullOrWhiteSpace(configuredKey))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        return providedBytes.Length == configuredBytes.Length &&
            CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}
