namespace MarkUptv.Pages;

public partial class MorePage : ContentPage
{
    public MorePage()
    {
        InitializeComponent();
    }

    private async void OnRecentlyWatchedClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"///RecentlyWatchedPage");

    private async void OnSupportClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"///{nameof(DonationPage)}");

    private async void OnCommunityClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("///SocialPage");

    private async void OnSearchClicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("///SearchPage");
}
