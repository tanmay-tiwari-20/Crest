# Using Crest 0.1.0

Crest opens a top-center island on an eligible Windows desktop by default. You can opt in to opening it over other apps. This guide covers the **0.1.0 release candidate** for Windows 11 x64. The [test report](TEST_REPORT.md) records verified behavior and the native acceptance checks still pending.

## Install

Get an application package from [GitHub Releases](https://github.com/tanmay-tiwari-20/Crest/releases). Expand the release's **Assets** list. GitHub's source-code ZIP and tarball contain source files; choose an installer or portable application package to run Crest.

Both packages include the .NET runtime. You do not need a separate runtime, an SDK, administrator access, or a Crest account. Windows 10 and ARM64 packages are not available. The current installer is unsigned.

### Installer

1. Download `Crest-Setup-0.1.0-x64.exe`.
2. Run it and follow the setup steps. Crest installs for your current Windows user account.
3. Choose an optional desktop shortcut if wanted. Setup adds Start-menu entries.
4. Start Crest from the Start menu or the final setup step.

Startup with Windows is off by default. You can enable it later in **Settings → Behavior**.

### Portable package

1. Download `Crest-Portable-0.1.0-x64.zip`.
2. Extract the entire archive into a folder you want to keep.
3. Run `Crest.exe` from the extracted folder.

Keep the executable, runtime, and supporting files together. Portable Crest still stores preferences in `%LOCALAPPDATA%\Crest`. If you move its folder after enabling startup, disable startup first and enable it again from the new location.

When a release supplies a SHA-256 checksum, compare it with your downloaded package:

```powershell
Get-FileHash .\Crest-Setup-0.1.0-x64.exe -Algorithm SHA256
```

Use the checksum for the same asset and release. The [build guide](BUILD.md) explains how the packages are produced.

## Open Settings and use the tray

Crest normally starts quietly in the Windows system tray. Its icon may be in the tray's hidden-icons menu.

| Action | How |
|---|---|
| Open Settings | Choose **Crest Settings** from Start, double-click the tray icon, or right-click it and choose **Settings**. |
| Pause Crest | Right-click the tray icon and choose **Pause**. Hover and keyboard reveal are disabled. |
| Resume Crest | Choose **Resume** in the tray menu, then enter the trigger afresh. |
| Quit | Choose **Quit** in the tray menu. The file shelf clears and native subscriptions are released. |

Closing Settings keeps Crest running. Starting Crest again reuses its existing instance; opening Settings restores its existing window, including a minimized window.

## Reveal and dismiss the island

1. Close Settings.
2. Press **Win+D** to show the wallpaper desktop. Minimize or close any remaining ordinary app windows on the selected display.
3. Move the pointer away from the top edge.
4. Enter the top-center edge and pause for about 120 milliseconds.
5. Click the chevron to open the horizontal activity deck. The focus, music, and battery cards show their current local state together.
6. Select an icon below the deck to open Music, Controls, Timer, Launch, or Shelf. The overview icon returns to the activity cards; album artwork also opens Music.
7. Move away to dismiss after the pointer grace period. In desktop-only mode, opening another app suppresses Crest.

The default centered trigger is **240 × 3 device-independent pixels**, which scale with Windows display scaling. Settings can change the delay, width, depth, and whether the trigger spans the full top edge.

A pointer already at the edge does not reveal Crest after Win+D, unlock, resume, or tray Resume. A fresh exit and entry is required. Timer completion and music activity wait for your next reveal.

### Desktop eligibility

The default **strict desktop mode** requires the actual wallpaper desktop to be foreground. It also requires ordinary visible app windows intersecting the selected display to be minimized. A foreground app on any monitor suppresses Crest.

File Explorer, Start, Search, Task View, and other shell panels are not eligible wallpaper-desktop states. In desktop-only mode, an active File Explorer window cannot summon Crest for a drag.

Turning off **Use strict desktop mode** in Behavior selects desktop-active mode. This permits visible app windows when the actual wallpaper desktop is foreground, such as after clicking an empty desktop area. Foreground apps and shell panels still suppress Crest.

In desktop-only mode, if Crest cannot establish desktop, display/DPI, or virtual-desktop eligibility, it stays hidden. Check the selected display if a monitor is connected, disconnected, or rearranged.

### Open Crest over other apps

Enable **Settings → Behavior → Allow Crest over other apps** to reveal Crest while using an app such as a browser or File Explorer. Close Settings, move away from the top edge, then enter the top-center trigger on your selected display. The usual dwell and pointer grace still apply.

This option is off by default and overrides strict/desktop-active eligibility while enabled, including when Windows panels are active on the normal desktop. Your strict-mode preference is kept for when you turn the option off. Changing access mode hides Crest and waits for a fresh trigger entry. Settings and tray Pause still hide Crest; lock, suspend, and secure desktop also disable reveal. Background music and timer events never open it automatically.

### Keyboard access

Enable **Settings → Behavior → Enable the accessibility reveal shortcut** to use **Ctrl+Alt+Space**. This is off by default and checks the same access mode as hover: eligible desktop by default, or another app when **Allow Crest over other apps** is enabled. It focuses the compact controls so keyboard navigation can begin.

**Tab** and **Shift+Tab** move between controls, **Enter** or **Space** activate buttons, and **Escape** dismisses the island. Text input focuses Crest when needed. Appearance provides a reduced-motion option and a follow-system motion preference. Full keyboard, screen-reader, high-contrast, and reduced-motion acceptance is still listed in the [test report](TEST_REPORT.md).

## Music

Music reads compatible media sessions published by Windows applications. It shows available title, artist, album artwork, playback state, and position. It enables only the playback and seek actions the player supports.

When several sessions are available, use the preferred-player selector to choose one. A player that stops publishing its session can disappear from the list. If there is no compatible session, Crest displays an empty state and disables playback actions.

To use Music, start playback in a compatible player and reveal Crest again. Crest does not sign in to streaming services or search for media.

## System controls

Controls adjusts the default Windows audio output's volume and mute state. The displayed value follows changes reported by Windows, including changes outside Crest.

Battery percentage, charging/power state, and remaining-time estimates come from Windows. An estimate appears only when Windows supplies one. Desktops without a battery and devices without a reported estimate show the appropriate unavailable state.

Built-in brightness uses a supported Windows/WMI capability. An external monitor or unsupported laptop interface may not expose this control. Missing hardware controls do not prevent the timer, shortcuts, or other supported modules from working.

## Timers and stopwatch (F09)

The focus card starts a **25-minute** timer when none exists. It then controls the selected timer with Pause, Resume, or Done. Select the card's time to open **Timers and stopwatch**. These F09 tools are included in the current source candidate; earlier published builds have one timer.

The Timers tab offers **5, 15, and 25 minute** presets. Each adds a new timer. For a custom timer, enter an optional name and a duration in minutes, then select **Add**. The range is one second to seven days (10,080 minutes); fractional minutes are accepted using your Windows number format.

Crest keeps up to **eight independent timers**. Select a timer name to make it the focus card's timer. Each row has its own Pause/Resume, Done, and cancel controls. Completion remains until you acknowledge or cancel that timer. If all eight slots are occupied, remove one to add another. Scroll to reach additional rows.

The countdown continues while Crest is hidden and includes time spent asleep. System-clock changes do not alter a live countdown. After an application restart, a running timer recovers using its saved UTC deadline; a paused timer retains its remaining duration. Timer sound is off by default and can be enabled in **Settings → Modules**. Completion never reveals the island automatically.

The **Stopwatch** tab provides Start, Pause/Resume, Lap, and Reset. Running elapsed time includes time hidden and asleep; paused time is excluded. Laps show the split and total, newest first. Up to **100 laps** are retained; recording stops at the cap until Reset clears them. Elapsed time and laps survive restarts. Restart recovery adds nonnegative UTC downtime, while live operation uses monotonic time. Reset clears the stopwatch and its saved laps. Hiding the Timers module hides these controls without stopping active tools.

Focus sessions and local reminders are later F09 work. Productivity events never reveal Crest automatically, including when access over other apps is enabled.

## Launch shortcuts

Open **Settings → Shortcuts** to add an app, file, Windows shortcut, folder, or website. Give the entry a recognizable name and choose its target. Website entries accept HTTP or HTTPS URLs and open in your default browser.

The side rail shows the first two entries. The Launch panel contains the complete list. Crest stores only entries you add; it does not discover your apps or folders automatically.

Targets open through Windows. If a file moves or a target becomes invalid, Crest reports the problem. Remove the old entry in Settings and add the current target. Removing a launch entry leaves its target untouched.

## File shelf

The shelf holds up to **20 references for the current Crest session**.

1. Start a file drag from the wallpaper desktop while it is eligible.
2. Enter the top-center trigger and pause to reveal Crest.
3. Drop the file into the island's Shelf panel.
4. Select a reference to open it, drag it to a destination, or use its remove button to remove the reference.

Crest does not silently copy, move, or delete source files. The destination handles a file dragged out through Windows. Removing a shelf reference does not delete its original. A moved or deleted source is marked missing.

Quitting Crest clears the shelf. It is not saved across restarts. In desktop-only mode, a drag from an active File Explorer window cannot reveal Crest; real desktop drag and drag-out behavior remains a native acceptance gate for this candidate.

## Settings

| Page | Options |
|---|---|
| Overview | Application overview and getting-started information. |
| Appearance | System/light/dark theme, motion preference, island size, and motion speed. |
| Behavior | Strict desktop mode, optional access over other apps, selected display, trigger coverage and dimensions, hover delay, pointer grace, startup, and keyboard reveal. |
| Modules | Choose visible modules and opt into timer-completion sound. |
| Shortcuts | Add and remove explicit launch entries. |
| About | Local-data information, reset, tray instructions, and release-verification information. |

## Data, upgrades, and removal

Preferences, shortcuts, and saved timer and stopwatch state are stored in `%LOCALAPPDATA%\Crest`. The shelf is held only in memory. Crest has no analytics, account access, clipboard collection, notification access, or online account integrations. Core features do not require internet; a website or launched application may require its own connection.

### Reset local data

Choose **Settings → About → Reset Crest data**. Crest asks before resetting preferences and shortcuts, cancelling the timer, clearing shelf references, and disabling startup. Original files are untouched.

### Upgrade

Download the new package from [GitHub Releases](https://github.com/tanmay-tiwari-20/Crest/releases). There is no automatic updater.

For an installed copy, run the newer installer. It requests a graceful quit from a running Crest instance, reuses the installation location, and preserves local configuration.

For portable use, choose **Quit**, extract the new package, and run its `Crest.exe`. Keep all files from a package together. The existing user-profile preferences are reused. Re-enable startup from the new location if you moved the portable folder.

### Uninstall

For an installed copy, open Windows **Settings → Apps → Installed apps**, find Crest, and choose **Uninstall**. Interactive uninstall offers a choice to keep or delete local Crest data; keeping it is the default. Silent uninstall retains local data.

For a portable copy, choose **Quit** and remove the extracted application folder. To clear saved preferences too, use **Reset Crest data** before quitting. Original shelf and shortcut targets are not removed by either method.

## Troubleshooting

### Crest does not appear

Check these in order:

1. Confirm Crest is running in the tray and is not paused.
2. Close Settings and any Windows shell panels.
3. Press Win+D and minimize or close ordinary visible apps on the selected display.
4. Move away from the top edge, then enter the top-center trigger afresh.
5. Check **Settings → Behavior** for the selected display, trigger coverage, dimensions, and hover delay.

A helper window can count as a visible app even when the desktop appears empty. A confirmed case involved a WhatsApp WebView window: closing that window restored strict-desktop hover. Close or minimize the relevant app normally and try again.

Do not expect hover over an active app or File Explorer to reveal Crest. The optional keyboard shortcut follows the same eligibility rules.

### Music is empty or seeking is disabled

Start playback in a player that exposes a Windows media session. Some players publish metadata without seek support; Crest disables unsupported actions. Check the preferred-player selector if several sessions are listed.

### Volume, brightness, or battery information is unavailable

Check that Windows has a usable default audio endpoint. Brightness requires a supported built-in interface. Battery estimates appear only when supplied by Windows. These limits can occur independently, while other Crest modules remain available.

### A shortcut or shelf reference is missing

Check whether the original target was moved, renamed, deleted, or disconnected. Re-add a launch entry with its current target. Shelf references are session-only; they clear when you quit Crest.

### Settings appears minimized or Crest is already running

Double-click the tray icon or choose **Crest Settings** from Start to restore the existing Settings window. Crest runs as one instance. Use the tray's **Quit** before running a separate development build or replacing a portable folder.

### Report a problem

Open an issue at [GitHub Issues](https://github.com/tanmay-tiwari-20/Crest/issues). Include the Crest version, Windows version, monitor count, display scaling, visibility mode, relevant app or hardware, and exact steps to reproduce. Note whether a fresh clear-desktop hover works after closing other apps.

The [test report](TEST_REPORT.md) distinguishes completed checks from remaining acceptance. Developers can use the [build guide](BUILD.md), [PRD analysis](PRD_ANALYSIS.md), [release notes](RELEASE_NOTES.md), and [repository README](../README.md) for implementation context.
