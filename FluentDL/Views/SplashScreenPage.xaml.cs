using FluentDL.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class SplashScreenPage : Page
{
    public SplashScreenViewModel ViewModel
    {
        get;
    }

    public SplashScreenPage()
    {
        ViewModel = App.GetService<SplashScreenViewModel>();
        InitializeComponent();
    }
}
