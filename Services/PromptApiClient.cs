using System.Net.Http.Json;
using System.Text.Json;
using AtendenteWhatssApp.Models;

namespace AtendenteWhatssApp.Services;

public sealed class PromptApiClient
{
    private const string BaseEndpoint = "https://apiiadev-gjenhsf0cpbvg3hm.canadacentral-01.azurewebsites.net/api/IA/prompts";
    private readonly HttpClient _httpClient;

    public PromptApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PromptApiMessageResponse> SendMessageAsync(
        string promptId,
        PromptApiMessageRequest request,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{BaseEndpoint}/{Uri.EscapeDataString(promptId)}/messages";

        using var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalApiException((int)response.StatusCode, responseBody);
        }

        var payload = JsonSerializer.Deserialize<PromptApiResponse>(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (payload is null || string.IsNullOrWhiteSpace(payload.ResponseId))
        {
            throw new ExternalApiException(StatusCodes.Status502BadGateway, "Prompt API returned an invalid response.");
        }

        return new PromptApiMessageResponse(
            payload.ResponseId,
            string.IsNullOrWhiteSpace(payload.ConversationId) ? null : payload.ConversationId,
            payload.OutputText ?? string.Empty);
    }

    private sealed record PromptApiResponse(string? ResponseId, string? ConversationId, string? OutputText);
}
