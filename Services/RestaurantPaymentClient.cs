using System.Net.Http.Json;
using System.Text.Json;
using AtendenteWhatssApp.Models;
using AtendenteWhatssApp.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AtendenteWhatssApp.Services;

public sealed class RestaurantPaymentClient
{
    private const string ServiceKeyHeaderName = "X-Internal-Service-Key";
    private readonly HttpClient _httpClient;
    private readonly InternalApiOptions _options;

    public RestaurantPaymentClient(HttpClient httpClient, IOptions<InternalApiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<RestaurantPaymentLinkResult> CreateWhatsAppPaymentAsync(
        RestaurantPaymentLinkRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/internal/delivery-orders/whatsapp-payment")
        {
            Content = JsonContent.Create(request)
        };
        if (!string.IsNullOrWhiteSpace(_options.ServiceKey))
        {
            httpRequest.Headers.Add(ServiceKeyHeaderName, _options.ServiceKey);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalApiException((int)response.StatusCode, body);
        }

        var result = JsonSerializer.Deserialize<RestaurantPaymentLinkResult>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (result is null || string.IsNullOrWhiteSpace(result.CheckoutUrl))
        {
            throw new ExternalApiException(
                StatusCodes.Status502BadGateway,
                "RestaurateAgente returned an invalid payment link response.");
        }

        return result;
    }
}
