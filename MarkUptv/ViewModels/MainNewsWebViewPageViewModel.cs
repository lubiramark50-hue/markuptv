using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace MarkUptv.ViewModels;

public partial class MainNewsWebViewPageViewModel : ObservableObject, IQueryAttributable
{
    private string _url = string.Empty;
    private string _pageTitle = "MarkUpTV News";
    private bool _isLoading = true;
    private string? _errorMessage;

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public string PageTitle
    {
        get => _pageTitle;
        set => SetProperty(ref _pageTitle, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool HasError =>
        !string.IsNullOrWhiteSpace(_errorMessage);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("url", out var urlObj) && urlObj != null)
        {
            var urlString = urlObj.ToString();
            if (!string.IsNullOrWhiteSpace(urlString))
            {
                LoadArticle(urlString.Trim());
            }
        }
    }

    /// <summary>
    /// Starts loading an article. The URL is used exactly as the Shell
    /// navigation supplied it — Shell already decodes query parameters.
    /// </summary>
    public void LoadArticle(string url)
    {
        Url = url;
        PageTitle = "Loading…";
        SetLoading(true);
        SetError(null);
    }

    public void SetLoading(bool isLoading)
    {
        IsLoading = isLoading;
    }

    public void SetError(string? message)
    {
        ErrorMessage = message;
    }

    public void SetPageTitle(string title)
    {
        PageTitle = string.IsNullOrWhiteSpace(title)
            ? "MarkUpTV News"
            : title.Trim();
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (Shell.Current is Shell shell)
        {
            await shell.GoToAsync("..");
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return;
        }

        SetLoading(true);
        SetError(null);

        // Re-point the WebView at the same URL to force a reload.
        var webView = FindWebView();
        webView?.Reload();
    }

    private static WebView? FindWebView()
    {
        if (Shell.Current?.CurrentPage is ContentPage { Content: Grid grid } &&
            grid.FindByName<WebView>("ContentWebView") is WebView webView)
        {
            return webView;
        }

        return null;
    }
}
