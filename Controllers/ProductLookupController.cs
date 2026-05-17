using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/ia/products")]
public sealed class ProductLookupController : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<IActionResult> LookupProductDetails(
        [FromQuery] string? storeId,
        [FromQuery] string? name,
        [FromQuery] int? limit,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(name))
        {
            return Problem(
                title: "Invalid product lookup",
                detail: "storeId and name are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var products = await repository.SearchActiveProductDetailsByNameAsync(
            storeId.Trim(),
            name.Trim(),
            limit.GetValueOrDefault(5),
            cancellationToken);

        return Ok(new ProductDetailLookupResponse(name.Trim(), products));
    }
}
