using System;
using Microsoft.Maui.Controls;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages
{
    /// <summary>
    /// Code-behind managing the lifecycle bounds of the consolidated CategoryChannel Page.
    /// </summary>
    public partial class CategoryChannelPage : ContentPage
    {
        private readonly CategoryChannelViewModel _viewModel;

        public CategoryChannelPage(CategoryChannelViewModel viewModel)
        {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = _viewModel;

        MarkUptv.Helpers.PlaybackCoordinator.Register(ChannelPlayer);
    }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Clean up and release running media pipelines smoothly
            _viewModel.SelectedChannel = null;
            MarkUptv.Helpers.PlaybackCoordinator.Release(ChannelPlayer);
        }

        protected override async void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                await _viewModel.EnsureLoadedAsync();
            }
            catch (System.Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("MarkUpTV OnAppearing: " + exception.Message);
            }
        }

        private void OnStreamOpened(object? sender, EventArgs e)
        {
            _viewModel.MediaReadyCommand.Execute(null);
        }

        private void OnStreamPlaybackFailed(object? sender, EventArgs e)
        {
            _viewModel.MediaFailedCommand.Execute(null);
        }

        private async void OnChannelCardTapped(
            object? sender,
            TappedEventArgs e)
        {
            try
            {
                if (sender is not VisualElement element)
                {
                    return;
                }

                await AnimateCardAsync(element);

                PlayTappedChannel(element);
            }
            catch (System.Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("MarkUpTV OnChannelCardTapped: " + exception.Message);
            }
        }

        private async void OnWatchClicked(
            object? sender,
            EventArgs e)
        {
            try
            {
                if (sender is not VisualElement element)
                {
                    return;
                }

                await AnimateCardAsync(element);

                PlayTappedChannel(element);
            }
            catch (System.Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("MarkUpTV OnWatchClicked: " + exception.Message);
            }
        }

        private void PlayTappedChannel(
            VisualElement element)
        {
            if (element.BindingContext is Models.TvChannel channel)
            {
                _viewModel.PlayChannelCommand.Execute(channel);
            }
        }

        private static async Task AnimateCardAsync(
            VisualElement element)
        {
            element.CancelAnimations();

            try
            {
                await element.ScaleToAsync(
                    0.97,
                    70,
                    Easing.CubicOut);

                await element.ScaleToAsync(
                    1,
                    140,
                    Easing.SpringOut);
            }
            finally
            {
                element.Scale = 1;
                element.Opacity = 1;
            }
        }
    }
}
