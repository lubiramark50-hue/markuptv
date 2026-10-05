using System.Net.Http.Json;
using System.Text.Json;
using MarkUptv.Models;

namespace MarkUptv.Services;

public class DonationService
{
    private readonly HttpClient _httpClient;

    public DonationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> InitiateDonationAsync(DonationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                amount = decimal.Parse(request.Amount),
                currency = "UGX",
                email = request.Email,
                phoneNumber = request.PhoneNumber,
                firstName = request.FirstName,
                lastName = request.LastName
            };

            var response = await _httpClient.PostAsJsonAsync("initiate", payload, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("redirectUrl", out var redirect) ? redirect.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}