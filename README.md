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
  <a href="#build">Build</a> •
  <a href="https://github.com/DerekYang2/FluentDL/wiki">Wiki</a>
</p>

<p align="center">
  <img src="./SampleGifs/FluentDL_demo.webp" alt="Sample Webp" />
</p>

## About
FluentDL is a Fluent UI desktop app for finding, matching, and downloading lossless audio (FLAC) at high speeds. 

It automatically matches tracks across streaming services, gives you fine‑grained control over matches, and includes tools for format conversion, metadata editing, and quality inspection. 

FluentDL supports Deezer, Qobuz, Spotify, and YouTube.

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
        <li>Lookup songs/albums from any of the four online sources</li>
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
      </ul>
    </td>
  </tr>
  <tr>
    <td valign="top">
      <strong>Queue</strong>
      <ul>
        <li>Matching between all possible combinations of online sources (e.g., convert Spotify and YouTube to Deezer equivalents)</li>
		<li>Review matches and failures and find backups from other sources</li>
        <li>Download tracks from Deezer, Qobuz, or Youtube with maximum quality</li>
        <li>Inspect downloaded track spectrogram</li>
      </ul>
    </td>
  </tr>
</table>

See [usage guide wiki](https://github.com/DerekYang2/FluentDL/wiki/Usage-Guide) to learn more.

## Installation
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

## Authentication

> [!NOTE]  
> Streaming services occasionally make changes to APIs, which may result in authentication issues.  Check the [Issues](https://github.com/DerekYang2/FluentDL/issues) page for known problems and feel free to report them. 

The authentication required depends on the sources and features you use. 

### Searching and Converting
For all sources except for Spotify, searches and conversions do not require authentication.

For Spotify, searches require API keys, which come bundled with FluentDL. If the bundled keys are rate limited, you can generate your own as described in the [spotify wiki section](https://github.com/DerekYang2/FluentDL/wiki/Authentication#spotify).

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

## Troubleshooting and logs

Expand **Settings > Diagnostics > Application logs** and select **Open folder**
to view `fluentdl-YYYYMMDD.log` in a text editor. The folder path is displayed
in the **Open log folder** card and can be copied.
Packaged installations store logs in the app's `LocalState\Logs` folder; unpackaged
runs use `%LOCALAPPDATA%\FluentDL\Logs`. After a crash, open that folder directly
or reopen the app and use Settings. Each launch has a session ID, and track
downloads have operation IDs to connect events across asynchronous work.

Important logging is always on: startup, unhandled managed exceptions, download
outcomes, conversion failures, and queue persistence failures. Turn on **Verbose
logging** before reproducing a problem to include extra troubleshooting events,
then turn it off again. This switch takes effect immediately, survives restarts,
and is included in settings import/export and reset. It does not enable logging
of raw HTTP requests or every download progress update.

Logs rotate daily and at 5 MB, retaining the latest 10 files. They are written
without application-level buffering and flushed on normal process exit and
handled crash paths. Native crashes, forced termination, and disk failures can
still prevent the final events from being saved. Windows may continue to create
its own crash event entries; FluentDL no longer writes its own Event Log entries.
Settings shows a warning if file logging encounters a write error.

Nothing is uploaded automatically. Tokens, cookies, response bodies, and raw
exception messages are deliberately excluded from these logs. Exceptions retain
their types, error codes, and method stacks (without source-file paths), including
inner exceptions. Review any log before sharing it.

### Release builds and symbols

No special packaging mode or bundled PDB files are needed for these managed logs:
exception types and method names are available in normal Release builds.
The formatter deliberately requests stacks without file information, so source
paths and line numbers are omitted even when symbols are present. Optimizations
can inline methods, so Release stacks may contain fewer frames than Debug stacks.

Keep the matching binaries and portable PDBs for each released version and
architecture privately if you need to investigate crash dumps. The SDK generates
portable PDBs by default in Debug and Release; `AppxSymbolPackageEnabled` controls
an additional packaging artifact, not whether these log messages work.
Adding source line numbers to the logs later would require both a formatter
change and matching symbols available at runtime. There is no need to ship a
Debug build or disable optimization.

For contributors: use `ILogger<T>` in injected services and Serilog's `Log` in
legacy static code, with constant message templates and safe structured fields.
The important paths above replace `Debug.WriteLine` rather than writing twice.
Unmigrated debug writes elsewhere are still debugger-only, not forwarded blindly
into persistent logs. Avoid logging whole models, settings, request URLs, or
exception messages as message arguments.

Run the isolated logging tests (no live APIs or WinUI window required) from the
`FluentDL` project directory:

```powershell
dotnet test ..\FluentDL.Core.Tests\FluentDL.Core.Tests.csproj
```

## Build

Only needed for developers who wish to customize source code.

To build and run the project on Visual Studio, see [development wiki](https://github.com/DerekYang2/FluentDL/wiki/Development).
