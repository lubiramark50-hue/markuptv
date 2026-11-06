using System;

namespace MarkUptv.Services;

public enum PaymentNavigationOutcome
{
    None,
    Success,
    Failure
}

/// <summary>
/// Classifies checkout completion navigation only from the app callback
/// scheme or the API's configured origin. A matching substring in an
/// unrelated site, query string or longer path must not complete payment.
/// </summary>
public static class PaymentNavigationGuard
{
    public static PaymentNavigationOutcome Classify(string? candidate, Uri backendUri)
    {
        ArgumentNullException.ThrowIfNull(backendUri);

        if (string.IsNullOrWhiteSpace(candidate) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? navigation))
        {
            return PaymentNavigationOutcome.None;
        }

        if (string.Equals(navigation.Scheme, "markuptv", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(navigation.UserInfo) ||
                navigation.AbsolutePath is not ("" or "/"))
            {
                return PaymentNavigationOutcome.None;
            }

            if (string.Equals(navigation.Host, "payment-success", StringComparison.OrdinalIgnoreCase))
                return PaymentNavigationOutcome.Success;

            if (string.Equals(navigation.Host, "payment-failed", StringComparison.OrdinalIgnoreCase))
                return PaymentNavigationOutcome.Failure;

            return PaymentNavigationOutcome.None;
        }

        if (!string.IsNullOrEmpty(navigation.UserInfo) ||
            !string.Equals(navigation.Scheme, backendUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(navigation.Host, backendUri.Host, StringComparison.OrdinalIgnoreCase) ||
            navigation.Port != backendUri.Port)
        {
            return PaymentNavigationOutcome.None;
        }

        return navigation.AbsolutePath.TrimEnd('/').ToLowerInvariant() switch
        {
            "/payment-success" or
            "/api/device/payment-callback" or
            "/api/donation/callback" => PaymentNavigationOutcome.Success,
            "/payment-failed" => PaymentNavigationOutcome.Failure,
            _ => PaymentNavigationOutcome.None
        };
    }
}
