using Windows.Storage;
using Windows.Storage.Pickers;

namespace FluentDL.Helpers
{
    class StoragePickerHelper
    {
        public static async Task<StorageFolder?> PickFolderAsync(PickerLocationId startLocationId)
        {
            var openPicker = new Windows.Storage.Pickers.FolderPicker();

            // Retrieve the window handle of the current WinUI 3 window.
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

            // Initialize the folder picker with the window handle.
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

            openPicker.SuggestedStartLocation = startLocationId;
            openPicker.FileTypeFilter.Add("*");

            return await openPicker.PickSingleFolderAsync();
        }
    }
}