using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.AccessCache;
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

        public static async Task<StorageFile?> PickFileAsync(IEnumerable<string> fileTypeFilter, PickerLocationId startLocationId = PickerLocationId.Downloads)
        { 
            // Create a file picker
            FileOpenPicker openPicker = new()
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = startLocationId
            };

            foreach (var filter in fileTypeFilter)
            {
                openPicker.FileTypeFilter.Add(filter);
            }

            // Retrieve the window handle (HWND) of the current WinUI 3 window.
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

            // Initialize the file picker with the window handle (HWND).
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

            StorageFile? file = await openPicker.PickSingleFileAsync();
            return file;
        }

        public static async Task<StorageFile?> FileSavePickerAsync(IEnumerable<string> fileTypeFilter, string suggestedFileName = "NewFile", PickerLocationId startLocationId = PickerLocationId.Downloads)
        {
            // Create a file save picker
            FileSavePicker savePicker = new()
            {
                SuggestedStartLocation = startLocationId,
                SuggestedFileName = suggestedFileName
            };
            foreach (var filter in fileTypeFilter)
            {
                savePicker.FileTypeChoices.Add(filter.TrimStart('.').ToUpper(), new List<string>() { filter });
            }
            // Retrieve the window handle (HWND) of the current WinUI 3 window.
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            // Initialize the file save picker with the window handle (HWND).
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
            StorageFile? file = await savePicker.PickSaveFileAsync();
            return file;
        }

        public static async Task<string?> GetDirectory()
        {

            // Open the picker for the user to pick a folder
            var folder = await StoragePickerHelper.PickFolderAsync(PickerLocationId.Downloads);
            if (folder != null)
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder); // Save the folder for future access
                return folder.Path;
            }
            else // Selected "cancel"
            {
                return null;
            }
        }
    }
}