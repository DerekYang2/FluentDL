using System.Diagnostics;
using CommunityToolkit.WinUI.UI.Controls;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml;
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
        CustomListView.ItemsSource = QueueViewModel.Source;
    }

    private void CustomListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }
}