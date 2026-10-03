using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MarkUptv.Models;

namespace MarkUptv.Services;

/// <summary>
/// Configuration options class for the Payment Service.
/// </summary>
public class PaymentSettings
{
    // 🛡️ SOLIDIFIED: Removed the devtunnel hardcode. It should default to empty so it forces configuration in MauiProgram.cs
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Buyer details for a TV unlock. Note there is deliberately no Amount or
/// Currency here: pricing is fixed server-side by the device unlock endpoint,
/// so a tampered client can never choose what it pays.
/// </summary>
public class PaymentDetails
{
    /// <summary>Product code for the daily films + 18+ pass.</summary>
    public const string DayPassProduct = "day_pass";

    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FirstName { get; set; } = "Premium";
    public string LastName { get; set; } = "User";

    /// <summary>
    /// What is being bought: <see cref="DayPassProduct"/> for the daily pass,
    /// or null for the legacy one-time TV unlock. The server owns the price of
    /// each product, so this picks a product — never an amount.
    /// </summary>
    public string? Product { get; set; }
}

/// <summary>
/// Outcome of asking the backend for a checkout page. Carries the redirect URL
/// on success, or the reason the gateway refused so the UI can say something
/// more useful than "something went wrong".
/// </summary>
public sealed record PaymentInitiationResult(bool Success, string? RedirectUrl, string? Error)
{
    public static PaymentInitiationResult Ok(string redirectUrl) => new(true, redirectUrl, null);

    public static PaymentInitiationResult Fail(string error) => new(false, null, error);
}

public partial class PaymentService
{
    private readonly HttpClient _httpClient;
    private readonly DeviceService _deviceService;
    private readonly StatusCacheService _cacheService;
    private readonly ILogger<PaymentService> _logger;
    private readonly PaymentSettings _settings;

    // Synchronization primitives to maximize reaction speed and completely block Cache Stampedes
    private static readonly SemaphoreSlim _statusSemaphore = new(1, 1);

    private const string StatusCacheKey = "local_device_status_lease";
    private const string TimestampCacheKey = "local_device_status_timestamp";
    private static readonly TimeSpan OfflineGracePeriod = TimeSpan.FromHours(4);

