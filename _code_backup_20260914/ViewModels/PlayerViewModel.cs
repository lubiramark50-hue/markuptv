namespace MarkUptv.ViewModels;

public partial class PlayerViewModel : BaseViewModel, IQueryAttributable
{
    private string _streamUrl = string.Empty;
    private string _title = "Live Player";

    public string StreamUrl
    {
        get => _streamUrl;
        set { _streamUrl = value; OnPropertyChanged(); HasStream = !string.IsNullOrWhiteSpace(value); }
    }

    public string Title
    {
        get => _title;
        set { _title = string.IsNullOrWhiteSpace(value) ? "Live Player" : value; OnPropertyChanged(); }
    }

    private bool _hasStream;
    public bool HasStream
    {
        get => _hasStream;
        private set { _hasStream = value; OnPropertyChanged(); }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("streamUrl", out var stream))
            StreamUrl = Uri.UnescapeDataString(stream?.ToString() ?? string.Empty);

        if (query.TryGetValue("title", out var title))
            Title = Uri.UnescapeDataString(title?.ToString() ?? string.Empty);
    }
}
