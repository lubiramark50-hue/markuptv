using System.Net.Http.Json;
using System.Text.Json;
using MarkUptv.Models;

namespace MarkUptv.Services;

public class DonationService
{
    private readonly HttpClient _httpClient;
    private static string BaseUrl => "https://4b2n3k16-7099.usw2.devtunnels.ms/";

    public DonationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> InitiateDonationAsync(DonationRequest request)
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

            var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/initiate", payload);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("redirectUrl").GetString();
        }
        catch
        {
            return null;
        }
    }
}