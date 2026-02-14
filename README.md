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
  <a href="https://github.com/derekyang2/fluentdl/releases/latest"><img src="https://img.shields.io/github/v/release/derekyang2/fluentdl?style=for-the-badge" height="60"></a>
	<a href="https://apps.microsoft.com/detail/9mx44km97x7x?referrer=appbadge&cid=Github&mode=full" target="_blank"  rel="noopener noreferrer">
		<sub>
	  <img src="https://get.microsoft.com/images/en-us%20dark.svg" height="35" />
		</sub>
    </a>
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
A Fluent UI desktop application that helps you download lossless songs as FLACs, convert between audio formats, match songs between different online sources, edit song metadata, and more. FluentDL supports Deezer, Qobuz, Spotify, and YouTube.

<table>
  <tr>
    <td valign="top">
      <strong>Search</strong>
      <ul>
        <li>Lookup songs/albums from any of the four online sources</li>
        <li>Parse all tracks from an online link, with track/album/playlist links supported</li>
        <li>Open songs/albums in preview sidebar to listen, download, or view metadata.</li>
      </ul>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Local</strong>
      <ul>
        <li>View file metadata and technical audio specs in-depth</li>
        <li>Edit file metadata live, including option to change cover art</li>
        <li>View file spectrogram using Spek</li>
        <li>Convert between flac, mp3, aac, alac, vorbis, opus with control over bitrate</li>
      </ul>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Queue</strong>
      <ul>
        <li>Matching between all possible combinations of online sources (e.g., convert Spotify and YouTube to Deezer equivalents)</li>
        <li>Download tracks from Deezer, Qobuz, or Youtube with maximum quality</li>
        <li>Inspect downloaded track spectrogram</li>
      </ul>
    </td>
  </tr>
</table>

## Installation
<a href="https://apps.microsoft.com/detail/9mx44km97x7x?referrer=appbadge&cid=Github&mode=full" target="_blank"  rel="noopener noreferrer">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

You can download directly from the [Microsoft Store](https://apps.microsoft.com/detail/9mx44km97x7x?referrer=appbadge&cid=Github&mode=full) for easier updates.

Alternatively, you can sideload an unsigned version from [Releases](https://github.com/DerekYang2/FluentDL/releases).

## Authentication

> [!NOTE]  
> Streaming services occasionally make changes to APIs, which may result in authentication issues.  Check the [Issues](https://github.com/DerekYang2/FluentDL/issues) page for known problems and feel free to report them. 

The authentication required depends on the sources and features you use. 

### Searching and Converting
For all sources except for Spotify, searches and conversions do not require authentication.

For Spotify, searches require API keys, which come bundled with FluentDL. If the bundled keys are rate limited, you can generate your own as described in the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication#spotify).

### Downloading
Authentication requirements for downloading varies for the sources. The type of account (free vs subscription) may also affect the audio quality available. 

You do not have re-enter credentials each time because they are stored locally. They can be left alone for months or even longer, but may eventually expire or invalidate due to occasional web-player changes. 

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
    <td>Not natively available. Use the Queue to convert song to other sources.</td>
  </tr>
</table>

### Retrieving Tokens
See the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication) for a detailed guide. 

## Build

Only relevant for developers who wish to customize source code.

To build and run the project on Visual Studio, see [development wiki](https://github.com/DerekYang2/FluentDL/wiki/Development).
