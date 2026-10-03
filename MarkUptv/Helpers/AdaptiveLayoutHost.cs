using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MarkUptv.Services;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MarkUptv.Helpers;

/// <summary>
/// Applies the app-wide adaptive layout rules to a page when it is shown, and
/// again whenever the viewport changes class (rotation, desktop resize, TV).
///
/// Two things used to make pages clip instead of adapt:
///   1. most pages had no scroll host at all, so anything taller than the
///      viewport was simply cut off, and
///   2. content stretched edge to edge on tablets and TV, which is unreadable.
///
/// Running centrally from the Shell's navigation hook means all ~55 pages get
/// the behaviour without each one re-implementing it. Per page:
///   - content that cannot scroll on its own is wrapped in a vertical
///     <see cref="ScrollView"/>, so nothing is ever unreachable;
///   - anything that already scrolls (CollectionView / ListView / WebView /
///     media) is left alone, so scrollers are never nested;
///   - on tablet/desktop/TV widths the content is clamped to a readable column
///     and centred, unless the page paints its own background (it would
///     letterbox).
/// </summary>
public static class AdaptiveLayoutHost
{
    /// <summary>
    /// Element type names that already own their scrolling. Wrapping any of
    /// these in a ScrollView produces nested scrollers, which is worse than the
    /// clipping we are fixing.
    /// </summary>
    private static readonly HashSet<string> ScrollOwningTypes = new(StringComparer.Ordinal)
    {
        "ScrollView",
        "CollectionView",
        "ListView",
        "CarouselView",
        "RefreshView",
        "WebView",
        "MediaElement",
        "HybridWebView"
    };

    // The page's untouched content, kept so a re-layout can start from the
    // original tree instead of unwrapping the previous pass by hand.
    private static readonly ConditionalWeakTable<Page, View> OriginalContent = new();

    // The breakpoint the page was last laid out for.
    private static readonly ConditionalWeakTable<Page, object> AppliedFor = new();

    // Pages whose Loaded hook has already been installed.
    private static readonly ConditionalWeakTable<Page, object> TypeScaleHooked = new();

    /// <summary>
    /// Re-applies the rules to whatever page is on screen. Called on navigation
    /// and whenever the viewport changes.
    /// </summary>
    public static void ApplyToCurrentPage()
    {
        try
        {
            Page? page = Shell.Current?.CurrentPage;

            if (page is null &&
                Application.Current is { Windows.Count: > 0 } app)
            {
                page = app.Windows[0].Page;
            }

            Apply(page);
        }
        catch
        {
            // Layout must never crash navigation.
        }
    }

    /// <summary>
    /// Applies the adaptive rules to one page. Safe to call repeatedly: it only
    /// rebuilds when the viewport has actually changed class.
    /// </summary>
    public static void Apply(Page? page)
    {
        if (page is not ContentPage contentPage || contentPage.Content is null)
        {
            return;
        }

        ScreenBreakpoint breakpoint = AdaptiveMetrics.Current;

        if (AppliedFor.TryGetValue(page, out var applied) &&
            applied is ScreenBreakpoint previous &&
            previous == breakpoint &&
            ReferenceEquals(contentPage.Content, LastHost.For(page)))
        {
            return;
        }

        View root;

        if (OriginalContent.TryGetValue(page, out var saved))
        {
            root = saved;
            DetachHost(contentPage.Content, root);
        }
        else
        {
            root = contentPage.Content!;
            OriginalContent.Add(page, root);
        }

        try
        {
            View host = root;

            // 1. Guarantee a scroll host so tall content is reachable.
            if (!OwnsScrolling(root))
            {
                host = new ScrollView
                {
                    Content = root,
                    VerticalOptions = LayoutOptions.Fill,
                    HorizontalOptions = LayoutOptions.Fill
                };
            }

            // 2. On wide viewports, keep content in a readable centred column.
            double maxWidth = AdaptiveMetrics.ContentMaxWidth;

            if (!double.IsPositiveInfinity(maxWidth) && !PaintsBackground(host))
            {
                host = new ContentView
                {
                    Content = host,
                    MaximumWidthRequest = maxWidth,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Fill
                };
            }

            contentPage.Content = host;
            LastHost.Set(page, host);

            // MAUI/WinUI does not reliably re-measure existing content when the
            // window changes size, so a page resized from 900px to 400px kept
            // its 460px-wide column and simply clipped. Nudging the measure
            // pass for the whole tree makes the new width take effect.
            InvalidateMeasureTree(host);

            // Typography scales with the viewport too. The tree is only
            // complete once the page has loaded, so hook that as well as
            // applying immediately (whichever finds elements first wins, and
            // the scaler is idempotent).
            if (!TypeScaleHooked.TryGetValue(page, out _))
            {
                TypeScaleHooked.Add(page, string.Empty);
                contentPage.Loaded += (_, _) => AdaptiveTypeScaler.Apply(contentPage);
            }

            AdaptiveTypeScaler.Apply(contentPage);

            AppliedFor.Remove(page);
            AppliedFor.Add(page, breakpoint);
        }
        catch
        {
            // If the wrap fails for any reason the page keeps its own content -
            // degraded, never broken.
            try
            {
                contentPage.Content = root;
            }
            catch
            {
                // Nothing further we can safely do.
            }
        }
    }

