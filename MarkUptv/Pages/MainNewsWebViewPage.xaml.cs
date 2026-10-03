using MarkUptv.ViewModels;
using Microsoft.Maui.Controls;

namespace MarkUptv.Pages;

public partial class MainNewsWebViewPage : ContentPage, IQueryAttributable
{
    private readonly MainNewsWebViewPageViewModel _vm;
    private bool _firstLoad = true;

    public MainNewsWebViewPage(MainNewsWebViewPageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;

        // MAUI Shell already decodes query parameters before they reach
        // ApplyQueryAttributes, so the URL must be used as-is. Calling
        // Uri.UnescapeDataString again would double-decode any article URL
        // containing percent-encoded characters and corrupt it.
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("url", out var urlObj) && urlObj != null)
        {
            var urlString = urlObj.ToString();
            if (!string.IsNullOrWhiteSpace(urlString))
            {
                _vm.LoadArticle(urlString.Trim());
            }
        }
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        _vm.SetLoading(true);
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        _vm.SetLoading(false);

        if (e.Result != WebNavigationResult.Success)
        {
            _vm.SetError("The article could not be loaded. Check your connection and try again.");
            return;
        }

        _vm.SetError(null);

        if (_firstLoad)
        {
            _firstLoad = false;
            _ = UpdateTitleFromPageAsync();
        }
    }

    private async Task UpdateTitleFromPageAsync()
    {
        try
        {
            var title = await ContentWebView.EvaluateJavaScriptAsync("document.title");
            if (!string.IsNullOrWhiteSpace(title))
            {
                _vm.SetPageTitle(title.Trim());
            }
        }
        catch
        {
            // The page may not expose its title; keep the default.
        }
    }
}
