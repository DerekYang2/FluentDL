using System.Collections.ObjectModel;
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

        QueueViewModel.Source.CollectionChanged += (sender, e) =>
        {
            Debug.WriteLine("QueueViewModel.Source.CollectionChanged");
        };

        InitPreviewPanelButtons();
    }

    private void InitPreviewPanelButtons()
    {
        var downloadButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Download), Label = "Download" };

        var downloadCoverButton = new AppBarButton() { Icon = new FontIcon { Glyph = "\uEE71" }, Label = "Download Cover" };

        var removeButton = new AppBarButton() { Icon = new SymbolIcon(Symbol.Delete), Label = "Remove" };
        removeButton.Click += async (sender, e) =>
        {
            var selectedSong = PreviewPanel.GetSong();
            if (selectedSong == null)
            {
                return;
            }

            QueueViewModel.Remove(selectedSong);
            PreviewPanel.Clear();
        };

        PreviewPanel.SetAppBarButtons(new List<AppBarButton> { downloadButton, downloadCoverButton, removeButton });
    }

    private async void CustomListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Get the selected item
        var selectedSong = (SongSearchObject)CustomListView.SelectedItem;
        if (selectedSong == null)
        {
            PreviewPanel.Clear();
            return;
        }

        PreviewPanel.Show();
        await PreviewPanel.Update(selectedSong);
    }
}