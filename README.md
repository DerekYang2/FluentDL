<h1 align="center">
  FluentDL
</h1>
<p align="center">
  <a href="#about">About</a> •
  <a href="#setup">Setup</a> •
  <a href="#running">Running</a> 
</p>
<p align="center">
  <img src="./SampleGifs/FluentDL_demo.webp" alt="Sample Webp" />
</p>

## About
A Fluent UI desktop application that helps you manage your local music files, perform audio format conversions, download songs, match songs between different online sources, and more. This project was made with [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) and [TemplateStudio](https://github.com/microsoft/TemplateStudio). Under the hood, the app uses FFmpeg and APIs from Deezer, Qobuz, Spotify, and Youtube.

FluentDL is organized into three sections: Search, Local Explorer, and Queue.

<table>
  <tr>
    <td valign="top">
      <h4>Search</h4>
      <ul>
        <li>Lookup songs from any of the four online sources</li>
        <li>Search using natural language or strict search by title/artist/album</li>
        <li>Parse all tracks from an online link, with track/album/playlist links supported</li>
        <li>Open songs in preview sidebar that can view large cover art, preview audio, show full metadata</li>
      </ul>
    </td>
    <td>
      <p align="center"><img src="./SampleGifs/search_page.webp" alt="Search Webp"/></p>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <h4>Local Explorer</h4>
      <ul>
        <li>Upload files from your computer or scan all audio files in a folder</li>
        <li>View file metadata and technical audio specs in-depth</li>
        <li>Edit file metadata live, including option to change cover art!</li>
        <li>Convert between any of these formats: flac, mp3, aac, alac, vorbis, opus</li>
      </ul>
    </td>
    <td>
      <p align="center"><img src="./SampleGifs/local_page.webp" alt="Local Webp"/></p>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <h4>Queue</h4>
      <ul>
        <li>Add files from Search or Local Explorer into the queue</li>
        <li>Run custom terminal tools on tracks (with wildcards)</li>
        <li>Match between any of the online sources (e.g., convert Spotify and YouTube to Deezer equivalents)</li>
        <li>Download tracks from online sources</li>
      </ul>
    </td>
    <td>
      <p align="center"><img src="./SampleGifs/queue_page.webp" alt="Queue Webp"/></p>
    </td>
  </tr>
</table>

## Setup

In order to run these project in Visual Studio, you must have WinUI 3 setup (Template Studio is not required).

WinUI 3 can be automatically configured using the Visual Studio Installer or manual installation. See the [official documentation](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/create-your-first-winui3-app) for full details.

All dependencies should be automatically handled by Visual Studio and can be found on NuGet. There is one package [MarqueeText](https://dev.azure.com/dotnet/CommunityToolkit/_artifacts/feed/CommunityToolkit-Labs/NuGet/CommunityToolkit.Labs.WinUI.MarqueeText) that has to be installed manually.

A pre-built FFmpeg executable can be found in [/Assets/ffmpeg/bin](https://github.com/DerekYang2/FluentDL/tree/master/Assets/ffmpeg/bin) and contains many additional codecs, such as libopus and libvorbis. You may use your own FFmpeg binaries, but note that libopus is required for proper Youtube downloading. 

## Running

This project is deployed using MSIX, which installs the application on Windows. 
To install this application, download the folder from [Releases](https://github.com/DerekYang2/FluentDL/releases) and open the `FluentDL_{VERSION}_x64_MSIX.msix` file. This will open a prompt that installs the application and all its dependencies (including the FFmpeg executable). 

The installation process currently requires an extra step because the certificate is self-signed. It must be trusted by the user before running the MSIX installer.

#### How do I trust the certificate?
In order to trust the certificate, click and open the `FluentDL_Certificate.cer` certificate file in the folder. 

You will see a security warning prompt ("Do you want to open this file?") and press open. 

Next, the certificate pop-up will explain how to add the certificate to "Trusted Root Certification Authorities". In order to this, follow these steps:

- Press `Install Certificate...`, which should open "Certificate Import Wizard"
- Choose  `Local Machine` then select `Next`
- Choose `Place all certificates in the following store` then press `Browse...`, which should open a new dialog
- In the new "Select Certificate Store" dialog, select the second option `Trusted Root Certification Authorities` and press `OK`
- Select `Next` and then `Finish`. You should see a dialog that says: The import was successful.

After adding the certificate to this storage, `FluentDL_Certificate.cer` should be trusted and the MSIX install button should not be greyed out anymore.
