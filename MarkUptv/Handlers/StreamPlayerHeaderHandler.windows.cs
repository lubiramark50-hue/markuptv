#if WINDOWS

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CommunityToolkit.Maui.Core.Handlers;
using MarkUptv.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace MarkUptv.Handlers;

public static partial class StreamPlayerHeaderHandler
{
    // AdaptiveMediaSource requires the HttpClient it was created with to stay
    // alive for as long as the source streams — keep a reference per URL.
    private static readonly ConcurrentDictionary<string, Windows.Web.Http.HttpClient> KeepAlive = new();

    static partial void ApplyPlatform(MediaElementHandler handler)
    {
        var (url, referer, userAgent, headers) = StreamHeaderProvider.Snapshot();
        if (url is null && referer is null && userAgent is null && headers.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (handler.PlatformView is not DependencyObject root)
        {
            return;
        }

        var mediaPlayerElement = FindMediaPlayerElement(root);
        if (mediaPlayerElement is null)
        {
            return;
        }

        // AdaptiveMediaSource is created with this HttpClient, so the headers
        // are attached to the manifest request AND every HLS segment fetch.
        var httpClient = new Windows.Web.Http.HttpClient();
        var defaultHeaders = httpClient.DefaultRequestHeaders;

        if (referer is not null)
        {
            defaultHeaders.Referer = new Uri(referer);
        }

        if (userAgent is not null)
        {
            defaultHeaders.UserAgent.ParseAdd(userAgent);
        }

        foreach (var (key, value) in headers)
        {
            defaultHeaders.TryAppendWithoutValidation(key, value);
        }

        KeepAlive[url] = httpClient;

        _ = CreateAndPlayAsync(mediaPlayerElement, url, httpClient);
    }

    private static async System.Threading.Tasks.Task CreateAndPlayAsync(
        MediaPlayerElement element,
        string url,
        Windows.Web.Http.HttpClient httpClient)
    {
        try
        {
            var adaptive = await AdaptiveMediaSource.CreateFromUriAsync(
                new Uri(url), httpClient);

            if (adaptive.Status == AdaptiveMediaSourceCreationStatus.Success)
            {
                var mediaSource =
                    Windows.Media.Core.MediaSource
                        .CreateFromAdaptiveMediaSource(adaptive.MediaSource);

                element.Source =
                    new MediaPlaybackItem(mediaSource);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[StreamPlayerHeaderHandler.Windows] {ex}");
        }
    }

    private static MediaPlayerElement? FindMediaPlayerElement(DependencyObject root)
    {
        if (root is MediaPlayerElement direct)
        {
            return direct;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is MediaPlayerElement match)
            {
                return match;
            }

            var nested = FindMediaPlayerElement(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}

#endif
