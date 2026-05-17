using AtendenteWhatssApp.Models;

namespace AtendenteWhatssApp.Services;

public enum ProductSaveStatus
{
    Saved,
    NotFound,
    Conflict
}

public sealed record ProductSaveResult(ProductSaveStatus Status, ProductResponse? Product);
