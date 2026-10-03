using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace MarkUptv.Helpers
{
    /// <summary>
    /// A gentle, repeating pulse used for LIVE indicators so the red dot
    /// continuously breathes instead of sitting static.
    ///
    /// The loop is single-flight per element: collection rebuilds (the live
    /// board auto-refreshes every minute) re-fire the Loaded trigger on
    /// Android, and every fire used to spawn ANOTHER infinite loop — the
    /// stacked loops pin the CPU, drain the battery and eventually ANR the
    /// app. Each element now pulses in exactly one loop, which also ends as
    /// soon as the element leaves the platform visual tree, so a hidden page
    /// never keeps animating in the background.
    /// </summary>
    public class PulseAnimation : TriggerAction<VisualElement>
    {
        private static readonly ConditionalWeakTable<VisualElement, object> Pulsing = new();

        protected override async void Invoke(VisualElement sender)
        {
            if (!Pulsing.TryAdd(sender, new object()))
            {
                // Already pulsing in an existing loop.
                return;
            }

            try
            {
                // Finite pulse: three beats then rest. Infinite animation loops
                // keep the Android UI automation busy-waiting (uiautomator dump
                // fails with 'could not get idle state') and burn battery for a
                // purely decorative effect.
                for (var beat = 0;
                     beat < 3 &&
                     sender.IsVisible &&
                     sender.Window is not null &&
                     sender.Handler is not null;
                     beat++)
                {
                    await sender.ScaleToAsync(1.25, 450, Easing.CubicOut);
                    await sender.ScaleToAsync(1.0, 450, Easing.CubicIn);
                }
            }
            catch
            {
                // Page teardown mid-animation – safe to stop.
            }
            finally
            {
                sender.CancelAnimations();
                sender.Scale = 1;
                Pulsing.Remove(sender);
            }
        }
    }
}
