using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
public sealed class AdminDashboardController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string? storeId,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return Problem(
                title: "Invalid dashboard query",
                detail: "storeId is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var dashboard = await repository.GetDashboardAsync(storeId, cancellationToken);
        return Ok(dashboard);
    }
}
