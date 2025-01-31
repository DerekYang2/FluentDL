namespace FluentDL.Helpers
{
    class Clipboard
    {
        public static void CopyToClipboard(string text)
        {
            // Copy the text to the clipboard
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
    }
}