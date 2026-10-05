using System.Reflection;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Microsoft.Maui.Handlers;
using AView = Android.Views.View;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiTap = Microsoft.Maui.Controls.TapGestureRecognizer;

namespace MarkUptv.Platforms.AndroidTv;

/// <summary>
/// Android TV remote support. Cards on the dashboard and pages are Borders with
/// a TapGestureRecognizer, which only react to touch and are invisible to
/// D-pad focus. On a television (and only there, phones are untouched) this
/// makes every tappable Border focusable, draws a gold focus ring, and turns
/// OK / Enter into the same action a tap would run.
/// </summary>
internal static class TvFocus
{
    private static bool _registered;

    public static bool IsTelevision { get; } = DetectTelevision();

    public static void Register()
    {
        if (_registered || !IsTelevision)
        {
            return;
        }

        _registered = true;

        BorderHandler.Mapper.AppendToMapping(
            "MarkUpTvDpadFocus",
            (handler, view) => Apply(handler.PlatformView as AView, view as MauiBorder));
    }

    private static bool DetectTelevision()
    {
        try
        {
            Context context = Application.Context;

            UiModeManager? uiMode =
                context.GetSystemService(Context.UiModeService) as UiModeManager;

            if (uiMode?.CurrentModeType == UiMode.TypeTelevision)
            {
                return true;
            }

            return context.PackageManager?.HasSystemFeature(
                PackageManager.FeatureLeanback) == true;
        }
        catch
        {
            return false;
        }
    }

    private static void Apply(AView? native, MauiBorder? border)
    {
        if (native is null || border is null)
        {
            return;
        }

        if (!border.GestureRecognizers.OfType<MauiTap>().Any())
        {
            return;
        }

        native.Focusable = true;
        native.FocusableInTouchMode = false;
        native.Clickable = false;

        native.FocusChange -= OnFocusChange;
        native.FocusChange += OnFocusChange;

        native.KeyPress -= OnKeyPress;
        native.KeyPress += OnKeyPress;

        native.Tag = new BorderRef(border);
    }

    private sealed class BorderRef
    {
        public BorderRef(MauiBorder border) => Border = new WeakReference<MauiBorder>(border);

        public WeakReference<MauiBorder> Border { get; }
    }

    private static void OnFocusChange(object? sender, AView.FocusChangeEventArgs e)
    {
        if (sender is not AView view)
        {
            return;
        }

        if (e.HasFocus)
        {
            float density = view.Resources?.DisplayMetrics?.Density ?? 1f;

            GradientDrawable ring = new();
            ring.SetColor(Color.Transparent);
            ring.SetStroke((int)(3 * density), Color.ParseColor("#F2C879"));
            ring.SetCornerRadius(16 * density);

            view.Foreground = ring;
            view.Animate()?.ScaleX(1.05f)?.ScaleY(1.05f)?.SetDuration(120)?.Start();
            view.BringToFront();
        }
        else
        {
            view.Foreground = null;
            view.Animate()?.ScaleX(1f)?.ScaleY(1f)?.SetDuration(120)?.Start();
        }
    }

    private static void OnKeyPress(object? sender, AView.KeyEventArgs e)
    {
        e.Handled = false;

        if (e.Event is null || e.Event.Action != KeyEventActions.Up)
        {
            return;
        }

        bool select =
            e.KeyCode == Keycode.DpadCenter ||
            e.KeyCode == Keycode.Enter ||
            e.KeyCode == Keycode.NumpadEnter ||
            e.KeyCode == Keycode.ButtonA;

        if (!select ||
            sender is not AView view ||
            view.Tag is not BorderRef reference ||
            !reference.Border.TryGetTarget(out MauiBorder? border))
        {
            return;
        }

        e.Handled = Activate(border);
    }

    private static bool Activate(MauiBorder border)
    {
        bool ran = false;

        foreach (MauiTap recognizer in border.GestureRecognizers.OfType<MauiTap>().ToList())
        {
            try
            {
                if (recognizer.Command is { } command &&
                    command.CanExecute(recognizer.CommandParameter))
                {
                    command.Execute(recognizer.CommandParameter);
                    ran = true;
                    continue;
                }

                MethodInfo? send = typeof(MauiTap).GetMethod(
                    "SendTapped",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (send is null)
                {
                    continue;
                }

                object?[] args = send
                    .GetParameters()
                    .Select((p, i) =>
                        i == 0 ? (object)border :
                        p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                    .ToArray();

                send.Invoke(recognizer, args);
                ran = true;
            }
            catch (Exception exception)
            {
                Android.Util.Log.Warn("MarkUpTV", "TV select failed: " + exception.Message);
            }
        }

        return ran;
    }
}
