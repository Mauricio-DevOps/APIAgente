using System.Security.Cryptography;
using System.Text;
using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Options;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/internal/companies")]
public sealed class InternalCompaniesController : ControllerBase
{
    private const string ServiceKeyHeaderName = "X-Internal-Service-Key";

    [HttpPost("sync")]
    public async Task<IActionResult> SyncCompany(
        [FromBody] InternalCompanySyncRequest request,
        [FromServices] WhatsappRepository repository,
        [FromServices] IOptions<InternalApiOptions> internalApiOptions,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(Request.Headers[ServiceKeyHeaderName].ToString(), internalApiOptions.Value.ServiceKey))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CompanyName) || string.IsNullOrWhiteSpace(request.CompanyPhone))
        {
            return Problem(
                title: "Invalid company sync",
                detail: "CompanyName and CompanyPhone are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var company = await repository.SyncCompanyAsync(
                request.CompanyName,
                request.CompanyPhone,
                request.PreviousCompanyPhone,
                cancellationToken);

            return Ok(company);
        }
        catch (InvalidOperationException error)
        {
            return Problem(
                title: "Company sync conflict",
                detail: error.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
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