    // 🛡️ SOLIDIFIED: Made options read-only to prevent accidental modification at runtime
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PaymentService(
        HttpClient httpClient,
        DeviceService deviceService,
        StatusCacheService cacheService,
        ILogger<PaymentService> logger,
        IOptions<PaymentSettings> settings)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        // 🛡️ SOLIDIFIED: Ensure BaseUrl is configured to prevent runtime routing crashes
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            _logger.LogWarning("PaymentSettings.BaseUrl is empty! Ensure it is configured in MauiProgram.cs");
        }
    }

    // 🛡️ SOLIDIFIED: Safer URI combination prevents "//" double-slash routing bugs
    private string CleanseUrl(string segment)
    {
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var cleanSegment = segment.TrimStart('/');
        return $"{baseUrl}/{cleanSegment}";
    }

    // ========================= REGISTRATION =========================
    public async Task<bool> RegisterDeviceAsync(CancellationToken ct = default)
    {
        const int maxRetryAttempts = 3;
        var delayBetweenRetries = TimeSpan.FromSeconds(1.5);

        var deviceId = _deviceService.GetDeviceId();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            _logger.LogError("RegisterDeviceAsync Aborted: Hardware tracking identifier is null or empty.");
            return false;
        }

        string requestUrl = CleanseUrl("api/device/register");
        var payload = new
        {
            deviceId = deviceId,
            deviceType = DeviceInfo.Platform.ToString(),
            osVersion = DeviceInfo.VersionString,
            // Emulators / virtual devices get free access forever — real
            // hardware pays after the free trial ends.
            isEmulator = DeviceInfo.DeviceType == DeviceType.Virtual
        };

        for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation("RegisterDeviceAsync [Attempt {Attempt}/{MaxAttempts}]: Transmitting registration packet...", attempt, maxRetryAttempts);

                // 🛡️ SOLIDIFIED: Safe using declaration ensures HTTP connections don't leak
                using var response = await _httpClient.PostAsJsonAsync(requestUrl, payload, ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("RegisterDeviceAsync Success: Handshake completed securely.");
                    return true;
                }

                var errContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("RegisterDeviceAsync Server Rejection [Attempt {Attempt}]: Status {StatusCode} - Details: {Details}", attempt, response.StatusCode, errContent);

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    // 🛡️ SOLIDIFIED: Break out of loop immediately on 4xx errors (client error, retrying won't fix it)
                    return false;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                _logger.LogWarning(ex, "RegisterDeviceAsync Network Interruption [Attempt {Attempt}]", attempt);
                if (attempt == maxRetryAttempts) break;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "RegisterDeviceAsync Critical Structural Exception.");
                return false;
            }

            if (attempt < maxRetryAttempts)
            {
                // 🛡️ SOLIDIFIED: Added Math.Max safety to guarantee jitter doesn't cause a negative delay crash
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(-250, 250));
                var finalDelay = delayBetweenRetries + jitter;
                var safeDelay = finalDelay > TimeSpan.Zero ? finalDelay : TimeSpan.FromSeconds(1);

                _logger.LogInformation("RegisterDeviceAsync: Waiting {TotalSeconds}s before next fallback...", safeDelay.TotalSeconds);
                await Task.Delay(safeDelay, ct);
                delayBetweenRetries *= 2; // Exponential backoff
            }
        }

        _logger.LogError("RegisterDeviceAsync Fatal: Exhausted all transport retry limits.");
        return false;
    }

    // ========================= STATUS (with advanced caching) =========================
    public async Task<DeviceStatusResponse?> GetStatusAsync(bool useCache = true, CancellationToken ct = default)
    {
        if (useCache)
        {
            var cached = _cacheService.GetCachedStatus();
            if (cached != null) return cached;
        }

        await _statusSemaphore.WaitAsync(ct);
        try
        {
            if (useCache)
            {
                var cached = _cacheService.GetCachedStatus();
                if (cached != null) return cached;
            }

            var deviceId = _deviceService.GetDeviceId();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning("GetStatusAsync Aborted: Physical device tracking signature is empty.");
                return await LoadOfflineCacheFallbackAsync();
            }

            string targetUrl = CleanseUrl($"api/device/status?deviceId={Uri.EscapeDataString(deviceId)}");
            _logger.LogInformation("GetStatusAsync: Polling synchronization gate -> {TargetUrl}", targetUrl);

            // 🛡️ SOLIDIFIED: Handled the HTTP response correctly inside the retry wrapper to prevent Null Ref Exceptions
            using var response = await FetchStatusWithRetryAsync(targetUrl, ct);

            if (response is { IsSuccessStatusCode: true })
            {
                var serverStatus = await response.Content.ReadFromJsonAsync<DeviceStatusResponse>(JsonOptions, ct);
                if (serverStatus != null)
                {
                    _cacheService.SetCachedStatus(serverStatus);

                    try
                    {
                        var serializedData = JsonSerializer.Serialize(serverStatus, JsonOptions);
                        await SecureStorage.Default.SetAsync(StatusCacheKey, serializedData);
                        await SecureStorage.Default.SetAsync(TimestampCacheKey, DateTime.UtcNow.Ticks.ToString());
                    }
                    catch (Exception cacheEx)
                    {
                        _logger.LogError(cacheEx, "GetStatusAsync Cryptographic Storage Write Error");
                    }

                    return serverStatus;
                }
            }

            _logger.LogWarning("GetStatusAsync Endpoint failed. Deploying hardware encrypted secure cache fallback.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical breakdown inside GetStatusAsync pipeline loop.");
        }
        finally
        {
            _statusSemaphore.Release();
        }

        return await LoadOfflineCacheFallbackAsync();
    }

    private async Task<HttpResponseMessage?> FetchStatusWithRetryAsync(string url, CancellationToken ct)
    {
        for (int i = 0; i < 2; i++)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                // 🛡️ SOLIDIFIED: If not successful, dispose it immediately so memory doesn't leak during the retry loop
                response.Dispose();
            }
            catch (Exception ex) when (i == 0)
            {
                _logger.LogWarning(ex, "Transient error fetching device status. Retrying immediately...");
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            }
        }
        return null;
    }

    public async Task<DeviceStatusResponse?> GetFreshStatusAsync(CancellationToken ct = default)
    {
        return await GetStatusAsync(useCache: false, ct);
    }

    public DeviceStatusResponse? GetCachedStatusOnly()
    {
        return _cacheService.GetCachedStatus();
    }

    private async Task<DeviceStatusResponse?> LoadOfflineCacheFallbackAsync()
    {
        try
        {
            var cachedJson = await SecureStorage.Default.GetAsync(StatusCacheKey);
            var cachedTicksStr = await SecureStorage.Default.GetAsync(TimestampCacheKey);

            if (!string.IsNullOrEmpty(cachedJson) && long.TryParse(cachedTicksStr, out long cachedTicks))
            {
                var cacheTimestamp = new DateTime(cachedTicks, DateTimeKind.Utc);

                if (DateTime.UtcNow < cacheTimestamp)
                {
                    _logger.LogCritical("Security Breach Attempt Detected: System clock set backward.");
                    return null;
                }

                if (DateTime.UtcNow - cacheTimestamp <= OfflineGracePeriod)
                {
                    var localStatus = JsonSerializer.Deserialize<DeviceStatusResponse>(cachedJson, JsonOptions);
                    if (localStatus != null)
                    {
                        _logger.LogInformation("GetStatusAsync Safety Net Triggered: Lease updated on {Time}", cacheTimestamp.ToLocalTime());
                        return localStatus;
                    }
                }
                else
                {
                    _logger.LogWarning("GetStatusAsync Safety Net Failure: Cache expired. Limit exceeded.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetStatusAsync hardware-encrypted storage cache recovery crash.");
        }
        return null;
    }

    // ========================= PAYMENT INITIATION =========================
    public async Task<PaymentInitiationResult> InitiatePaymentAsync(PaymentDetails payment, CancellationToken ct = default)
    {
        // The TV unlock is a device-level purchase handled by the backend
        // (api/device/initiate-payment -> Pesapal checkout -> permanent unlock).
        // Amount and currency are fixed server-side, so the client only sends
        // who is buying - never how much.
        string requestUrl = CleanseUrl("api/device/initiate-payment");
        _logger.LogInformation("InitiatePaymentAsync: Target Endpoint cleared -> {RequestUrl}", requestUrl);

        if (payment == null || string.IsNullOrWhiteSpace(payment.Email))
        {
            _logger.LogWarning("Payment Initialization Aborted: Email missing.");
            return PaymentInitiationResult.Fail("Please enter a valid email address.");
        }

        try
        {
            var deviceId = _deviceService.GetDeviceId();
            var payload = new
            {
                DeviceId = deviceId,
                Email = payment.Email.Trim(),
                PhoneNumber = payment.PhoneNumber?.Trim() ?? string.Empty,
                Product = payment.Product
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = JsonContent.Create(payload, null, JsonOptions)
            };

            string uniqueIdempotencyToken = Guid.NewGuid().ToString();
            requestMessage.Headers.Add("X-Idempotency-Key", uniqueIdempotencyToken);

            // 🛡️ SOLIDIFIED: Wrapped the final response in 'using' so it releases the server connection immediately
            using var response = await _httpClient.SendAsync(requestMessage, ct);

            var responseContent = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // The backend explains itself ("Pesapal returned an empty token
                // payload." etc). Showing that beats a generic dead-end message.
                _logger.LogError("Server Rejected Payment Hook ({StatusCode}): {RawError}", response.StatusCode, responseContent);
                return PaymentInitiationResult.Fail(
                    ToUserFacingMessage(ReadServerMessage(responseContent), response.StatusCode));
            }

            try
            {
                using var jsonDoc = JsonDocument.Parse(responseContent);

                if (jsonDoc.RootElement.TryGetProperty("redirectUrl", out var urlElement))
                {
                    string? targetRedirectUrl = urlElement.GetString();
                    if (!string.IsNullOrWhiteSpace(targetRedirectUrl))
                    {
                        _logger.LogInformation("Payment Initialization Complete.");
                        return PaymentInitiationResult.Ok(targetRedirectUrl);
                    }
                }

                _logger.LogError("Payment Initialization Failed: 'redirectUrl' missing in server JSON.");
                return PaymentInitiationResult.Fail(
                    ToUserFacingMessage(ReadServerMessage(responseContent), response.StatusCode));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Payment System Incompatible JSON structure");
                return PaymentInitiationResult.Fail("Payment gateway returned an unreadable response.");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Payment System Network Interruption");
            return PaymentInitiationResult.Fail("Cannot reach the payment service. Check your connection and try again.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // 🛡️ SOLIDIFIED: Differentiates between a forced app-shutdown cancellation vs a network timeout
            _logger.LogError(ex, "Payment System Operation Connection Timed Out");
            return PaymentInitiationResult.Fail("The payment service timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical system breakdown inside InitiatePaymentAsync");
            return PaymentInitiationResult.Fail("Unexpected error while starting the payment.");
        }
    }

    /// <summary>
    /// Pulls the human-readable "message"/"Message" field out of an error body
    /// so the paywall can explain what the gateway actually said.
    /// </summary>
    private static string? ReadServerMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in new[] { "message", "Message", "error", "Error" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    var message = value.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                        return message.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON - the caller falls back to a generic message.
        }

        return null;
    }

    /// <summary>
    /// Translates gateway/plumbing faults into copy a paying customer can act
    /// on. "Pesapal returned an empty token payload" is a useful log line but a
    /// terrible thing to show someone who is trying to buy the app.
    /// </summary>
    private static string ToUserFacingMessage(string? serverMessage, System.Net.HttpStatusCode status)
    {
        if (string.IsNullOrWhiteSpace(serverMessage))
            return $"Payment gateway unavailable ({(int)status}). Please try again shortly.";

        var message = serverMessage.Trim();

        var isGatewayFault =
            status == System.Net.HttpStatusCode.BadGateway ||
            status == System.Net.HttpStatusCode.ServiceUnavailable ||
            status == System.Net.HttpStatusCode.GatewayTimeout ||
            message.Contains("token payload", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("auth failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Pesapal", StringComparison.OrdinalIgnoreCase);

        return isGatewayFault
            ? "Payment is temporarily unavailable. Please try again in a few minutes."
            : message;
    }
}