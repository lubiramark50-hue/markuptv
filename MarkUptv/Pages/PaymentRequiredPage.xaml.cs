using MarkUptv.ViewModels;

namespace MarkUptv.Pages;

public partial class PaymentRequiredPage : ContentPage
{
    public PaymentRequiredPage(PaymentRequiredViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (BindingContext is PaymentRequiredViewModel viewModel)
        {
            if (viewModel.InitializeCommand.CanExecute(null))
            {
                viewModel.InitializeCommand.Execute(null);
            }
        }
    }
}