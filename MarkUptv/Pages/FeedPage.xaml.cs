using MarkUptv.ViewModels;
using Microsoft.Maui.Controls;

namespace MarkUptv.Pages;

public partial class FeedPage : ContentPage
{
    public FeedPage(FeedViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}