using MarkUptv.Models;

namespace MarkUptv.Services;

public static class CheckoutQuotePolicy
{
    // A missing price/currency is not a confirmed quote.
    public static bool IsReady(DeviceStatusResponse? status) =>
        status is { CanWatch: false, Price: > 0 } &&
        !string.IsNullOrWhiteSpace(status.Currency) &&
        status.Currency.Length == 3 &&
        status.Currency.All(char.IsLetter);
}
