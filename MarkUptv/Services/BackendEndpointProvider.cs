using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace MarkUptv.Services;

/// <summary>
/// Decides which host the app talks to for the MarkUpTV backend.
///
/// The backend normally runs on the developer's PC on a fixed port, so the app
/// needs its LAN address. That address was a compile-time constant, which meant
/// every DHCP lease change silently broke the app: channel rows still rendered
/// from the on-device cache while the football board - which has no offline
/// cache - came up empty, looking like "no live football today" rather than a
/// connection problem.
///
/// Instead, the endpoint is probed once per launch from a candidate list and
/// the winner is remembered in <see cref="Preferences"/>. Candidates are tried
/// in order:
///   1. the endpoint saved from the last successful launch,
///   2. the platform default (localhost / 10.0.2.2 / the configured LAN IP),
///   3. the same port on this machine's mDNS name and plain host name.
/// A manual override can always be written to the
/// <see cref="PreferenceKey"/> preference, which wins over everything.
///
/// Nothing here throws: if no candidate answers, the platform default is used
/// and the request fails exactly as it would have before.
/// </summary>
public sealed class BackendEndpointProvider
{
    /// <summary>Preference holding a manual or last-known-good backend URL.</summary>
    public const string PreferenceKey = "markuptv.backend_url";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2.5);

    private readonly ILogger<BackendEndpointProvider> _logger;
    private readonly Uri _defaultEndpoint;
    private readonly string _hostName;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Uri? _resolved;

    public BackendEndpointProvider(
        Uri defaultEndpoint,
        string hostName,
        ILogger<BackendEndpointProvider> logger)
    {
        _defaultEndpoint = defaultEndpoint
            ?? throw new ArgumentNullException(nameof(defaultEndpoint));

        _hostName = string.IsNullOrWhiteSpace(hostName)
            ? string.Empty
            : hostName.Trim();

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The address the app would use with no probing at all.</summary>
    public Uri DefaultEndpoint => _defaultEndpoint;

    /// <summary>
    /// The endpoint to send a request to. Resolved once and cached for the
    /// lifetime of the app.
    /// </summary>
    public async Task<Uri> GetEndpointAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolved is null)
            {
                _resolved = await ProbeAsync(cancellationToken).ConfigureAwait(false)
                            ?? _defaultEndpoint;
            }

            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Uri?> ProbeAsync(CancellationToken cancellationToken)
    {
        foreach (Uri candidate in BuildCandidates())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (await IsReachableAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "Backend reachable at {Endpoint}; using it for this session.",
                    candidate);

                Remember(candidate);

                return candidate;
            }
        }

        _logger.LogWarning(
            "No backend candidate answered; falling back to {Endpoint}.",
            _defaultEndpoint);

        return null;
    }

    private IEnumerable<Uri> BuildCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Manual override / last known good.
        if (TryBuild(Preferences.Get(PreferenceKey, string.Empty), out Uri? saved) &&
            seen.Add(saved!.AbsoluteUri))
        {
            yield return saved!;
        }

        // 2. The platform default for this build (localhost, 10.0.2.2 or LAN).
        if (seen.Add(_defaultEndpoint.AbsoluteUri))
        {
            yield return _defaultEndpoint;
        }

        // 3+4. The same port on the host's mDNS and plain names, which survive
        // a DHCP lease change on most home networks.
        if (_hostName.Length > 0)
        {
            foreach (string host in new[] { $"{_hostName}.local", _hostName })
            {
                var candidate = new UriBuilder(_defaultEndpoint)
                {
                    Host = host
                }.Uri;

                if (seen.Add(candidate.AbsoluteUri))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static bool TryBuild(string? value, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var address = value.Trim();

        if (!address.EndsWith("/", StringComparison.Ordinal))
        {
            address += "/";
        }

        return Uri.TryCreate(address, UriKind.Absolute, out uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private async Task<bool> IsReachableAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = ProbeTimeout
            };

            using var response = await client
                .GetAsync(new Uri(endpoint, "health"), cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Backend probe failed for {Endpoint}.",
                endpoint);

            return false;
        }
    }

    private void Remember(Uri endpoint)
    {
        try
        {
            if (!string.Equals(
                    endpoint.AbsoluteUri,
                    Preferences.Get(PreferenceKey, string.Empty),
                    StringComparison.OrdinalIgnoreCase))
            {
                Preferences.Set(PreferenceKey, endpoint.AbsoluteUri);
            }
        }
        catch (Exception exception)
        {
            // Preferences can be unavailable very early in startup; the app
            // still works, it just re-probes next launch.
            _logger.LogDebug(exception, "Could not persist the backend endpoint.");
        }
    }
}
