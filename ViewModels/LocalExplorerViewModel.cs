using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.ViewModels;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Models;
using FluentDL.Services;

namespace FluentDL.ViewModels;

public partial class LocalExplorerViewModel : ObservableRecipient
{
    public LocalExplorerViewModel()
    {
    }

    public static SongSearchObject? ParseFile(string path)
    {
        return null;
    }
}