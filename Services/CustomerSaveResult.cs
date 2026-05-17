using AtendenteWhatssApp.Models;

namespace AtendenteWhatssApp.Services;

public enum CustomerSaveStatus
{
    Saved,
    NotFound,
    Conflict
}

public sealed record CustomerSaveResult(CustomerSaveStatus Status, CustomerResponse? Customer, string? ConflictField = null);
