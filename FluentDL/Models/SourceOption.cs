using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentDL.Core.Sources;

namespace FluentDL.Models;

/// <summary>One source as a checkbox in the queue's clear and conversion dialogs.</summary>
public sealed class SourceOption : INotifyPropertyChanged
{
    private bool? _isChecked;
    private bool _isEnabled = true;

    public SourceOption(SourceDescriptor source)
    {
        Id = source.Id;
        DisplayName = source.DisplayName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public bool? IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
