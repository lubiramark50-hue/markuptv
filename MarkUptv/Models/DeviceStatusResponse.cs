using System;
using System.Collections.Generic;
using System.Text;

namespace MarkUptv.Models
{
    public partial class DeviceStatusResponse
    {
        public bool CanWatch { get; set; }
        public int DaysRemaining { get; set; }
        public bool IsTrial { get; set; }
        public string? ExpiryDate { get; set; }

        /// <summary>
        /// One-time TV unlock price published by the server. Never hard-code an
        /// amount in the UI - the price the user is shown must be the price the
        /// gateway charges.
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>ISO currency code for <see cref="Price"/>.</summary>
        public string? Currency { get; set; }

        /// <summary>
        /// Display string for the unlock price, e.g. "5,000 UGX". Falls back to
        /// the known default when talking to a server that predates the field.
        /// </summary>
        public string PriceText =>
            Price > 0
                ? $"{Price:N0} {(!string.IsNullOrWhiteSpace(Currency) ? Currency : "UGX")}"
                : "5,000 UGX";

        // ── Daily pass (films + 18+), priced by the server ──────────────

        /// <summary>
        /// True while the device holds an unexpired daily pass. The server also
        /// enforces this on the film and adult endpoints, so the flag is a
        /// mirror of the truth, not the gate itself.
        /// </summary>
        public bool PassActive { get; set; }

        /// <summary>ISO instant the pass lapses; null when there is none.</summary>
        public string? PassExpiresUtc { get; set; }

        public decimal PassPrice { get; set; }

        public string? PassCurrency { get; set; }

        public int PassHours { get; set; } = 24;

        /// <summary>"1,000 UGX" — the exact figure the gateway will charge.</summary>
        public string PassPriceText => PassPrice > 0
            ? $"{PassPrice:N0} {(!string.IsNullOrWhiteSpace(PassCurrency) ? PassCurrency : "UGX")}"
            : "1,000 UGX";

        /// <summary>"24 hours" badge on the offer card.</summary>
        public string PassDurationText => PassHours switch
        {
            24 => "24 hours",
            1 => "1 hour",
            < 24 => $"{PassHours} hours",
            _ => $"{PassHours / 24} day{(PassHours / 24 == 1 ? string.Empty : "s")}"
        };

        /// <summary>
        /// Human countdown to the pass expiry ("Pass ends in 4h 12m"). Computed
        /// from the server's UTC instant; the emulator clock can trail the
        /// server, so a lapsed pass reads as ended rather than negative.
        /// </summary>
        public string PassExpiryText
        {
            get
            {
                if (!PassActive)
                {
                    return string.Empty;
                }

                if (string.IsNullOrWhiteSpace(PassExpiresUtc) ||
                    !DateTimeOffset.TryParse(
                        PassExpiresUtc,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out DateTimeOffset expiry))
                {
                    // Active with no expiry instant: the emulator freebie.
                    // Never repeat the strip's title — say why instead.
                    return "This device streams free — nothing to renew";
                }

                TimeSpan left = expiry - DateTimeOffset.UtcNow;

                return left <= TimeSpan.Zero
                    ? "Pass just ended"
                    : left.TotalHours >= 1
                        ? $"Pass ends in {(int)left.TotalHours}h {left.Minutes:00}m"
                        : $"Pass ends in {Math.Max(1, left.Minutes)}m";
            }
        }
    }
}
