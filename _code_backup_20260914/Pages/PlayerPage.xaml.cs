using MarkUptv.Helpers;
using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class PlayerPage : ContentPage
{
	private readonly PlayerViewModel _viewModel;

	public PlayerPage(PlayerViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel
			?? throw new System.ArgumentNullException(nameof(viewModel));
		BindingContext = viewModel;

		PlaybackCoordinator.Register(ChannelPlayer);
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		PlaybackCoordinator.Release(ChannelPlayer);
	}
}
