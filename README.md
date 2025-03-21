<h1 align="center">
    <a href="https://github.com/DerekYang2/FluentDL">
        <picture> 
          <source media="(prefers-color-scheme: dark)" srcset="https://github.com/user-attachments/assets/54f71675-9400-44eb-bc75-0e0e5084eaa0">
          <source media="(prefers-color-scheme: light)" srcset="https://github.com/user-attachments/assets/a7c68acd-1987-4a54-a157-c008fe584051">
          <img alt="FluentDL" src="https://github.com/user-attachments/assets/54f71675-9400-44eb-bc75-0e0e5084eaa0" height="44">
        </picture>
    </a>
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
A Fluent UI desktop application that helps you download lossless songs as FLACs, convert between audio formats, match songs between different online sources, edit song metadata, and more. This project was made with [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) and [TemplateStudio](https://github.com/microsoft/TemplateStudio). Under the hood, the app uses FFmpeg and APIs for Deezer, Qobuz, Spotify, and Youtube.

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
        <li>Add songs to Queue (see below)</li>
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
        <li>View file spectrogram using Spek</li>
        <li>Convert between flac, mp3, aac, alac, vorbis, opus with control over bitrate</li>
        <li>Add songs to Queue (see below)</li>
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
        <li>Matching between all possible combinations of online sources (e.g., convert Spotify and YouTube to Deezer equivalents)</li>
        <li>Download tracks from Deezer, Qobuz, or Youtube with maximum quality</li>
        <li>Inspect downloaded tracks using Spek, a spectrogram tool</li>
        <li>Run custom terminal tools on tracks using wildcards</li>
      </ul>
    </td>
    <td>
      <p align="center"><img src="./SampleGifs/queue_page.webp" alt="Queue Webp"/></p>
    </td>
  </tr>
</table>

## Installation 
This project is deployed using MSIX, which installs the application on Windows. To install this application, download the first zip from [Releases](https://github.com/DerekYang2/FluentDL/releases).

### Option 1
The application may be installed directly using a powershell script located in the zip folder. In the root folder inside of the zip, you should see an `Install.ps1` file and other files such as `FluentDL_{VERSION}_x64_MSIX.msix`. 

Right click on `Install.ps1` and press `Run with PowerShell`.

If you have already installed FluentDL before, you can directly double click to run `FluentDL_{VERSION}_x64_MSIX.msix`. The release will note when this does not work (the occasion that the certificate is updated/changed). 

### Option 2

If that option is not available, open Powershell or CMD into the root directory and run the command:

```powershell.exe -executionpolicy unrestricted .\Install.ps1```

The application is now installed, and you should be able to find "FluentDL" with Search or in your Apps list.

<details>
  <summary><b>What does the Powershell script do?</b></summary>
    
Ideally, you would only need to run `FluentDL_{VERSION}_x64_MSIX.msix`, which opens the official Microsoft Store installer interface. However, the certificate is self-signed because ones from certificate authorities can cost hundreds of dollars per year. The powershell script trusts the self-signed certificate on your machine and then runs the MSIX. The <a href="https://superuser.com/questions/463081/adding-self-signed-certificate-to-trusted-root-certificate-store-using-command-l">manual way</a> of trusting a certificate is more work. This is also why if you have already ran the script (trusted the certificate), you can directly run the MSIX in the future. 

A future solution could be deploying to the Microsoft Store directly for a smaller, one-time free. 

</details>

## Build

The steps below are only relevant for developers who wish to modify source code. 

Your machine should have the ability to develop WinUI 3 applications. The simplest setup method is using the Visual Studio Installer and selecting [Windows application development](https://devblogs.microsoft.com/visualstudio/dive-into-native-windows-development-with-new-winui-workload-and-template-improvements/).

Then, open the Solution file `.\FluentDL\FluentDL.sln` using Visual Studio. You must actually open the solution file, not just the project folder. It should prompt you to install the correct .NET SDK. If not, install the latest .NET 8 SDK.  

An [experimental package](https://github.com/CommunityToolkit/Labs-Windows/issues/426) was installed seperately. If it is not automatically handled by the nuget.config, install the package manually. 

To install manually:
- Open the Tools menu, select Options > NuGet Package Manager > Package Sources.
- Select the green plus in the upper-right corner and add `https://pkgs.dev.azure.com/dotnet/CommunityToolkit/_packaging/CommunityToolkit-Labs/nuget/v3/index.json` as a source.
- Now install the Nuget package as you normally would. Use the GUI to install MarqueeText `0.1.250103-build.1988` (ensure pre-release checkbox is selected). Alternatively, enter `Install-Package CommunityToolkit.Labs.WinUI.MarqueeText -version 0.1.250103-build.1988` in the VS Package Manager Console.
Read more about the package and installation instructions [here](https://dev.azure.com/dotnet/CommunityToolkit/_artifacts/feed/CommunityToolkit-Labs/NuGet/CommunityToolkit.Labs.WinUI.MarqueeText/overview/0.1.250103-build.1988).

To run the application, hit the `FluentDL (Package)` play button. To deploy the application, follow `Project > Publish > Create App Packages` and sign it with a certificate. 

Cloning the repo may take a while because it is bundled with FFmpeg and Spek. The pre-built FFmpeg executable is found at [./FluentDL/Assets/ffmpeg/bin](https://github.com/DerekYang2/FluentDL/blob/master/FluentDL/Assets/ffmpeg/bin/ffmpeg.exe) and contains many additional codecs, such as libopus and libvorbis. 

## Authentication

> [!NOTE]  
> Streaming services occasionally make changes to APIs, which may result in authentication issues. Double check the [Issues](https://github.com/DerekYang2/FluentDL/issues) page for ongoing problems. 

The authentication required depends on the sources and features you use. 

### Searching and Converting
Searches/conversions for Deezer, Qobuz, and Youtube do not require authentication.

If you are logged into the Spotify web player, you will not need to authenticate. Authentication tokens are automatically grabbed from cookies. 

However, if this method fails for whatever reason, use the more reliable Spotify authentication method described in the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication#spotify).

### Downloading
Authentication requirements for downloading varies for the sources. The type of account (free vs subscription) may also affect the audio quality available. You do not have re-enter credentials each time because they are stored locally. They can be left alone for months or even longer, but may eventually expire or invalidate due to occasional web-player changes. 

<table>
  <tr>
    <td><strong>Service</strong></td>
    <td><strong>Downloads</strong></td>
  </tr>
  <tr>
    <td>Youtube</td>
    <td>No Authentication Required (128 kbps OPUS, similar to 256kbps MP3)</td>
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

<details>
  <summary><b>Click to learn more about file sound quality</b></summary>
  
  You cannot determine the quality of a file by checking its bitrate. Files can be transcoded (converted), meaning a FLAC or high-bitrate file may have originated from a low-quality source. <a href="https://erikstechcorner.com/2020/09/how-to-check-if-your-flac-files-are-really-lossless/">Here</a> is a guide on using Spek, a spectrogram tool, to verify audio file quality. Spek is bundled with FluentDL.

  #### Additional Notes:
  - As verified through spectrogram, the highest quality YouTube sources use the very efficient OPUS codec. The issue is OPUS containers, such as `.ogg` or `.webm`, have poor metadata support and compatibility. FluentDL transcodes them into a FLAC in order to maintain original quality and support metadata. However, they are NOT actually lossless and is an example of transcoding.
  - There may not be a significant difference between 128 kbps and higher depending on your audio hardware and ear. For example, you may be content with music on Spotify Web or YouTube without subscriptions, which are both low-bitrate. <a href="https://abx.digitalfeed.net/list.lame.html">ABX tests</a> are a good way to test your limits!
  - Downloading directly from Spotify is not supported. Most tools out there download low bitrate MP3s. However, there are a few Python tools that get the true sources (320 kbps Vorbis, 256 kbps AAC). Unfortunately, I could not find .NET equivalents. For FluentDL, use the convert tool to get equivalent Deezer/Qobuz/YouTube tracks, then set the output to Local (download).
</details>



### Retrieving Tokens
Qobuz email and password is self explanatory. Some authentication methods, such as Deezer ARL, Qobuz id/tokens alternative, or Spotify developer app tokens, are not obvious. 

However, tokens are not difficult to obtain: see the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication) for a detailed guide. 
