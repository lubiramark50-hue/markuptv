using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MarkUptv.Services;
using Microsoft.Extensions.Logging;

namespace MarkUptv.Handlers;

/// <summary>
/// Rewrites every outgoing backend request to the endpoint that actually
/// answered the reachability probe (see <see cref="BackendEndpointProvider"/>).
///
/// The typed clients still declare a base address, so relative paths keep
/// working and the handler is a no-op whenever the resolved endpoint is the
/// same host - which is the normal case on the machine running the server.
/// It only matters when the developer's PC has moved to a different LAN
/// address than the one compiled into the app.
/// </summary>
public sealed class BackendRoutingHandler(
    BackendEndpointProvider endpoints,
    ILogger<BackendRoutingHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.RequestUri is { IsAbsoluteUri: true } requestUri)
            {
                Uri endpoint = await endpoints
                    .GetEndpointAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!string.Equals(
                        requestUri.Authority,
                        endpoint.Authority,
                        StringComparison.OrdinalIgnoreCase))
                {
                    request.RequestUri = new Uri(endpoint, requestUri.PathAndQuery);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never let endpoint discovery break a request: fall through and
            // let the client use the base address it was configured with.
            logger.LogWarning(
                exception,
                "Backend endpoint routing failed; using the configured base address.");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
