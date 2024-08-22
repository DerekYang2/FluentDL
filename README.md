## About
A fluent UI desktop application aiming to search/fix metadata of local music files, run custom commands on queues local and online tracks, download songs, and match songs from different sources.
Works with Deezer, Qobuz, Spotify, and Youtube.

#### Search page
Search from any of the four sources, filtered search (advanced search), or paste links from tracks/albums/playlists.
![image](https://github.com/user-attachments/assets/b132d51e-c9fb-4014-8e51-3eb85aac5a5d)

#### Local Explorer
Upload any local files and view/edit metadata or add to queue.
![image](https://github.com/user-attachments/assets/ebd6d1b4-4111-4491-a62d-a2d25c8988fc)
![image](https://github.com/user-attachments/assets/3bf038d4-010e-4416-94d5-58b94ae9927c)

#### Queue
Convert between any of the sources, run custom commands, and more.
![image](https://github.com/user-attachments/assets/5c2edef6-eb44-4901-86db-8f792bb7954e)
![image](https://github.com/user-attachments/assets/984e29a1-c29f-4896-868c-552e3e3960b9)


## Setup

This project uses [WinUI 3](https://github.com/microsoft/microsoft-ui-xaml) and [TemplateStudio](https://github.com/microsoft/TemplateStudio). In order to run these project in Visual Studio, you must have WinUI 3 working (Template Studio is not required).

WinUI 3 can be automatically configured using the Visual Studio Installer or manual installation. See the [official documentation](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/create-your-first-winui3-app) for full details.

All dependencies should be automatically handled by Visual Studio and can be found on NuGet. There is one package [MarqueeText](https://dev.azure.com/dotnet/CommunityToolkit/_artifacts/feed/CommunityToolkit-Labs/NuGet/CommunityToolkit.Labs.WinUI.MarqueeText) that has to be installed manually.

## Running

This project is deployed using MSIX, which installs the application on Windows. To install this application, download the folder from releases and open the `.msix` file. This will open a prompt that installs the application and all its dependencies.

However, currently the certificate is not verified and must be trusted by the user in order to install from the `.msix` prompt. 

In order to trust the certificate, click and open the `.cer` certificate file in the folder. You will see a security warning prompt ("Do you want to open this file?") and press open. Next, the certificate pop-up will explain how to add the certificate to "Trusted Root Certification Authorities". In order to this, follow these steps:

- Press `Install Certificate...`, which should open "Certificate Import Wizard"
- Select Current User or Local Machine, either should work
- Select `Place all certificates in the following store`
- Press browse and select the second option, `Trusted Root Certification Authorities`

After adding the certificate to this storage, opening the `.cer` certificate file should show that it is now trusted and the MSIX install button should not be greyed out anymore.
