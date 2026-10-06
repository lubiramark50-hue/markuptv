using MarkUptv.Models;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class CommunityHubPage : ContentPage
{
    private readonly CommunityHubViewModel _viewModel;

    public CommunityHubPage(CommunityHubViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));

        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ReloadCommand.Execute(null);
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.ExportCommand.ExecuteAsync(null);
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnExportClicked: " + exception.Message);
        }
    }

    private async void OnThreadTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (sender is BindableObject bindable && bindable.BindingContext is MatchThread thread)
            {
                await _viewModel.OpenThreadCommand.ExecuteAsync(thread);
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnThreadTapped: " + exception.Message);
        }
    }

    private void OnUnfollowClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is string team)
        {
            _viewModel.UnfollowCommand.Execute(team);
        }
    }

    private void OnUnblockClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is string author)
        {
            _viewModel.UnblockCommand.Execute(author);
        }
    }

    private void OnUpholdClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is CommunityReport report)
        {
            _viewModel.UpholdCommand.Execute(report);
        }
    }

    private void OnDismissClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject bindable && bindable.BindingContext is CommunityReport report)
        {
            _viewModel.DismissCommand.Execute(report);
        }
    }
}
