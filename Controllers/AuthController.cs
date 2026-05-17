using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtendenteWhatssApp.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] WhatsappRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Problem(
                title: "Invalid login",
                detail: "Username and password are required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var company = await repository.AuthenticateCompanyAsync(
            request.Username,
            request.Password,
            cancellationToken);

        return company is null ? Unauthorized() : Ok(company);
    }
}
