using System.Diagnostics;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FluentDL.Views;

public sealed partial class LocalExplorerPage : Page
{
    public LocalExplorerViewModel ViewModel
    {
        get;
    }

    public LocalExplorerPage()
    {
        ViewModel = App.GetService<LocalExplorerViewModel>();
        InitializeComponent();
    }

    private void SortOrderComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void SortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private async void UploadButton_OnClick(object sender, RoutedEventArgs e)
    {
        FileOpenPicker fileOpenPicker = new() { ViewMode = PickerViewMode.Thumbnail, FileTypeFilter = { ".mp3", ".aac", ".flac", ".wav" }, };

        var windowHandle = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(fileOpenPicker, windowHandle);

        StorageFile file = await fileOpenPicker.PickSingleFileAsync();

        if (file != null)
        {
            Debug.WriteLine("Selected file: " + file.Path);
        }
    }

    private void FileListView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }
}