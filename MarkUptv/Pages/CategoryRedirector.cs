namespace MarkUptv.Pages;

internal static class CategoryRedirector
{
    public static Task OpenAsync(string category, string title, string accent)
    {
        var route = $"{nameof(CategoryChannelPage)}?category={Uri.EscapeDataString(category)}&title={Uri.EscapeDataString(title)}&accent={Uri.EscapeDataString(accent)}";
        return Shell.Current?.GoToAsync(route) ?? Task.CompletedTask;
    }
}
