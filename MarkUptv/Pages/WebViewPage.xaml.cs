using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.ApplicationModel;

namespace MarkUptv.Pages;

public partial class WebViewPage :
    ContentPage,
    IQueryAttributable
{
    private Uri? _currentUri;

    private string _pageTitle =
        "MarkUpTV Browser";

    private string _displayUrl =
        "No page loaded";

    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage =
        string.Empty;

    private bool _canGoBack;
    private bool _canGoForward;

    public string PageTitle
    {
        get => _pageTitle;

        private set
        {
            if (_pageTitle == value)
            {
                return;
            }

            _pageTitle = value;
            OnPropertyChanged();
        }
    }

    public string DisplayUrl
    {
        get => _displayUrl;

        private set
        {
            if (_displayUrl == value)
            {
                return;
            }

            _displayUrl = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;

        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool HasError
    {
        get => _hasError;

        private set
        {
            if (_hasError == value)
            {
                return;
            }

            _hasError = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;

        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public bool CanGoBack
    {
        get => _canGoBack;

        private set
        {
            if (_canGoBack == value)
            {
                return;
            }

            _canGoBack = value;
            OnPropertyChanged();
        }
    }

    public bool CanGoForward
    {
        get => _canGoForward;

        private set
        {
            if (_canGoForward == value)
            {
                return;
            }

            _canGoForward = value;
            OnPropertyChanged();
        }
    }

    public WebViewPage()
    {
        InitializeComponent();

        BindingContext = this;
    }

    public void ApplyQueryAttributes(
    IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        string requestedTitle =
            ReadQueryValue(
                query,
                "pageTitle");

        string requestedUrl =
            ReadQueryValue(
                query,
                "url");

        if (!string.IsNullOrWhiteSpace(requestedTitle))
        {
            PageTitle = requestedTitle;
        }

        if (!TryCreateSafeWebUri(
                requestedUrl,
                out Uri? uri))
        {
            ShowError(
                "The selected result does not contain a valid HTTP or HTTPS address.");

            return;
        }

        /*
         * NotNullWhen(true) guarantees that uri is non-null here.
         */
        LoadUri(uri);
    }

    protected override void OnDisappearing()
    {
        BrowserHeader.CancelAnimations();
        LoadingBar.CancelAnimations();

        base.OnDisappearing();
    }

    private void LoadUri(
        Uri uri)
    {
        _currentUri = uri;

        PageTitle =
            string.IsNullOrWhiteSpace(PageTitle)
                ? uri.Host
                : PageTitle;

        DisplayUrl =
            CreateDisplayUrl(uri);

        HasError = false;
        ErrorMessage = string.Empty;
        IsLoading = true;

        ContentWebView.Source =
            new UrlWebViewSource
            {
                Url = uri.AbsoluteUri
            };
    }

    private async void OnWebViewNavigating(
        object? sender,
        WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(
                e.Url,
                UriKind.Absolute,
                out Uri? uri))
        {
            e.Cancel = true;

            ShowError(
                "The page attempted to open an invalid address.");

            return;
        }

        if (!IsHttpOrHttps(uri))
        {
            e.Cancel = true;

            if (uri.Scheme.Equals(
                    "mailto",
                    StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(
                    "tel",
                    StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await Launcher.Default.OpenAsync(uri);
                }
                catch (Exception exception)
                {
                    ShowError(
                        $"This link could not be opened: {exception.Message}");
                }
            }

            return;
        }

        _currentUri = uri;
        DisplayUrl = CreateDisplayUrl(uri);

        HasError = false;
        ErrorMessage = string.Empty;
        IsLoading = true;

        LoadingBar.CancelAnimations();
        LoadingBar.Progress = 0.06;

        _ = LoadingBar.ProgressTo(
            0.78,
            1_200,
            Easing.CubicOut);
    }

    private async void OnWebViewNavigated(
        object? sender,
        WebNavigatedEventArgs e)
    {
        LoadingBar.CancelAnimations();

        try
        {
            await LoadingBar.ProgressTo(
                1,
                160,
                Easing.CubicOut);
        }
        catch
        {
            // Visual progress completion is non-critical.
        }

        IsLoading = false;

        CanGoBack =
            ContentWebView.CanGoBack;

        CanGoForward =
            ContentWebView.CanGoForward;

        if (e.Result !=
            WebNavigationResult.Success)
        {
            ShowError(
                CreateNavigationErrorMessage(
                    e.Result));

            return;
        }

        HasError = false;
        ErrorMessage = string.Empty;

        if (Uri.TryCreate(
                e.Url,
                UriKind.Absolute,
                out Uri? navigatedUri))
        {
            _currentUri = navigatedUri;
            DisplayUrl = CreateDisplayUrl(navigatedUri);
        }

        try
        {
            string? rawTitle =
                await ContentWebView
                    .EvaluateJavaScriptAsync(
                        "document.title");

            string normalizedTitle =
                NormalizeJavaScriptString(
                    rawTitle);

            if (!string.IsNullOrWhiteSpace(
                    normalizedTitle))
            {
                PageTitle = normalizedTitle;
            }
            else if (_currentUri is not null)
            {
                PageTitle = _currentUri.Host;
            }
        }
        catch
        {
            if (_currentUri is not null &&
                string.IsNullOrWhiteSpace(PageTitle))
            {
                PageTitle = _currentUri.Host;
            }
        }
        finally
        {
            LoadingBar.Progress = 0;
        }
    }

    private async void OnCloseClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await AnimateButtonAsync(sender);

            if (Shell.Current is not null)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }

            if (Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync();
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnCloseClicked: " + exception.Message);
        }
    }

    private async void OnBackClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await AnimateButtonAsync(sender);

            if (ContentWebView.CanGoBack)
            {
                ContentWebView.GoBack();
                return;
            }

            if (Shell.Current is not null)
            {
                await Shell.Current.GoToAsync("..");
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnBackClicked: " + exception.Message);
        }
    }

    private async void OnForwardClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await AnimateButtonAsync(sender);

            if (ContentWebView.CanGoForward)
            {
                ContentWebView.GoForward();
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnForwardClicked: " + exception.Message);
        }
    }

    private async void OnRefreshClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await AnimateButtonAsync(sender);

            HasError = false;
            ErrorMessage = string.Empty;

            ContentWebView.Reload();
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnRefreshClicked: " + exception.Message);
        }
    }

    private async void OnRetryClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await AnimateButtonAsync(sender);

            if (_currentUri is not null)
            {
                LoadUri(_currentUri);
            }
        }
        catch (System.Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("MarkUpTV OnRetryClicked: " + exception.Message);
        }
    }

    private async void OnOpenExternalClicked(
        object? sender,
        EventArgs e)
    {
        await AnimateButtonAsync(sender);

        if (_currentUri is null)
        {
            ShowError(
                "There is no web address to open.");

            return;
        }

        try
        {
            bool canOpen =
                await Launcher.Default.CanOpenAsync(
                    _currentUri);

            if (!canOpen)
            {
                ShowError(
                    "No external browser is available.");

                return;
            }

            await Launcher.Default.OpenAsync(
                _currentUri);
        }
        catch (Exception exception)
        {
            ShowError(
                $"The external browser could not be opened: {exception.Message}");
        }
    }

    private static async Task AnimateButtonAsync(
        object? sender)
    {
        if (sender is not
            Microsoft.Maui.Controls.VisualElement element)
        {
            return;
        }

        element.CancelAnimations();

        try
        {
            await element.ScaleToAsync(
                0.9,
                60,
                Easing.CubicOut);

            await element.ScaleToAsync(
                1,
                135,
                Easing.SpringOut);
        }
        finally
        {
            element.Scale = 1;
        }
    }

    private void ShowError(
        string message)
    {
        IsLoading = false;
        HasError = true;
        ErrorMessage = message;

        LoadingBar.CancelAnimations();
        LoadingBar.Progress = 0;
    }

    private static string ReadQueryValue(
        IDictionary<string, object> query,
        string key)
    {
        if (!query.TryGetValue(
                key,
                out object? value))
        {
            return string.Empty;
        }

        string raw =
            value?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
            return raw;
        }
    }

    private static bool TryCreateSafeWebUri(
      string? value,
      [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(
                value.Trim(),
                UriKind.Absolute,
                out Uri? parsedUri))
        {
            return false;
        }

        if (!IsHttpOrHttps(parsedUri))
        {
            return false;
        }

        uri = parsedUri;
        return true;
    }

    private static bool IsHttpOrHttps(
        Uri uri)
    {
        return
            uri.Scheme.Equals(
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateDisplayUrl(
        Uri uri)
    {
        string path =
            uri.PathAndQuery == "/"
                ? string.Empty
                : uri.PathAndQuery;

        return $"{uri.Host}{path}";
    }

    private static string NormalizeJavaScriptString(
     string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized =
            value.Trim();

        /*
         * EvaluateJavaScriptAsync may return a quoted JavaScript string.
         * Decode the small set of escapes used in page titles without
         * invoking reflection-based JSON deserialization.
         */
        if (normalized.Length >= 2 &&
            normalized[0] == '"' &&
            normalized[^1] == '"')
        {
            normalized =
                normalized[1..^1];
        }

        return normalized
            .Replace(
                "\\\"",
                "\"",
                StringComparison.Ordinal)
            .Replace(
                "\\'",
                "'",
                StringComparison.Ordinal)
            .Replace(
                "\\n",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\\r",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\\t",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\\/",
                "/",
                StringComparison.Ordinal)
            .Replace(
                "\\\\",
                "\\",
                StringComparison.Ordinal)
            .Trim();
    }

    private static string CreateNavigationErrorMessage(
     WebNavigationResult result)
    {
        return result switch
        {
            WebNavigationResult.Success =>
                string.Empty,

            WebNavigationResult.Cancel =>
                "The page navigation was cancelled.",

            WebNavigationResult.Timeout =>
                "The website took too long to respond.",

            WebNavigationResult.Failure =>
                "The website could not be reached or returned an invalid response.",

            _ =>
                "The website could not be displayed."
        };
    }
}