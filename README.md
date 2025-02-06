<h1 align="center">
  FluentDL
</h1>

<p align="center">
  <a href="https://github.com/derekyang2/fluentdl/releases/latest"><img src="https://img.shields.io/github/v/release/derekyang2/fluentdl"></a>
  <a href="https://github.com/derekyang2/fluentdl/releases"><img src="https://img.shields.io/github/downloads/derekyang2/fluentdl/total?logo=github">
</p>

<p align="center">
  <a href="#about">About</a> •
  <a href="#installation">Installation</a> •
  <a href="#build">Build</a> •
   <a href="#authentication">Authentication</a>
</p>

<p align="center">
  <img src="./SampleGifs/FluentDL_demo.webp" alt="Sample Webp" />
</p>

## About
A Fluent UI desktop application that helps you download lossless songs as FLACs, convert between audio formats, match songs between different online sources, edit song metadata, and more. This project was made with [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) and [TemplateStudio](https://github.com/microsoft/TemplateStudio). Under the hood, the app uses FFmpeg and APIs from Deezer, Qobuz, Spotify, and Youtube.

FluentDL is organized into three sections: Search, Local Explorer, and Queue.

<table>
  <tr>
    <td valign="top">
      <strong>Search</strong>
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
      <strong>Local Explorer</strong>
      <ul>
        <li>Upload files from your computer or scan all audio files in a folder</li>
        <li>View file metadata and technical audio specs in-depth</li>
        <li>Edit file metadata live, including option to change cover art!</li>
        <li>Convert between flac, mp3, aac, alac, vorbis, opus with control over bitrate</li>
      </ul>
    </td>
    <td>
      <p align="center"><img src="./SampleGifs/local_page.webp" alt="Local Webp"/></p>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Queue</strong>
      <ul>
        <li>Add files from Search or Local Explorer into the queue</li>
        <li>Run custom terminal tools on tracks (with wildcards)</li>
        <li>Match between any of the online sources (e.g., convert Spotify and YouTube to Deezer equivalents)</li>
        <li>Download tracks from Deezer, Qobuz, or Youtube with maximum quality</li>
        <li>Download entire queue using Convert: select Local as output</li>
      </ul>
    </td>
    <td>
      <p align="center"><img src="./SampleGifs/queue_page.webp" alt="Queue Webp"/></p>
    </td>
  </tr>
</table>

> [!NOTE]  
> Tip: change the number of threads in settings for significantly faster conversions, matching, and downloading.


## Installation 
This project is deployed using MSIX, which installs the application on Windows. To install this application, download the first zip from [Releases](https://github.com/DerekYang2/FluentDL/releases).

### Option 1
The application may be installed directly using a powershell script located in the zip folder. In the root folder inside of the zip, you should see an `Install.ps1` file and other files such as `FluentDL_{VERSION}_x64_MSIX.msix`. 

Right click on `Install.ps1` and press `Run with PowerShell`.

### Option 2

If that option is not available, open Powershell or CMD into the root directory and run the command:

```powershell.exe -executionpolicy unrestricted .\Install.ps1```

The application is now installed, and you should be able to find "FluentDL" with Search or in your Apps list.

## Build

Your machine should have the ability to develop WinUI 3 applications. The simplest setup method is using the Visual Studio Installer and selecting [Windows application development](https://devblogs.microsoft.com/visualstudio/dive-into-native-windows-development-with-new-winui-workload-and-template-improvements/).

Then, open the Solution file `.\FluentDL\FluentDL.sln` using Visual Studio. It should prompt you to install the correct .NET SDK. If not, install the latest .NET 8 SDK.  

An [experimental package](https://github.com/CommunityToolkit/Labs-Windows/issues/426) must be installed seperately. The package can be found [here](https://dev.azure.com/dotnet/CommunityToolkit/_artifacts/feed/CommunityToolkit-Labs/NuGet/CommunityToolkit.Labs.WinUI.MarqueeText/overview/0.1.250103-build.1988). Installation instructions for these packages can be found [here](https://dev.azure.com/dotnet/CommunityToolkit/_artifacts/feed/CommunityToolkit-Labs/connect) under the Visual Studio tab. 

In order to deploy the application, follow `Project > Publish > Create App Packages` and sign it with a certificate. 

The pre-built FFmpeg executable is found at [./Assets/ffmpeg/bin](https://github.com/DerekYang2/FluentDL/tree/master/Assets/ffmpeg/bin) and contains many additional codecs, such as libopus and libvorbis. 
 
You may use your own FFmpeg binaries, but note that libopus is required for Youtube's highest quality source.

## Authentication

Before using the application, head over to the settings page through the sidebar. 

### Searches
Searching/conversions between Deezer, Qobuz, and Youtube do not require authentication.

If you are logged into the Spotify web player, you will not need to authenticate. Authentication tokens are automatically grabbed from cookies. 

However, if you are not logged into the web player or there are auth issues, you may resort to developer API tokens. This method is guaranteed to work. These tokens (client ID and client secret) can be created for free through the Spotify Developer Dashboard. For more details on obtaining these tokens, visit the [official documentation](https://developer.spotify.com/documentation/web-api/tutorials/getting-started). 

### Downloading
Authentication requirements for downloading varies for the sources. The type of account (free vs subscription) may also affect the audio quality available. You do not have re-enter credentials each time because they are stored locally. Note that tokens expire or may occasionally become invalid due to web-player changes. 

<table>
  <tr>
    <td><strong>Service</strong></td>
    <td><strong>Downloads</strong></td>
  </tr>
  <tr>
    <td>Youtube</td>
    <td>No Authentication Required (128 kbps OPUS, which is similar to 256kbps MP3)</td>
  </tr>
  <tr>
    <td>Deezer</td>
    <td>Free Account (128 kbps MP3), Premium Account (320 kbps MP3 and 16bit/44.1kHZ FLAC)</td>
  </tr>
  <tr>
    <td>Qobuz</td>
    <td>Free Account (30 second preview), Premium Account (up to 24bit/192khz FLAC)</td>
  </tr>
  <tr>
    <td>Spotify</td>
    <td>Not Available</td>
  </tr>
</table>

You cannot determine the quality of a file by checking its bitrate. Files can be transcoded (converted), meaning a FLAC or high-bitrate file may have originated from a low-quality source. [Here](https://erikstechcorner.com/2020/09/how-to-check-if-your-flac-files-are-really-lossless/) is a guide on using Spek, a spectrogram tool, to verify audio file quality. 

#### Additional Notes:
- As verified through spectrogram, highest quality Youtube sources use the very efficient OPUS codec. The issue is OPUS containers, such as `.ogg` or `.webm`, have poor metadata support and compatability. FluentDL transcodes them into a FLAC in order to maintain original quality and support metadata. However, they are NOT lossless; this is an example of the transcoding mentioned above.
- Downloading directly from Spotify is not supported. Most tools out there download low quality MP3s. However, there are a few Python tools that get the true sources (320 kbps vorbis, 256 kbps AAC). Unfortunately, I could not find .NET equivalents. For FluentDL, use the convert tool to get equivalent Deezer/Qobuz/Youtube tracks, then set output to Local (download).


### Retrieving Tokens
If you already have them, enter them in settings. Otherwise:

In order to obtain your Deezer ARL, log into [https://www.deezer.com/](https://www.deezer.com/). Then open Developer Tools, and head to the `Application` tab. In the sidebar, open the dropdown list for `Cookies` and there should be an subitem `https://www.deezer.com/`. Click on the subitem and to find the the `arl` value, which should be 192 characters long. Note that you should open the _dropdown_ for the `Cookies` section, not click on it.

Similarly, to obtain a Qobuz id and token, log into [https://play.qobuz.com/](https://play.qobuz.com/) and open Developer Tools. Head over the `Application` tab, open the dropdown list for `Local Storage` and click on the subitem `https://play.qobuz.com`. You should then click on the `localuser` JSON object in the viewing window, where you can find the fields `id` (7 digits) and `token` (86 characters). 
