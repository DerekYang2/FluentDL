using FluentDL.ViewModels;
using Microsoft.UI.Xaml.Controls;
using SQLitePCL;
using System.Collections;
using System.Globalization;
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
}
