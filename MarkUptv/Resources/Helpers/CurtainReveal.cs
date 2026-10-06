namespace MarkUptv.Helpers
{
    /// <summary>
    /// Animates a View by scaling it from 0 to 1 with a slight overshoot,
    /// simulating a curtain opening.
    /// </summary>
    public class CurtainReveal : TriggerAction<VisualElement>
    {
        protected override async void Invoke(VisualElement sender)
        {
            try
            {
                sender.Scale = 0;
                sender.Opacity = 0;
                sender.IsVisible = true;

                await Task.WhenAll(
                    sender.ScaleToAsync(1.0, 500, Easing.CubicOut),
                    sender.FadeToAsync(1, 400)
                );
            }
            catch (System.Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("MarkUpTV Invoke: " + exception.Message);
            }
        }
    }
}