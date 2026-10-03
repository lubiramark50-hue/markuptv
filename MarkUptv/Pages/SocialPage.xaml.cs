using MarkUptv.Models;
using MarkUptv.ViewModels;
using Microsoft.Maui.Controls;

namespace MarkUptv.Pages;

public partial class SocialPage : ContentPage
{
    private readonly SocialViewModel _viewModel;

	public SocialPage(SocialViewModel viewModel)
	{
		InitializeComponent();
        BindingContext = _viewModel = viewModel;
	}

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    // ─── Card gesture handlers ─────────────────────────────────────────
    // The card-level commands are wired to these code-behind handlers
    // instead of {x:Reference} bindings: inside a DataTemplate the
    // reference-based command bindings resolved against compiled-bindings
    // context silently no-op on device, so likes/shares/opens never fired.
    // An event handler has no binding to resolve and is robust.

    private void OnCardTapped(object? sender, TappedEventArgs e)
        => ExecuteCardCommand(_viewModel.OpenPostDetailCommand, sender);

    private void OnLikeTapped(object? sender, TappedEventArgs e)
        => ExecuteCardCommand(_viewModel.LikePostCommand, sender);

    private void OnShareTapped(object? sender, TappedEventArgs e)
        => ExecuteCardCommand(_viewModel.SharePostCommand, sender);

    private void OnBookmarkTapped(object? sender, TappedEventArgs e)
        => ExecuteCardCommand(_viewModel.BookmarkPostCommand, sender);

    private static void ExecuteCardCommand(
        System.Windows.Input.ICommand? command,
        object? sender)
    {
        if (command is null)
        {
            return;
        }

        if ((sender as Element)?.BindingContext is SocialPost post &&
            command.CanExecute(post))
        {
            command.Execute(post);
        }
    }
}
