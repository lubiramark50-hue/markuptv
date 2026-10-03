using Microsoft.Maui.Controls;

namespace MarkUptv.Helpers
{
    /// <summary>
    /// Pulsing animation for the AI chat "typing" indicator dots.
    /// Used from a DataTrigger EnterAction; keeps animating for as long
    /// as the attached element remains visible.
    /// </summary>
    public class AnimateTypingAction : TriggerAction<VisualElement>
    {
        /// <summary>Stagger delay in milliseconds (each dot starts later).</summary>
        public int Delay { get; set; } = 0;

        protected override async void Invoke(VisualElement sender)
        {
            try
            {
                sender.AbortAnimation(nameof(AnimateTypingAction));
                sender.Opacity = 0.3;

                if (Delay > 0)
                {
                    await Task.Delay(Delay);
                }

                while (sender.IsVisible)
                {
                    await Task.WhenAll(
                        sender.FadeToAsync(1.0, 350, Easing.SinInOut),
                        sender.ScaleToAsync(1.15, 350, Easing.SinInOut));

                    await Task.WhenAll(
                        sender.FadeToAsync(0.3, 350, Easing.SinInOut),
                        sender.ScaleToAsync(1.0, 350, Easing.SinInOut));
                }
            }
            catch
            {
                // Animation lifecycle is best-effort; the element may be
                // detached or the page may be closing while it runs.
            }
        }
    }
}