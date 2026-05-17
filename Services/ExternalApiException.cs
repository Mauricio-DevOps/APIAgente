namespace AtendenteWhatssApp.Services;

public sealed class ExternalApiException : Exception
{
    public ExternalApiException(int statusCode, string responseBody)
        : base($"External API returned status code {statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public string ResponseBody { get; }
}
