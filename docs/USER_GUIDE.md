# Using Crest 0.2.1

Crest opens a top-center island on an eligible Windows desktop by default. You can opt in to opening it over other apps. This guide covers the **0.2.1 release candidate** for Windows 11 x64. The [test report](TEST_REPORT.md) records verified behavior and the native acceptance checks still pending.

## Install

Get an application package from [GitHub Releases](https://github.com/tanmay-tiwari-20/Crest/releases). Expand the release's **Assets** list. GitHub's source-code ZIP and tarball contain source files; choose an installer or portable application package to run Crest.

Both packages include the .NET runtime. You do not need a separate runtime, an SDK, administrator access, or a Crest account. Windows 10 and ARM64 packages are not available. The current installer is unsigned.

### Installer

1. Download `Crest-Setup-0.2.1-x64.exe`.
2. Run it and follow the setup steps. Crest installs for your current Windows user account.
3. Choose an optional desktop shortcut if wanted. Setup adds Start-menu entries.
4. Start Crest from the Start menu or the final setup step.

Startup with Windows is off by default. You can enable it later in **Settings → Behavior**.

### Portable package

1. Download `Crest-Portable-0.2.1-x64.zip`.
2. Extract the entire archive into a folder you want to keep.
3. Run `Crest.exe` from the extracted folder.

Keep the executable, runtime, and supporting files together. Portable Crest still stores preferences in `%LOCALAPPDATA%\Crest`. If you move its folder after enabling startup, disable startup first and enable it again from the new location.

When a release supplies a SHA-256 checksum, compare it with your downloaded package:

```powershell
Get-FileHash .\Crest-Setup-0.2.1-x64.exe -Algorithm SHA256
```

Use the checksum for the same asset and release. The [build guide](BUILD.md) explains how the packages are produced.

## Open Settings and use the tray

Crest normally starts quietly in the Windows system tray. Its icon may be in the tray's hidden-icons menu.

| Action | How |
|---|---|
| Open Settings | Choose **Crest Settings** from Start, double-click the tray icon, or right-click it and choose **Settings**. |
| Pause Crest | Right-click the tray icon and choose **Pause**. Hover and keyboard reveal are disabled. |
| Resume Crest | Choose **Resume** in the tray menu, then enter the trigger afresh. |
| Quit | Choose **Quit** in the tray menu. Unpinned shelf references clear, pinned references remain saved, and native subscriptions are released. |

Closing Settings keeps Crest running. Starting Crest again reuses its existing instance; opening Settings restores its existing window, including a minimized window.

## Reveal and dismiss the island

1. Close Settings.
2. Press **Win+D** to show the wallpaper desktop. Minimize or close any remaining ordinary app windows on the selected display.
3. Move the pointer away from the top edge.
4. Enter the top-center edge and pause for about 120 milliseconds.
5. Click the chevron to open the horizontal activity deck. The focus, music, and battery cards show their current local state together.
6. Select an icon below the deck to open Music, Controls, Timer, Launch, Shelf, Capture, Workspaces, or Clipboard. Settings → Modules controls visibility and icon order. The overview icon returns to the activity cards; album artwork also opens Music.
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

Controls adjusts the default Windows audio output's volume and mute state. The displayed value follows changes reported by Windows, including changes outside Crest. Select a connected device under **Audio output**, then choose **Switch audio output**. **Refresh audio outputs** reconnects the list after a device change. If Windows cannot switch directly, choose **Windows sound settings** and select the device there.

Direct switching uses Windows PolicyConfig COM and verifies the resulting default. PolicyConfig is undocumented and may be unsupported on a Windows build. Crest reports failure and keeps the Windows settings fallback available.

Battery percentage, charging/power state, and remaining-time estimates come from Windows. An estimate appears only when Windows supplies one. Desktops without a battery and devices without a reported estimate show the appropriate unavailable state.

If Windows reports low or critical battery while Crest is hidden, Crest retains one notice for the next reveal. It identifies the earlier percentage separately from the current power state, even if you have since plugged in. A cancelled reveal or disabled Controls keeps the notice pending. Battery information never opens Crest automatically or plays a sound.

The detail footer shows local date and time using your Windows formatting preferences. Reduced motion keeps the island and detail panel stationary, uses short fades, and applies expansion geometry immediately.

Built-in brightness uses a supported Windows/WMI capability. An external monitor or unsupported laptop interface may not expose this control. Missing hardware controls do not prevent the timer, shortcuts, or other supported modules from working.

**System activity** reports actual total-device CPU use, used/total physical memory, and receive/send rates across active network adapters. Rates need consecutive samples and restart after a reveal or adapter change. Sampling stops when the island is hidden. These are activity rates rather than an internet speed test.

**Connectivity** shows Windows-reported Wi-Fi connection/radio status and Bluetooth radio status. A denied or unavailable provider is shown as unavailable. Use **Windows Wi-Fi settings** or **Windows Bluetooth settings** to manage connections and radios.

## Timers, focus, and reminders (F09)

The focus card starts a **25-minute** timer when none exists. It then controls the selected timer with Pause, Resume, or Done. Select the card's time to open the **Timer** module. Version 0.2.1 includes independent timers, stopwatch, focus sessions and reminders; the original 0.1.0 release had one timer.

The Timers tab offers **5, 15, and 25 minute** presets. Each adds a new timer. For a custom timer, enter an optional name and a duration in minutes, then select **Add**. The range is one second to seven days (10,080 minutes); fractional minutes are accepted using your Windows number format.

Crest keeps up to **eight independent timers**. Select a timer name to make it the focus card's timer. Each row has its own Pause/Resume, Done, and cancel controls. Completion remains until you acknowledge or cancel that timer. If all eight slots are occupied, remove one to add another. Scroll to reach additional rows.

The countdown continues while Crest is hidden and includes time spent asleep. System-clock changes do not alter a live countdown. After an application restart, a running timer recovers using its saved UTC deadline; a paused timer retains its remaining duration. Completion never reveals the island automatically.

Timer-completion sound is off by default. Enable **Settings → Modules → Play a sound for timer completion** to hear Crest's original bundled **3.54-second chime** when a timer finishes, including while the island is hidden. The completed timer remains until you acknowledge or cancel it.

Select **Settings → Modules → Test timer sound** to preview the same chime. Preview works with completion sound turned off and leaves that preference unchanged. Crest uses your Windows audio output and application volume settings.

The **Stopwatch** tab provides Start, Pause/Resume, Lap, and Reset. Running elapsed time includes time hidden and asleep; paused time is excluded. Laps show the split and total, newest first. Up to **100 laps** are retained; recording stops at the cap until Reset clears them. Elapsed time and laps survive restarts. Restart recovery adds nonnegative UTC downtime, while live operation uses monotonic time. Reset clears the stopwatch and its saved laps. Hiding the Timers module hides these controls without stopping active tools.

The **Focus sessions** tab provides work minutes, break minutes, and 1–12 work rounds. Choose **Start focus session**, then Pause/Resume as needed. A completed work phase waits for **Start break**; a completed break waits for **Start next work round**. The last work round completes the session. **Dismiss focus session** clears it. Phase and progress survive restarts, and completed phases never advance automatically while you are away.

The **Reminders** tab accepts text and either minutes from now or a local date and time in your Windows format. Up to **32 reminders** are saved. Due reminders remain visible in the list until you choose **Snooze 5m**, **Snooze 15m**, or **Dismiss**. Snooze sets a new due time; dismissal removes only that reminder. Scheduling, snooze, and due status survive restarts. Reminders use their saved UTC due time, so changing the system clock can change when one becomes due.

Productivity events never reveal Crest automatically, including when access over other apps is enabled. Live reminder and focus completions post local Windows tray notices while Crest runs. Clicking a notice, or choosing **Timers, focus and reminders** from the tray, opens **Settings → Activities** so you can snooze, dismiss, or continue while another app is active. Windows may suppress tray notices; retained due/completed state remains available in Crest. Overdue activities recovered at restart remain pending without replaying old alerts.

The compact island shows due reminders and active/completed focus sessions when Timer is enabled. Its reminder action opens Reminders; its completed focus action opens Focus sessions. Compact pause/resume controls the displayed focus session independently of other timers.

## Clipboard history (F10)

Clipboard text capture is **off by default**. Enable **Settings → Privacy → Enable text clipboard history**, then copy new text normally. Crest records subsequent text clipboard changes while it runs. Enabling or resuming does not import the text already on the clipboard.

Open **Clipboard** from the icon dock to search saved text, select a preview to copy its full text back to the clipboard, pin/unpin an entry, or remove it. Search ignores letter case. **Pause capture** keeps existing entries and survives restarts; **Resume capture** collects future changes again.

Set **Clipboard retention** in Privacy to 1–30 days, with seven days as the default. Expired unpinned text is removed even while capture is paused. Pins remain until removed or cleared. History is limited to **100 entries**, **8,192 characters per entry**, and **120,000 stored characters**; oversized text is skipped. Old unpinned entries make room for new ones. If pins fill the limit, remove or unpin an entry before saving more text.

**Clear history** removes all entries, including pins. Turning capture off also clears saved history. Clear does not change the current Windows clipboard; its existing text stays excluded until a new clipboard change. History is stored locally in your Windows user profile and is not encrypted by Crest.

## Screen capture references (F13)

Open **Capture** and launch the Windows screenshot tool. If Windows cannot open it, use **Win+Shift+S**. Save a capture through Windows, then explicitly select its image file in Crest. Crest keeps up to **12 recent selected references**, newest first, across restarts.

Supported selections are PNG, JPEG, BMP, GIF, and WebP files. A reference opens the source image through Windows. Removing or clearing references leaves the files intact. Crest does not watch your screenshot folders or automatically import captures. Missing source files are reported; select their new location if they move.

## Workspace presets (F14)

Open **Settings → Workspaces**, enter a name, and choose **Create workspace**. Add apps/files, folders, or HTTP/HTTPS website addresses to the preset. You can save up to **20 workspaces**, each with **20 targets**. Rename a workspace or remove individual targets from the same page.

Choose **Launch** for a preset from Settings or the **Workspaces** module to open its targets through Windows. If one target fails, Crest continues through the group and reports the failures. Presets launch only when selected. Removing a preset or target leaves its files and applications untouched.

## Launch shortcuts

Open **Settings → Shortcuts** to add an app, file, Windows shortcut, folder, or website. Give the entry a recognizable name and choose its target. Website entries accept HTTP or HTTPS URLs and open in your default browser.

The side rail shows the first two entries. The Launch panel contains the complete list. Crest stores only entries you add; it does not discover your apps or folders automatically.

Targets open through Windows. If a file moves or a target becomes invalid, Crest reports the problem. Remove the old entry in Settings and add the current target. Removing a launch entry leaves its target untouched.

## File shelf

The shelf holds up to **20 file or folder references**. New references belong to the current session unless pinned.

1. Start a file drag from the wallpaper desktop while it is eligible.
2. Enter the top-center trigger and pause to reveal Crest.
3. Drop the file into the island's Shelf panel.
4. Select a reference to open it, drag it to a destination, or use its remove button to remove the reference.

Crest does not silently copy, move, or delete source files. The destination handles a file dragged out through Windows. Removing a shelf reference does not delete its original. A moved or deleted source is marked missing.

The shelf retains a reference to the original location. Dragging a reference back to the folder that already contains its original can produce Windows' same-destination message. Choose a different destination folder for a transfer. If you delete the original, its shelf entry becomes missing and cannot open until the source is restored.

Incoming drops use a supported Link or Copy effect and never authorize moving the original. During preparation, Shelf shows a short status and rejects a second overlapping import. Reset cancels outstanding preparation, and equivalent folder paths use one slot.

Choose **Pin** on a reference to retain its path across restarts. Unpinning returns it to session-only retention. Pinned missing sources remain on the shelf so you can recover them. **Locate** lets you select the file or folder at its current location; the reference keeps its identity and pin. A file reference requires a file and a folder reference requires a folder. Crest rejects a replacement already present on the shelf.

For a missing source, **Open nearby folder** opens the nearest existing parent location, when available. Crest does not scan your drives or guess a replacement. Recovery changes only the saved reference; it does not restore, copy, move, or delete the original.

Quitting clears unpinned references and retains pinned ones. In desktop-only mode, a drag from an active File Explorer window cannot reveal Crest. Earlier owner-operated reveal/drop, drag-out, missing-source, removal, and restart observations qualify their recorded binaries. The P2 shelf changes require fresh native qualification; see the [test report](TEST_REPORT.md).

## Settings

| Page | Options |
|---|---|
| Overview | Application overview and getting-started information. |
| Appearance | System/light/dark/Ocean/Forest/Rose theme, motion, Compact/Comfortable/Large size presets, size adjustment, motion speed, and preference display scope. |
| Behavior | Strict desktop mode, optional app access, selected display, per-display trigger coverage/dimensions/delay/grace, surface gestures, startup, and keyboard reveal. |
| Modules | Choose visible modules, move icons earlier/later, opt into timer-completion sound, and preview the bundled chime with Test timer sound. |
| Shortcuts | Add and remove explicit launch entries. |
| Workspaces | Create, rename, edit, launch, and remove groups of selected targets. |
| Privacy | Opt into clipboard history and set retention; explain saved references and history management. |
| Activities | Use timers, stopwatch, focus sessions and reminders in a normal Settings window, including while other apps are active. |
| About | Local-data information, reset, tray instructions, and release-verification information. |

In Appearance and Behavior, **Preference display** selects **All displays (default)** or a specific display. A display profile saves its own theme, motion, size/speed, trigger coverage, width/depth, dwell, and grace. Uncustomized displays use the defaults. **Use defaults for this display** removes that override. Crest still uses one selected display at a time; changing the preference-editing scope does not move the island. The selected display falls back to primary if disconnected, and a saved override can be reused when that display returns.

Double-click and middle-click actions on an empty island surface can expand, collapse, dismiss, move through enabled modules, open Settings, or do nothing. The optional pointer-wheel action switches modules. Pointer controls such as buttons, text fields, sliders, and lists retain their normal behavior. Gestures and module order are shared preferences; appearance and trigger profiles are per display.

## Data, upgrades, and removal

Preferences, shortcuts, workspaces, timers, stopwatch, focus sessions, reminders, pinned shelf paths, selected capture paths, and explicitly enabled clipboard history are stored in `%LOCALAPPDATA%\Crest`. Unpinned shelf references are held only in memory. Crest has no analytics, account access, notification access, or online account integrations. Core features do not require internet; a website or launched application may require its own connection.

### Reset local data

Choose **Settings → About → Reset Crest data**. Crest asks before resetting preferences, shortcuts and workspaces, clearing activities, clipboard history, shelf and capture references, and disabling startup. Original files are untouched. Clipboard capture returns to its off-by-default state.

### Upgrade

Download the new package from [GitHub Releases](https://github.com/tanmay-tiwari-20/Crest/releases). There is no automatic updater.

For an installed copy, run the newer installer. It requests a graceful quit from a running Crest instance, reuses the installation location, and preserves local configuration.

Upgrading preserves your enabled-module choices. To show Capture, Workspaces or Clipboard if they are not in your dock, enable them in **Settings → Modules**. Showing Clipboard does not opt into collection; enable text capture separately in **Settings → Privacy**. Existing timers recover their saved state. Unpinned shelf references clear when the old app quits; pin references that should survive future restarts.

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

In desktop-only mode, hovering over an active app or File Explorer does not reveal Crest. Enable **Allow Crest over other apps** if you want that access. The optional keyboard shortcut follows the same visibility preference and secure/lifecycle guards.

### Music is empty or seeking is disabled

Start playback in a player that exposes a Windows media session. Some players publish metadata without seek support; Crest disables unsupported actions. Check the preferred-player selector if several sessions are listed.

### Volume, brightness, or battery information is unavailable

Check that Windows has a usable default audio endpoint. Brightness requires a supported built-in interface. Battery estimates appear only when supplied by Windows. These limits can occur independently, while other Crest modules remain available.

### Timer sound is silent

Open **Settings → Modules** and select **Test timer sound**. Preview works even when completion sound is off. If it is silent, check your Windows audio output and Crest's volume and mute state in **Windows Volume Mixer**. To hear timer completions, enable **Play a sound for timer completion** and start a new short timer.

### A shortcut or shelf reference is missing

Check whether the original target was moved, renamed, deleted, or disconnected. Re-add a launch entry with its current target. Use **Locate** on a missing shelf reference, or open its nearest existing folder. Pin shelf references that should survive quitting; unpinned references clear on exit.

### Settings appears minimized or Crest is already running

Double-click the tray icon or choose **Crest Settings** from Start to restore the existing Settings window. Crest runs as one instance. Use the tray's **Quit** before running a separate development build or replacing a portable folder.

### Report a problem

Open an issue at [GitHub Issues](https://github.com/tanmay-tiwari-20/Crest/issues). Include the Crest version, Windows version, monitor count, display scaling, visibility mode, relevant app or hardware, and exact steps to reproduce. Note whether a fresh clear-desktop hover works after closing other apps.

The [test report](TEST_REPORT.md) distinguishes completed checks from remaining acceptance. Developers can use the [build guide](BUILD.md), [PRD analysis](PRD_ANALYSIS.md), [release notes](RELEASE_NOTES.md), and [repository README](../README.md) for implementation context.
