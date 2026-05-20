using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/branding")]
public sealed class AdminBrandingController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBranding(
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid branding request",
                detail: "StoreId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var settings = await repository.GetBrandingSettingsAsync(storeId, cancellationToken);
        return settings is null ? NotFound() : Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> SaveBranding(
        [FromBody] BrandingSettingsUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId) ||
            string.IsNullOrWhiteSpace(request.SiteName) ||
            string.IsNullOrWhiteSpace(request.PaletteKey))
        {
            return Problem(
                title: "Invalid branding request",
                detail: "StoreId, SiteName and PaletteKey are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return Ok(await repository.SaveBrandingSettingsAsync(request, cancellationToken));
        }
        catch (InvalidOperationException error)
        {
            return Problem(
                title: "Invalid branding settings",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
