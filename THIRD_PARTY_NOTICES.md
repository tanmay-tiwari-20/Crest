# Third-party notices and artwork

## Crest code and original assets

Original Crest source code and documentation are available under the [MIT license](LICENSE), copyright 2026 Tanmay Tiwari. The Crest icon, current focus character, and synthesized timer chime are original project assets and use the same license.

`assets/focus-orb.svg` is the editable source for the original blue focus character; `assets/focus-orb.png` is its transparent raster export. The character uses original vector geometry, gradients, and eye/face details. It contains no crop from the earlier visual references.

`assets/timer-complete.wav` is Crest's original 3.54-second timer chime: mono 16-bit PCM at 44.1 kHz, synthesized from mathematical oscillators with fades and silence. It contains no third-party recordings, music, or sampled audio. The bundled recording uses the project's MIT license.

The initial prototype used a character cropped from a user-provided reference image. That crop was replaced for public distribution. Earlier Git history and historical test reports may refer to that prototype; the project does not claim ownership of third-party reference material or extend its MIT grant to such material.

## .NET and Windows dependencies

The self-contained Windows packages include the Microsoft .NET and Windows Desktop runtimes, the `System.Management` NuGet dependency, and Windows SDK projections (`Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`). These components retain their respective licenses and notices. The package includes a `licenses` directory with licenses from the restored runtime packages, the supplied third-party notices, a System.Management MIT attribution, the Windows SDK license terms, and a dependency/version record.

- [.NET runtime license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)
- [.NET runtime third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)
- [Windows Desktop runtime license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)
- [System.Management package](https://www.nuget.org/packages/System.Management/10.0.0)
- [Windows SDK license terms](https://aka.ms/WinSDKLicenseURL)

The Crest MIT grant covers the project's original work. It does not relicense the Windows SDK or other bundled components.

Windows APIs, system fonts, and hardware capabilities are provided by the user's Windows installation. The project does not redistribute Windows system fonts.

## Installer build tool

Inno Setup is used to create the Windows installer. Its compiler and tool files are build dependencies, kept outside Git under `.tools`; the compiler is not included in the application packages. Its own license remains with the build tool.

- [Inno Setup license](https://jrsoftware.org/files/is/license.txt)

## Optional services and installed applications

The opt-in weather module uses Open-Meteo's city search and weather APIs. Weather data is attributed to **Open-Meteo**, under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/); the panel displays this attribution. API access is subject to the provider's [terms](https://open-meteo.com/en/terms), including the free non-commercial service limits. No Open-Meteo server code is bundled in Crest.

Read-only AI assistance connects to an existing local [Ollama](https://ollama.com/) installation and an explicitly selected model. Crest does not distribute Ollama or model weights. Those installations and models retain their own licenses and resource requirements.

Microsoft Phone Link, Windows capture tools, and the selected meeting provider's browser page are existing applications or services opened through Windows. The classic Outlook adapter reads the default calendar from a running installation using the Outlook Object Model. Online calendar subscriptions use the URL supplied by the user. Crest ships its own iCalendar reader for feeds and local files. Microsoft Outlook and calendar-provider code are distributed separately.

## Development-only tools

Image rendering tools used to export project artwork are not required to run Crest and are not bundled in the application. GitHub Actions referenced by the workflows retain their own licenses. Package checksums describe specific built files; they do not change dependency licensing.
