using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentDL.ViewModels;

public partial class SplashScreenViewModel : ObservableRecipient
{
    private string _loadingText = string.Empty;

    public string LoadingText
    {
        get => _loadingText;
        set => SetProperty(ref _loadingText, value);
    }

    public SplashScreenViewModel()
    {
    }
}
