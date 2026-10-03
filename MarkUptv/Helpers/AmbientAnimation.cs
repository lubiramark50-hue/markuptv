namespace MarkUptv.Helpers;

/// <summary>
/// Controls the decorative "ambient" animations (floating shapes, equaliser
/// bars, halo glows) that pages play on entry.
///
/// Design rule for this app: ambient motion is a short, tasteful flourish —
/// never a permanent loop. A never-ending animation keeps the platform UI
/// permanently busy, which:
///   * drains battery and GPU on real phones,
///   * keeps the Android window from ever reaching an idle state (so
///     accessibility services and UI automation cannot inspect it),
///   * competes with the animations that actually matter (navigation,
///     player transitions),
///   * fights "reduce motion" expectations from users.
///
/// So every ambient loop runs a fixed number of beats and then settles.
/// </summary>
public static class AmbientAnimation
{
    /// <summary>
    /// Number of cycles a decorative animation plays before coming to rest.
    /// Three beats reads as intentional polish rather than as a stalled spinner.
    /// </summary>
    public const int DefaultBeats = 3;

    /// <summary>
    /// Returns true while the given beat index should still animate.
    /// </summary>
    /// <param name="beat">Zero-based cycle index.</param>
    /// <param name="cancellationToken">Page/service lifetime token.</param>
    /// <param name="beats">Optional override of <see cref="DefaultBeats"/>.</param>
    public static bool ShouldContinue(
        int beat,
        CancellationToken cancellationToken,
        int beats = DefaultBeats)
    {
        return beat < beats &&
               !cancellationToken.IsCancellationRequested;
    }
}
