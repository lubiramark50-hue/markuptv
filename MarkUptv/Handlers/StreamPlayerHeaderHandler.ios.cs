#if IOS || MACCATALYST

using System;
using System.Collections.Generic;
using AVFoundation;
using CommunityToolkit.Maui.Core.Handlers;
using Foundation;
using MarkUptv.Helpers;

namespace MarkUptv.Handlers;

public static partial class StreamPlayerHeaderHandler
{
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

        // The toolkit's AVPlayer is not exposed through documented APIs on the
        // platform view, so discover it by walking the internal graph.
        var player = FindPlayer(
            handler.PlatformView,
            t => typeof(AVPlayer).IsAssignableFrom(t)) as AVPlayer;

        if (player is null)
        {
            return;
        }

        var headerFields = new NSMutableDictionary<NSString, NSString>();
        if (userAgent is not null)
        {
            headerFields["User-Agent"] = new NSString(userAgent);
        }

        if (referer is not null)
        {
            headerFields["Referer"] = new NSString(referer);
        }

        foreach (var (key, value) in headers)
        {
            headerFields[key] = new NSString(value);
        }

        // AVURLAssetHTTPHeaderFieldsKey — AVFoundation attaches these headers
        // to the asset's HTTP requests (manifest fetch + HLS key requests).
        // Note: segment fetches can still drop headers on some iOS versions;
        // the local reverse-proxy pattern is the guaranteed fallback.
        var optionsDict = NSDictionary.FromObjectsAndKeys(
            new NSObject[] { (NSString)"AVURLAssetHTTPHeaderFieldsKey" },
            new NSObject[] { headerFields });

        var assetOptions = new AVUrlAssetOptions(optionsDict);
        var asset = new AVUrlAsset(NSUrl.FromString(url), assetOptions);

        // Replace the toolkit's item with one built from our header-carrying
        // asset, then resume playback.
        player.ReplaceCurrentItemWithPlayerItem(AVPlayerItem.FromAsset(asset));

        if (handler.VirtualView.ShouldAutoPlay)
        {
            player.Play();
        }
    }
}

#endif
