﻿using System.Diagnostics;
using FluentDL.Services;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();

        // Run an async task to get the rip config path
        new Task(() =>
        {
            var ripSubprocess = new RipSubprocess();
            var ripConfigPath = ripSubprocess.RunCommandSync("rip config path");
            ripSubprocess.Dispose();

            // Find first index of ' and second index of ' in the string
            var firstIndex = ripConfigPath.IndexOf('\'');
            var secondIndex = ripConfigPath.IndexOf('\'', firstIndex + 1);
            ripConfigPath = ripConfigPath.Substring(firstIndex + 1, secondIndex - firstIndex - 1);

            configTextBlock.Text = ripConfigPath;

            // Open the toml text file at ripConfigPath and save contents to string
            var configFileStr = System.IO.File.ReadAllText(ripConfigPath);
            Debug.WriteLine("CONFIG FILE:" + configFileStr);
        }).Start();
    }
}