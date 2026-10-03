#if ANDROID

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Android.Content;
using Android.Runtime;
using CommunityToolkit.Maui.Core.Handlers;
using MarkUptv.Helpers;

namespace MarkUptv.Handlers;

public static partial class StreamPlayerHeaderHandler
{
    // The toolkit (MediaElement 4.x) plays through ExoPlayer 2.19
    // (Xam.Plugins.Android.ExoPlayer). Depending on the installed binding the
    // namespace is either Com.Google.Android.Exoplayer2 or Android.Media3 —
    // so the swap below is reflection-driven and degrades to a no-op if the
    // player or API can't be located, keeping playback safe either way.
    private static readonly string[] PlayerNamespaces =
    {
        "Com.Google.Android.Exoplayer2",
        "Android.Media3.ExoPlayer"
    };

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

        var player = FindPlayer(
            handler.PlatformView,
            t => PlayerNamespaces.Any(ns =>
                t.FullName?.StartsWith(ns, StringComparison.Ordinal) == true &&
                t.Name.Contains("ExoPlayer", StringComparison.Ordinal)));

        if (player is null)
        {
            return;
        }

        var ns = PlayerNamespaces.FirstOrDefault(n =>
            player.GetType().FullName?.StartsWith(n, StringComparison.Ordinal) == true);
        if (ns is null)
        {
            return;
        }

        try
        {
            var context = handler.MauiContext?.Context as Context;
            if (context is null)
            {
                return;
            }

            // ── 1. DefaultHttpDataSource.Factory with our headers ──────────
            var dataSourceFactoryType = Type.GetType(
                $"{ns}.Upstream.DefaultHttpDataSource+Factory, Mono.Android",
                throwOnError: false);
            if (dataSourceFactoryType is null)
            {
                return;
            }

            var dataSourceFactory = Activator.CreateInstance(dataSourceFactoryType);
            if (dataSourceFactory is null)
            {
                return;
            }

            var requestHeaders = new Dictionary<string, string>();
            if (userAgent is not null)
            {
                requestHeaders["User-Agent"] = userAgent;
            }

            if (referer is not null)
            {
                requestHeaders["Referer"] = referer;
            }

            foreach (var (key, value) in headers)
            {
                requestHeaders[key] = value;
            }

            // setDefaultRequestProperties(Map<String,String>)
            var setProps = dataSourceFactoryType.GetMethod(
                "SetDefaultRequestProperties",
                BindingFlags.Instance | BindingFlags.Public);
            setProps?.Invoke(dataSourceFactory, new object[] { new JavaDictionary<string, string>(requestHeaders) });

            // ── 2. DefaultMediaSourceFactory(context, dataSource) ──────────
            var mediaSourceFactoryType = Type.GetType(
                $"{ns}.Source.DefaultMediaSourceFactory, Mono.Android",
                throwOnError: false);
            if (mediaSourceFactoryType is null)
            {
                return;
            }

            var mediaSourceFactory = Activator.CreateInstance(
                mediaSourceFactoryType,
                context,
                dataSourceFactory);

            // ── 3. Swap the factory on the live player ─────────────────────
            // ExoPlayer 2.19 exposes setMediaSourceFactory on the impl; older
            // bindings need the builder-time API. Try the runtime setter first.
            var playerType = player.GetType();
            var setFactory = playerType.GetMethod(
                "SetMediaSourceFactory",
                BindingFlags.Instance | BindingFlags.Public) ??
                playerType.GetMethod(
                    "SetMediaSourceFactory",
                    BindingFlags.Instance | BindingFlags.NonPublic);

            if (setFactory is null)
            {
                return;
            }

            setFactory.Invoke(player, new[] { mediaSourceFactory });

            // ── 4. Force the current item through the new factory ──────────
            // Re-set the current MediaItem so the HLS/DASH source is rebuilt
            // on top of our data source, then restart playback.
            var currentItem = playerType.GetProperty(
                "CurrentMediaItem",
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)?.GetValue(player);

            if (currentItem is null)
            {
                return;
            }

            var setItem = playerType.GetMethod(
                "SetMediaItem",
                BindingFlags.Instance | BindingFlags.Public) ??
                playerType.GetMethod(
                    "SetMediaItem",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            setItem?.Invoke(player, new[] { currentItem });

            playerType.GetMethod("Prepare", BindingFlags.Instance | BindingFlags.Public)?.Invoke(player, null);

            if (handler.VirtualView.ShouldAutoPlay)
            {
                playerType.GetMethod("Play", BindingFlags.Instance | BindingFlags.Public)?.Invoke(player, null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[StreamPlayerHeaderHandler.Android] {ex}");
        }
    }
}

#endif
