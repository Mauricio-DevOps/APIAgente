using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/admin/stores")]
public sealed class AdminStoresController : ControllerBase
{
    [HttpPost("prompt")]
    public async Task<IActionResult> UpsertPrompt(
        [FromBody] StorePromptUpsertRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.UpsertStorePromptAsync(
            request.StoreId.Trim(),
            request.PromptId.Trim(),
            cancellationToken);

        return NoContent();
    }

    [HttpPost("conversation/reset")]
    [HttpPost("database/reset")]
    public async Task<IActionResult> ResetHomologationDatabase(
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.ResetHomologationDataAsync(cancellationToken);

        return NoContent();
    }
}