    /// <summary>
    /// Forces a fresh measure pass over a page's tree. Only called when the
    /// viewport has actually changed class, so the walk stays off the hot path.
    /// </summary>
    private static void InvalidateMeasureTree(Element root)
    {
        try
        {
            if (root is VisualElement visual)
            {
                visual.InvalidateMeasure();
            }

            foreach (var element in Descendants(root))
            {
                if (element is VisualElement child)
                {
                    child.InvalidateMeasure();
                }
            }
        }
        catch
        {
            // A measure nudge is best-effort.
        }
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        if (root is not IVisualTreeElement treeElement)
        {
            yield break;
        }

        foreach (var descendant in treeElement.GetVisualTreeDescendants())
        {
            if (descendant is Element element)
            {
                yield return element;
            }
        }
    }

    /// <summary>
    /// Unwraps any host this class added so the original content can be
    /// re-parented into a fresh one.
    /// </summary>
    private static void DetachHost(View? host, View root)
    {
        while (host is not null && !ReferenceEquals(host, root))
        {
            if (host is ContentView contentView && contentView.Content is not null)
            {
                View next = contentView.Content;
                contentView.Content = null;
                host = next;
                continue;
            }

            if (host is ScrollView scrollView && scrollView.Content is not null)
            {
                View next = scrollView.Content;
                scrollView.Content = null;
                host = next;
                continue;
            }

            return;
        }
    }

    private static bool OwnsScrolling(Element element)
    {
        if (ScrollOwningTypes.Contains(element.GetType().Name))
        {
            return true;
        }

        try
        {
            if (element is not IVisualTreeElement treeElement)
            {
                return false;
            }

            foreach (var descendant in treeElement.GetVisualTreeDescendants())
            {
                if (descendant is Element child &&
                    ScrollOwningTypes.Contains(child.GetType().Name))
                {
                    return true;
                }
            }
        }
        catch
        {
            // A partially built tree reports nothing; treat it as scroll-free.
        }

        return false;
    }

    private static bool PaintsBackground(Element element)
    {
        if (element is not VisualElement visual)
        {
            return false;
        }

        if (visual.Background is not null)
        {
            return true;
        }

        Color? background = visual.BackgroundColor;

        return background is not null && background != Colors.Transparent;
    }

    /// <summary>Tracks the host currently installed on each page.</summary>
    private static class LastHost
    {
        private static readonly ConditionalWeakTable<Page, View> Hosts = new();

        public static View? For(Page page)
            => Hosts.TryGetValue(page, out var host) ? host : null;

        public static void Set(Page page, View host)
        {
            Hosts.Remove(page);
            Hosts.Add(page, host);
        }
    }
}
