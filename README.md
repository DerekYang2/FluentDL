<h1 align="center">
  FluentDL
</h1>
<p align="center">
  <a href="#about">About</a> •
  <a href="#build">Build</a> •
  <a href="#easy-installation">Installation and Running</a> •
  <a href="#manual-installation">Manual Installation</a> •
   <a href="#authentication">Authentication</a>
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
        <li>Download tracks from Deezer, Qobuz, or Youtube</li>
        <li>Download entire queue using Convert: select Local as output</li>
      </ul>
    </td>
    <td>
      <p align="center"><img src="./SampleGifs/queue_page.webp" alt="Queue Webp"/></p>
    </td>
  </tr>
</table>

TIP: change the number of threads in settings for significantly faster conversions, matching, and downloading.

## Build

NOTE: Currently not working, Core is missing.

A pre-built FFmpeg executable can be found in [./Assets/ffmpeg/bin](https://github.com/DerekYang2/FluentDL/tree/master/Assets/ffmpeg/bin) and contains many additional codecs, such as libopus and libvorbis. You may use your own FFmpeg binaries, but note that libopus is required for Youtube's highest quality source.

## Installation and Running
This project is deployed using MSIX, which installs the application on Windows. To install this application, download the first zip from [Releases](https://github.com/DerekYang2/FluentDL/releases).

### Easy Installation
The application may be installed directly using a powershell script located in the zip folder. In the root folder inside of the zip, you should see an `Install.ps1` file and other files such as `FluentDL_{VERSION}_x64_MSIX.msix`. Open Powershell or CMD into this directory and run the command:

```powershell.exe -executionpolicy unrestricted .\Install.ps1```

The application is now installed, and you should be able to find "FluentDL" with Search or in your Apps list.

### Manual Installation
Running the `FluentDL_{VERSION}_x64_MSIX.msix` file will open the Microsoft Store installer prompt that handles the installation and all dependencies (including the FFmpeg executable). This installation process requires an extra step because the certificate is currently self-signed. It must be trusted by the user before running the MSIX installer, otherwise the install button is greyed-out.

In order to trust the certificate, click and open the `FluentDL_Certificate.cer` certificate file in the folder. 

You will see a security warning prompt ("Do you want to open this file?") and press open. 

Next, the certificate pop-up will explain how to add the certificate to "Trusted Root Certification Authorities". In order to this, follow these steps:

- Press `Install Certificate...`, which should open "Certificate Import Wizard"
- Choose  `Local Machine` then select `Next`
- Choose `Place all certificates in the following store` then press `Browse...`, which should open a new dialog
- In the new "Select Certificate Store" dialog, select the second option `Trusted Root Certification Authorities` and press `OK`
- Select `Next` and then `Finish`. You should see a dialog that says: The import was successful.

After adding the certificate to this storage, `FluentDL_Certificate.cer` should be trusted and you may now run `FluentDL_{VERSION}_x64_MSIX.msix`.

## Authentication

Before using the application, head over to the settings page through the sidebar. 

Searching songs does not require authentication, however Spotify is the only exception. Spotify will require API tokens (a client ID and client secret) which can be entered in the settings page. For more details on obtaining these tokens, visit the [official documentation](https://developer.spotify.com/documentation/web-api/tutorials/getting-started).

Downloading from Youtube does not require authentication.
Downloading from Deezer and Qobuz require authenticated through ARLs and Tokens respectively. 
You do not have re-enter credentials each time because they are stored locally. Note that tokens expire or may break due to occasional web-player changes. 

Deezer and Qobuz FLAC sources are only accessible with premium accounts. [Here](https://erikstechcorner.com/2020/09/how-to-check-if-your-flac-files-are-really-lossless/) is a guide on using Spek to verify your file quality.

### Retrieving Tokens
If you already have them, enter them in settings. Otherwise:

In order to obtain your Deezer ARL, log into [https://www.deezer.com/](https://www.deezer.com/). Then open Developer Tools, and head to the `Application` tab. In the sidebar, open the dropdown list for `Cookies` and there should be an subitem `https://www.deezer.com/`. Click on the subitem and to find the the `arl` value, which should be 192 characters long. Note that you should open the _dropdown_ for the `Cookies` section, not click on it.

Similarly, to obtain a Qobuz id and token, log into [https://play.qobuz.com/](https://play.qobuz.com/) and open Developer Tools. Head over the `Application` tab, open the dropdown list for `Local Storage` and click on the subitem `https://play.qobuz.com`. You should then click on the `localuser` JSON object in the viewing window, where you can find the fields `id` (7 digits) and `token` (86 characters). 
