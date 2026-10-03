using Microsoft.Maui.Controls;

namespace MarkUptv.Helpers
{
    public class BounceAnimation : TriggerAction<VisualElement>
    {
        protected override async void Invoke(VisualElement sender)
        {
            await sender.ScaleToAsync(0.8, 100);
            await sender.ScaleToAsync(1.1, 100);
            await sender.ScaleToAsync(1.0, 100);
        }
    }
}