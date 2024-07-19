using CommunityToolkit.WinUI.UI.Controls;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class QueuePage : Page
{
    public QueueViewModel ViewModel
    {
        get;
    }

    public QueuePage()
    {
        ViewModel = App.GetService<QueueViewModel>();
        InitializeComponent();
    }
}