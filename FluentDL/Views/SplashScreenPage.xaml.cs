using FluentDL.Helpers;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml.Controls;
using System.Reflection;
using Windows.ApplicationModel;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.Views;

public sealed partial class SplashScreenPage : Page
{
    private DispatcherQueue dispatcher;
    public SplashScreenViewModel ViewModel
    {
        get;
    }
    SortedDictionary<int, string> rowDict;

    public SplashScreenPage()
    {
        ViewModel = App.GetService<SplashScreenViewModel>();
        InitializeComponent();
        dispatcher = DispatcherQueue.GetForCurrentThread();
        rowDict = [];
        VersionText.Text = GetVersionDescription();
    }

    public void ClearRows()
    {
        dispatcher.TryEnqueue(() =>
        {
            rowDict.Clear();
        });
    }

    public void SetText(string s, int row = 0)
    {
        dispatcher.TryEnqueue(() =>
        {
            rowDict[row] = s;
            InfoText.Text = string.Join('\n', rowDict.Values);
        });
    }

    public static string GetVersionDescription()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;

            version = new(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version!;
        }

        return $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
