using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AtendenteWhatssApp.Options;
using Microsoft.Extensions.Options;

namespace AtendenteWhatssApp.Services;

public sealed class TwilioMessageClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioMessageClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<TwilioMessageSendResult> SendWhatsappMessageAsync(
        string from,
        string to,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured.");
        }

        var endpoint = $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["From"] = from,
                ["To"] = to,
                ["Body"] = body
            })
        };

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalApiException((int)response.StatusCode, responseBody);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<TwilioMessageResponse>(
                responseBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            return new TwilioMessageSendResult(payload?.Sid, payload?.Status);
        }
        catch (JsonException)
        {
            return new TwilioMessageSendResult(Sid: null, Status: null);
        }
    }

    private sealed record TwilioMessageResponse(string? Sid, string? Status);
}

public sealed record TwilioMessageSendResult(string? Sid, string? Status);
