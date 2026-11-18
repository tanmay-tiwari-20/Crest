# Building and packaging Crest

The finished Windows x64 distribution includes the .NET runtime. Only maintainers need the SDK and packaging compiler.

Run `powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Package` from the source project. The script uses `.tools/dotnet/dotnet.exe` when present, otherwise the system .NET 10 SDK. It restores dependencies, compiles the WPF application, runs the console verification suite, publishes a self-contained x64 application, and compiles its installer.

Outputs are `artifacts/portable/Crest/` and `artifacts/Crest-Setup-0.1.0-x64.exe`, with a SHA-256 checksum beside the installer. `scripts/package.ps1` packages an already-published folder. Builds default to Release. `-SkipTests` is intended only for maintenance diagnostics, not release qualification.

The build targets `net10.0-windows10.0.19041.0`. Its direct NuGet dependency is `System.Management` 10.0.0; Windows SDK and runtime references are resolved through the framework target. The project SDK used for this build is .NET 10.0.404. Publication uses `win-x64`, self-contained runtime files, and ReadyToRun compilation.

Packaging includes the local .NET license and third-party notices when available, plus a dependency record extracted from the restored package graph without machine-specific paths.

The packaging compiler is official Inno Setup 6.7.4. If missing, the package script downloads it from the official `jrsoftware/issrc` GitHub release, verifies its Authenticode publisher, and extracts it in official portable mode under `.tools/innosetup/`. Portable mode does not register the compiler, associate `.iss` files, create shortcuts, or add an uninstaller. The compiler license remains in that tools folder.

The Crest installer runs per user without elevation and requires Windows 11 x64. It installs in the user's local Programs folder, adds Start-menu shortcuts, and offers an unchecked desktop shortcut. It creates no startup entry; the user can enable startup in Crest Settings. Upgrades reuse the install directory and preserve local configuration. Setup and uninstall signal the app's named quit event and wait for its single-instance mutex to disappear; a process that cannot quit is reported without being force-killed.

Uninstall removes executable components and the optional startup entry. Interactive uninstall offers deletion of local Crest data with retention as the default. Silent uninstall retains data. Shelf originals are references outside that local data folder and are never removed by uninstall.

The installer is not code-signed by this project. Production signing requires the owner's publisher certificate and should be added to the release pipeline when available.
