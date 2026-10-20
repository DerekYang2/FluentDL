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
FluentDL is a Fluent UI desktop app for finding, matching, and downloading lossless audio (FLAC) at high speeds. 

It automatically matches tracks across streaming services, gives you fine‑grained control over matches, and includes format conversion, lyrics, loudness normalization, metadata editing, and quality inspection. 

FluentDL supports Deezer, Qobuz, Spotify, TIDAL, and YouTube.

#### Why FluentDL?
If you have your own credentials and don't need a website downloader:
- Performance: native desktop downloader for fast, multi‑threaded saving.
- Control: review matches, edit metadata, and inspect audio quality with spectrograms.
- All‑in‑one workflow: search, preview, match, download, convert, and tag in one app.

<table>
  <tr>
    <td valign="top">
      <strong>Search</strong>
      <ul>
        <li>Look up songs and albums across the five online sources</li>
        <li>Parse all tracks from an online link, with track/album/playlist links supported</li>
        <li>Open songs/albums in preview sidebar to listen, download, or view metadata and available sample rates/bit depths</li>
      </ul>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Local</strong>
      <ul>
        <li>View file metadata and technical audio specs in-depth</li>
        <li>Edit file metadata, including option to change cover art</li>
        <li>View file spectrogram to determine true lossless/quality</li>
        <li>Convert between flac, mp3, aac, alac, vorbis, opus with specific bitrate or VBR</li>
		<li>Run ReplayGain 2.0 with various settings</li>
		<li>Search, preview, and download lyrics files (.lrc)</li>
      </ul>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Queue</strong>
      <ul>
        <li>Matching between all possible combinations of online sources (e.g., convert Spotify and YouTube to Deezer equivalents)</li>
		<li>Review matches and failures and find backups from other sources</li>
        <li>Download tracks from Deezer, Qobuz, TIDAL, or YouTube at the quality available to your account</li>
        <li>Create playlists or append queued tracks to owned playlists on Deezer, Qobuz, Spotify, and TIDAL</li>
        <li>Inspect downloaded track spectrogram</li>
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

### Expanding queued albums

Album rows have an Expand album into tracks button in both queue layouts. The compact layout also has this action in its right-click menu. Confirming replaces the album with its individual tracks without downloading audio. New rows keep the album's position and track order; tracks already queued stay in their existing positions and are skipped.

The album stays unchanged if the source cannot return its complete track list or the saved replacement fails. Finish active tasks before committing the expansion. This is a one-way replacement, not a collapsible album group.

### Updating from FluentDL

Open Settings > About > FluentDL > App updates to check for an update, download it and choose when to install. Store installations use Microsoft Store updates. GitHub installations use the official release for the installed architecture and require administrator permission on Windows 11. Development builds do not update themselves.

Installation is blocked while work is active. Finish that work, then choose Install update again. FluentDL saves a queue backup before Windows can close the app. It does not resume an interrupted job or bypass Store availability. Automatic update notifications do not install updates automatically.

## Authentication

> [!NOTE]  
> Streaming services occasionally make changes to APIs, which may result in authentication issues.  Check the [Issues](https://github.com/DerekYang2/FluentDL/issues) page for known problems and feel free to report them. 

The authentication required depends on the sources and features you use. 

### Searching and converting
Open Settings > Authentication to configure each service. The Service Access card shows the search, playlist and download access available with the configured accounts and credentials.

Spotify catalog access can use the anonymous web player or your own developer credentials. TIDAL can use its signed-in account or catalog developer credentials, including the bundled credentials in official releases. Catalog credentials alone cannot write user playlists.

### Playlist uploads
Queue > Upload creates a playlist or appends individual tracks to an owned playlist on the selected service. It skips duplicate IDs, excludes album rows and preserves the existing playlist's entries and metadata. It does not convert tracks or upload local audio files.

Sign in through Settings > Authentication before uploading. Spotify requires saved developer credentials and explicit account authorization; its refresh token is encrypted for the Windows user so sign-in can survive app restarts. See the [Spotify setup notes](tools/live-tests/Spotify-playlists.md) for the callback URI and Development Mode account allowlist requirements. TIDAL reuses the account used for full downloads. Deezer and Qobuz reuse their configured account sign-ins.

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
  <tr>
    <td>TIDAL</td>
    <td>Direct downloads require account sign-in and a subscription or trial with full-track access. Availability and quality depend on the account and track.</td>
  </tr>
</table>

### Retrieving Tokens
See the [authentication wiki](https://github.com/DerekYang2/FluentDL/wiki/Authentication) for a detailed guide. 

## Build

Only needed for developers who wish to customize source code.

To build and run the project on Visual Studio, see [development wiki](https://github.com/DerekYang2/FluentDL/wiki/Development).

## License

FluentDL is distributed under [GPL-3.0](LICENSE) with its Spek.NET spectrogram component. See the [third-party notices](FluentDL/THIRD-PARTY-NOTICES.md) for the component's source and the retained MIT notice for earlier code.
