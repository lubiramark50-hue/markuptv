using Microsoft.Maui.Controls;

namespace MarkUptv.Helpers
{
    /// <summary>
    /// Gives tappable elements instant visual feedback: scales down on press,
    /// springs back on release or when the pointer leaves the element.
    /// Attach to any Border/Grid/etc. that also has a TapGestureRecognizer.
    /// </summary>
    public class TapScaleBehavior : Behavior<VisualElement>
    {
        private View? _view;
        private PointerGestureRecognizer? _pointer;

        public double PressedScale { get; set; } = 0.96;
        public uint PressDuration { get; set; } = 80;
        public uint ReleaseDuration { get; set; } = 120;

        protected override void OnAttachedTo(VisualElement bindable)
        {
            base.OnAttachedTo(bindable);
            if (bindable is not View view)
                return;

            _view = view;
            _pointer = new PointerGestureRecognizer();
            _pointer.PointerPressed += OnPointerPressed;
            _pointer.PointerReleased += OnPointerReleased;
            _pointer.PointerExited += OnPointerExited;
            view.GestureRecognizers.Add(_pointer);
        }

        protected override void OnDetachingFrom(VisualElement bindable)
        {
            base.OnDetachingFrom(bindable);
            if (_pointer is not null && bindable is View view)
            {
                _pointer.PointerPressed -= OnPointerPressed;
                _pointer.PointerReleased -= OnPointerReleased;
                _pointer.PointerExited -= OnPointerExited;
                view.GestureRecognizers.Remove(_pointer);
                _pointer = null;
            }
            _view = null;
        }

        private async void OnPointerPressed(object? sender, PointerEventArgs e)
        {
            if (_view is null)
                return;
            _view.CancelAnimations();
            await _view.ScaleToAsync(PressedScale, PressDuration, Easing.CubicOut);
        }

        private async void OnPointerReleased(object? sender, PointerEventArgs e)
        {
            if (_view is null)
                return;
            await _view.ScaleToAsync(1.0, ReleaseDuration, Easing.SpringOut);
        }

        private async void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (_view is null)
                return;
            await _view.ScaleToAsync(1.0, ReleaseDuration, Easing.CubicOut);
        }
    }
}
