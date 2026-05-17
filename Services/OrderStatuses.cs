namespace AtendenteWhatssApp.Services;

public static class OrderStatuses
{
    public const string PendingReview = "PendingReview";
    public const string EmProducao = "EmProducao";
    public const string EmRotaEntrega = "EmRotaEntrega";
    public const string Concluido = "Concluido";

    public static bool IsValid(string? status)
    {
        return Normalize(status) is not null;
    }

    public static bool IsActive(string? status)
    {
        var normalized = Normalize(status);
        return normalized is EmProducao or EmRotaEntrega;
    }

    public static string? Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return status.Trim() switch
        {
            PendingReview => PendingReview,
            EmProducao => EmProducao,
            EmRotaEntrega => EmRotaEntrega,
            Concluido => Concluido,
            _ => null
        };
    }
}
