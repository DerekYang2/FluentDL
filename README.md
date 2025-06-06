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
<a href="#authentication">Authentication</a> •
  <a href="#build">Build</a>
</p>

<p align="center">
  <img src="./SampleGifs/FluentDL_demo.webp" alt="Sample Webp" />
</p>

## About
A Fluent UI desktop application that helps you download lossless songs as FLACs, convert between audio formats, match songs between different online sources, edit song metadata, and more. Under the hood, the app uses FFmpeg and APIs for Deezer, Qobuz, Spotify, and YouTube.

FluentDL is organized into three sections: Search, Local Explorer, and Queue.

<table>
  <tr>
    <td valign="top">
      <strong>Search</strong>
      <ul>
        <li>Lookup songs from any of the four online sources</li>
        <li>Search using natural language or strict search by title/artist/album</li>
        <li>Parse all tracks from an online link, with track/album/playlist links supported</li>
        <li>Open songs in preview sidebar to listen, download, or view metadata.</li>
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
        <li>Edit file metadata live, including option to change cover art</li>
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
To install the app, download the first zip from [Releases](https://github.com/DerekYang2/FluentDL/releases).

If you are updating FluentDL, directly run `FluentDL_{VERSION}_x64_MSIX.msix` **or** follow one of the options below.

If this is your **first** installation, you **must** follow one of the options below.

Before starting, open the Windows Settings app and enable developer mode: `Settings > For developers > Developer Mode`.

### Option 1
This method works if running scripts is enabled: `Settings > For developers > PowerShell`. 

In the root folder inside of the zip, you should see an `Install.ps1` file and other files such as `FluentDL_{VERSION}_x64_MSIX.msix`. 

Right click on `Install.ps1` (in File Explorer) and press `Run with PowerShell`.

### Option 2

This method works regardless if scripts is enabled. 

If you open PowerShell into the root directory, run the command:

```powershell.exe -executionpolicy unrestricted .\Install.ps1```

Otherwise, run the command replacing `PATH` with the full path to the install script:

```powershell.exe -executionpolicy unrestricted PATH```

The application is now installed, and you should be able to find "FluentDL" with Search or in your Apps list.

<details>
  <summary><b>What does the PowerShell script do?</b></summary>
    
Ideally, you would only need to run `FluentDL_{VERSION}_x64_MSIX.msix`, which opens the official Microsoft Store installer interface. However, the certificate is self-signed because ones from certificate authorities can cost hundreds of dollars per year. The PowerShell script trusts the self-signed certificate on your machine and then runs the MSIX. The <a href="https://superuser.com/questions/463081/adding-self-signed-certificate-to-trusted-root-certificate-store-using-command-l">manual way</a> of trusting a certificate is more work. This is also why if you have already run the script (trusted the certificate), you can directly run the MSIX in the future. 
</details>

## Authentication

> [!NOTE]  
> Streaming services occasionally make changes to APIs, which may result in authentication issues. Double check the [Issues](https://github.com/DerekYang2/FluentDL/issues) page for ongoing problems. 

The authentication required depends on the sources and features you use. 

### Searching and Converting
Searches and conversions do not require authentication.

If logged into the Spotify Web Player in your default browser, FluentDL will automatically authenticate from cookies.

Otherwise, the bundled API keys will be used. 

If the bundled keys are rate limited, generate your own as described in the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication#spotify).

### Downloading
Authentication requirements for downloading varies for the sources. The type of account (free vs subscription) may also affect the audio quality available. You do not have re-enter credentials each time because they are stored locally. They can be left alone for months or even longer, but may eventually expire or invalidate due to occasional web-player changes. 

<table>
  <tr>
    <td><strong>Service</strong></td>
    <td><strong>Downloads</strong></td>
  </tr>
  <tr>
    <td>Youtube</td>
    <td>No Authentication Required (128 kbps OPUS, similar to 192kbps MP3)</td>
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

### Retrieving Tokens
Qobuz email and password is self explanatory. Some authentication methods, such as Deezer ARL, Qobuz id/tokens alternative, or Spotify developer app tokens, are not obvious. 

However, tokens are not difficult to obtain: see the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication) for a detailed guide. 

## Build

Only relevant for developers who wish to customize source code.

To build and run the project on Visual Studio, see [development wiki](https://github.com/DerekYang2/FluentDL/wiki/Development).
