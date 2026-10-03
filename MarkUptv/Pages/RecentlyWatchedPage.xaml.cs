using Microsoft.Maui.Controls;
using CommunityToolkit.Maui.Core.Primitives;
using MarkUptv.ViewModels;
using MarkUptv.Models;
using System.Linq;
using System;

namespace MarkUptv.Pages
{
    public partial class RecentlyWatchedPage : ContentPage
    {
        private RecentlyWatchedViewModel? ViewModel => BindingContext as RecentlyWatchedViewModel;

        /// <summary>
        /// The dense 2-up tile's play chip: resumes the channel it was tapped on.
        /// </summary>
        private void OnTileWatchClicked(object? sender, EventArgs e)
        {
            if (sender is VisualElement element &&
                element.BindingContext is TvChannel channel &&
                ViewModel?.PlayRecentCommand?.CanExecute(channel) == true)
            {
                ViewModel.PlayRecentCommand.Execute(channel);
            }
        }

        public RecentlyWatchedPage(RecentlyWatchedViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
            MarkUptv.Helpers.PlaybackCoordinator.Register(Player);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Clean up resources and stop the player to prevent ghost audio
            if (Player != null)
            {
                MarkUptv.Helpers.PlaybackCoordinator.Release(Player);
                Player.Stop();
                Player.Source = null;
            }

            // Fire Dispose to clean up event handlers inside your ViewModel
            ViewModel?.Dispose();
        }

        // ========== 🎬 MEDIA PLAYER EVENTS ==========

        private void OnMediaOpened(object sender, EventArgs e)
        {
            ViewModel?.MediaOpened();
        }

        private void OnMediaFailed(object sender, MediaFailedEventArgs e)
        {
            ViewModel?.MediaFailed();
        }

        // ========== 🗂️ COLLECTION VIEW EVENTS ==========

        private void OnGroupSelected(object sender, SelectionChangedEventArgs e)
        {
            // Optional layout matching placeholder logic
        }

        private void OnChannelSelected(object sender, SelectionChangedEventArgs e)
        {
            var selectedChannel = e.CurrentSelection.FirstOrDefault() as TvChannel;

            if (selectedChannel != null && ViewModel != null)
            {
                if (ViewModel.PlayRecentCommand?.CanExecute(selectedChannel) == true)
                {
                    ViewModel.PlayRecentCommand.Execute(selectedChannel);
                }
            }

            // Clear the selection highlight for immediate structural reuse
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
        }

        // ========== 🖱️ GESTURE EVENTS ==========

        private void OnLogoClicked(object sender, TappedEventArgs e)
        {
            if (sender is Border border && border.BindingContext is TvChannel clickedChannel)
            {
                if (ViewModel?.PlayRecentCommand?.CanExecute(clickedChannel) == true)
                {
                    ViewModel.PlayRecentCommand.Execute(clickedChannel);
                }
            }
        }
    }
}
