using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class PostDetailPage : ContentPage
{
    public PostDetailPage(PostDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
