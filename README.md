<h1 align="center">
    <a href="https://github.com/DerekYang2/FluentDL#gh-dark-mode-only"><img alt="FluentDL" src="./Branding/readme-dark.png" height="44" /></a>
    <a href="https://github.com/DerekYang2/FluentDL#gh-light-mode-only"><img alt="FluentDL" src="./Branding/readme-light.png" height="44" /></a>
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
  <a href="#build">Build</a> •
  <a href="https://github.com/DerekYang2/FluentDL/wiki">Wiki</a>
</p>

<p align="center">
  <img src="./SampleGifs/FluentDL_demo.webp" alt="Sample Webp" />
</p>

## About
FluentDL is a suite of audio tools for Windows. It matches tracks and playlists across Deezer, Qobuz, Spotify, TIDAL and YouTube, downloads from them, and manages your local library.

FluentDL runs entirely on your PC. It has no servers or accounts of its own, sends no usage data, and stores your sign-ins only on your PC. It doesn't host or provide any content.

FluentDL installs from the Microsoft Store, so fixes for service changes arrive as normal updates.

<table>
  <tr>
    <td valign="top">
      <strong>Search</strong>
      <ul>
        <li>Search songs and albums on any of the five online sources</li>
        <li>Paste a track, album, or playlist link to load all of its tracks</li>
        <li>Preview a song or album to listen, download, or check its metadata and available quality</li>
      </ul>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Queue</strong>
      <ul>
        <li>Download tracks and albums from Deezer, Qobuz, TIDAL, or YouTube at the best quality your account allows</li>
        <li>Match tracks between any two sources, such as a Spotify playlist to Qobuz</li>
        <li>Review matches and failures, and retry them with another source</li>
        <li>Upload queued tracks to a new or existing playlist on Deezer, Qobuz, Spotify, or TIDAL</li>
        <li>Queue whole albums, and expand them into tracks</li>
        <li>Inspect a downloaded track's spectrogram</li>
      </ul>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Local</strong>
      <ul>
        <li>Convert between FLAC, MP3, AAC, ALAC, Vorbis, and Opus at a set bitrate or VBR</li>
        <li>Edit tags and cover art</li>
        <li>Check a file's true quality with its spectrogram</li>
        <li>Apply ReplayGain 2.0 loudness tags</li>
        <li>Search, preview, and download lyrics (.lrc)</li>
        <li>View detailed metadata and audio specs</li>
      </ul>
    </td>
  </tr>
</table>

See [usage guide wiki](https://github.com/DerekYang2/FluentDL/wiki/Usage-Guide) to learn more.

## Installation

#### Microsoft Store
<table>
  <tr>
    <td valign="top">
      You can download directly from the Microsoft Store for an easier and efficient update process.
		<br>
      Check out <a href="https://github.com/DerekYang2/FluentDL/releases">Releases</a> to see full changelogs and sideloaded versions.
    </td>
    <td valign="top" style="padding-left: 20px;">
      <a href="https://apps.microsoft.com/detail/9mx44km97x7x?referrer=appbadge&cid=Github&mode=full" target="_blank" rel="noopener noreferrer">
        <img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
      </a>
    </td>
  </tr>
</table>

#### Store CLI (Windows 11 built-in)

Installation: 
```
store install fluentdl
```

Update: 
```
store update fluentdl
```

#### winget (Windows 10)
Installation: 
```
winget install --id 9MX44KM97X7X --source msstore --accept-package-agreements --accept-source-agreements
```

Update: 
```
winget upgrade --id 9MX44KM97X7X --source msstore --exact --include-unknown --accept-package-agreements --accept-source-agreements
```

## Authentication

> [!NOTE]  
> Streaming services occasionally change their APIs, which can break sign-in. Check the [Issues](https://github.com/DerekYang2/FluentDL/issues) page for known problems, and report new ones.

Searching, matching and all local tools work without an account. Some features need you to sign in to a service in Settings:

- Downloads from Deezer, Qobuz and TIDAL. YouTube needs no account. Your account type sets the available quality.
- Private playlists, and uploading playlists.

**Settings > Service Access** shows what works with the accounts you've added. Hover over a cell for details.

See the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication) for how to sign in to each service.

## Build

Only needed for developers who wish to customize source code.

To build and run the project on Visual Studio, see [development wiki](https://github.com/DerekYang2/FluentDL/wiki/Development).

## License

FluentDL is distributed under [GPL-3.0](LICENSE) with its Spek.NET spectrogram component. See the [third-party notices](FluentDL/THIRD-PARTY-NOTICES.md) for the component's source and the retained MIT notice for earlier code.
