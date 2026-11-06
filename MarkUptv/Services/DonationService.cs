using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MarkUptv.Models;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Services;

public class DonationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DonationService> _logger;

    public DonationService(HttpClient httpClient, ILogger<DonationService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> InitiateDonationAsync(DonationRequest request)
    {
        try
        {
            var payload = new
            {
                amount = decimal.Parse(request.Amount, NumberStyles.Number, CultureInfo.CurrentCulture),
                currency = "UGX",
                email = request.Email,
                phoneNumber = request.PhoneNumber,
                firstName = request.FirstName,
                lastName = request.LastName
            };

            // The typed client is configured with the application's backend
            // at /api/donation/. Do not bypass it through a temporary tunnel.
            using var response = await _httpClient.PostAsJsonAsync("initiate", payload);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("redirectUrl", out var redirectElement))
                return null;

            string? redirectUrl = redirectElement.GetString();
            return Uri.TryCreate(redirectUrl, UriKind.Absolute, out Uri? redirect) &&
                   redirect.Scheme == Uri.UriSchemeHttps
                ? redirectUrl
                : null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Donation initiation could not reach the payment backend.");
            return null;
        }
    }
}