namespace MarkUptv.Pages;

public partial class ConnectionRequiredPage : ContentPage
{
    public ConnectionRequiredPage()
    {
        InitializeComponent();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        bool reachedServer = false;

        try
        {
            RetryButton.IsEnabled = false;
            StatusLabel.IsVisible = false;
            Spinner.IsVisible = true;
            Spinner.IsRunning = true;

            if (Application.Current is App app)
            {
                reachedServer = await app.RetryAccessCheckAsync();
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV retry: " + exception.Message);
        }
        finally
        {
            Spinner.IsRunning = false;
            Spinner.IsVisible = false;
            RetryButton.IsEnabled = true;

            if (!reachedServer)
            {
                StatusLabel.Text = "Still can't connect. Check your internet and try again.";
                StatusLabel.IsVisible = true;
            }
        }
    }
}
