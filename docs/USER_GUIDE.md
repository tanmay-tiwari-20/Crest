# Using Crest

Crest 0.4.0 opens a top-center island on an eligible Windows desktop by default. You can opt in to opening it over other apps. [Release notes](RELEASE_NOTES.md) · [Test report](TEST_REPORT.md)

## Install

Choose the installer or portable application ZIP under **Assets**. [Crest 0.4.0 prerelease](https://github.com/tanmay-tiwari-20/Crest/releases/tag/v0.4.0)

Both packages include the .NET runtime. You do not need a separate runtime, an SDK, administrator access, or a Crest account. Windows 10 and ARM64 packages are not available. The current installer is unsigned.

### Installer

1. Download `Crest-Setup-0.4.0-x64.exe`.
2. Run it and follow the setup steps. Crest installs for your current Windows user account.
3. Choose an optional desktop shortcut if wanted. Setup adds Start-menu entries.
4. Start Crest from the Start menu or the final setup step.

Startup with Windows is off by default. You can enable it later in **Settings → Behavior**.

### Portable package

1. Download `Crest-Portable-0.4.0-x64.zip`.
2. Extract the entire archive into a folder you want to keep.
3. Run `Crest.exe` from the extracted folder.

Keep the executable, runtime, and supporting files together. Portable Crest still stores preferences in `%LOCALAPPDATA%\Crest`. If you move its folder after enabling startup, disable startup first and enable it again from the new location.

When a release supplies a SHA-256 checksum, compare it with your downloaded package:

```powershell
Get-FileHash .\Crest-Setup-0.4.0-x64.exe -Algorithm SHA256
```

Compare the result with that filename's entry in `SHA256SUMS.txt` from the same release. The [build guide](BUILD.md) explains package creation and the optional signed MSIX path for Windows capabilities. An ordinary installer or portable package does not guarantee notification-listener access.

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
5. Click the compact island or its chevron to expand the current activity. When Crest is idle, it opens **Your tools**. Select a secondary bubble to switch to another live activity.
6. Select **Tools** to find the enabled modules. **Settings → Modules** controls visibility and order.
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

The focus card starts a **25-minute** timer when none exists. It then controls the selected timer with Pause, Resume, or Done. Select the card's time to open the **Timer** module, which includes independent timers, stopwatch, focus sessions, and reminders.

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

Set **Clipboard retention** in Privacy to 1–30 days, with seven days as the default. Expired unpinned text is removed even while capture is paused. Pins remain until removed or cleared. History is limited to **100 entries**, **8,192 characters per entry**, and **120,000 stored characters**; oversized text is shortened and labeled. Old unpinned entries make room for new ones. If pins fill the limit, remove or unpin an entry before saving more text.

**Clear history** removes all entries, including pins. Turning capture off also clears saved history. Clear does not change the current Windows clipboard; its existing text stays excluded until a new clipboard change.

Retained clipboard text is protected with Windows DPAPI for your current Windows user. Enabled legacy plaintext history migrates automatically. If protected storage cannot be read or written, Crest explains the problem and avoids leaving plaintext history on disk. This protection does not prevent applications running as your Windows user from accessing your clipboard or user data.

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

## Optional integrations

Open **Settings → Integrations** and choose **Weather**, **Calendar**, **Downloads**, **AI**, or **Windows**. Each source starts off. The first enabled source adds **Optional integrations** to the island's tools catalogue. You can hide the entry in **Settings → Modules**. Source refreshes run quietly.

### Weather

1. Select **Use Windows location**. Windows controls location access. From the island, **Set up Windows location** opens the Weather page in Settings. If access is unavailable or you prefer a fixed city, enable weather, enter the city name, choose **Find city**, and select a match.
2. Read the current temperature, conditions, wind, and last-refresh time. Use **Refresh weather** when you want a new forecast.

Crest asks Windows for location once per selection and rounds the coordinates to 0.1° before saving them or requesting weather. Select **Use Windows location** again after moving. The weather provider receives only the saved area for forecasts and the text you enter for city searches. [Open-Meteo](https://open-meteo.com/)

Crest keeps one forecast in a 30-minute cache. A failed refresh shows the age of saved data. **Clear saved weather** deletes the cache. **Disconnect weather** also turns off weather and removes the saved location. Turning off weather keeps the chosen area for next time.

### Calendar and meeting links

Choose one source under **Calendar**:

- **Connect Outlook** reads the default calendar from classic Outlook on this PC. Open classic Outlook with a configured profile before connecting. Use a feed or file with the new Outlook app, web Outlook, Google Calendar, or Apple Calendar.
- **Use subscription link** opens a private field. **Connect calendar link** accepts an HTTPS or `webcal` iCalendar publishing/subscription link from a provider. Use a link that supplies .ics data within 2 MiB. The selected link is encrypted for your Windows user. Treat private calendar links like credentials.
- **Select local calendar** reads a local `.ics` file up to 2 MiB.

The panel refreshes on open as needed. **Refresh calendar** fetches changes on demand. Crest shows up to 200 upcoming events over 90 days and keeps previews in memory. Connecting another source replaces the current source.

Event names and details are hidden by default. Enable **Show event titles and details** to see titles, locations, and supported meeting links. File and subscription sources also show event descriptions. Outlook uses locations for meeting links. Ordinary events, timezones, all-day dates, and common daily/weekly/monthly/yearly recurrence are supported, including exclusions and occurrence overrides. Complex recurrence or unknown timezone rules are reported and skipped.

For a recognized meeting link, **Open Microsoft Teams meeting**, **Open Zoom meeting**, or **Open Google Meet meeting** opens the validated HTTPS provider page in your browser. Joining proceeds through that provider. Crest does not automatically join a call and provides no meeting mute or hang-up control.

**Clear event previews** clears the in-memory events. **Disconnect calendar** also removes the source and turns off reading. Turning the source off keeps its configuration for next time.

### Crest-managed downloads

Enable **Enable Crest-managed downloads**, enter an HTTP or HTTPS URL, and select **Choose destination and download**. Choose a new filename in an existing folder. Crest preserves existing files and refuses a destination already occupied by another transfer.

The panel shows percentage when the server supplies a total size, or received bytes when it does not. Up to two transfers run at once, with a 4 GiB limit per transfer and 20 entries in the session list. Individual transfers provide **Cancel**, **Retry**, and **Remove download from list** when applicable. Choose **Open downloaded file** to launch a completed file explicitly.

**Cancel transfers and clear list**, disabling downloads, or quitting cancels active transfers and cleans up Crest's temporary partial files. Completed files remain in their chosen folders. The list is memory-only and clears on restart. Crest tracks only transfers started here; downloads started in browsers or other apps are not monitored.

### Read-only local AI

Have [Ollama](https://ollama.com/) running on this PC with the model you want already installed. Crest does not install Ollama or download a model. In **AI**, enter its local endpoint, usually `http://127.0.0.1:11434/`, and the installed model name. Choose **Save local model selection**, then enable **Enable read-only local AI assistance**.

Enter a question and choose **Ask local model**. Only your typed question and bounded conversation are sent to the selected loopback service. Remote endpoints are rejected. Crest gives the model no file, clipboard, system, execution, or external-action tools; suggestions remain text for you to review. Local model resource use is separate from Crest's core memory and CPU targets.

Questions are limited to 4,000 characters. Responses and retained conversation have bounded size, and each request has a two-minute timeout. **Cancel response** stops the current request. **Clear conversation** removes the typed prompt and session conversation. Turning assistance off cancels requests and clears conversation while retaining the selected endpoint/model preferences. Conversation is not persisted across restarts.

### Windows notification previews

In **Settings → Integrations → Windows**, enable **Read Windows notification previews**, then choose **Request Windows notification access**. Windows decides whether to grant access. An ordinary installer or portable package may lack the required package identity and notification-listener capability. The optional MSIX build path declares that capability; installation requires suitable signing and trust, and package/device acceptance remains separate.

After access is granted, use **Refresh notification previews**. App names and times are visible; enable **Show notification titles and bodies** to display notification text. Crest retains at most 50 previews in memory, with bounded text and 24-hour expiry. **Acknowledge Crest preview** removes an individual preview, and **Clear Crest notification previews** clears the inbox. These actions do not dismiss the original Windows notification or reproduce reply actions. Windows controls its own notification banners.

Turning notification reading off stops collection and clears previews. Permission denial or revocation leaves local Crest modules available. Manage the operating system's permission separately in Windows Settings.

### Accessory batteries and Phone Link

Enable **Read paired accessory batteries**, then choose **Refresh accessory batteries** to read standard Bluetooth LE battery values from connected paired devices. Pair devices through **Open Bluetooth settings**. A device that exposes no usable standard value is shown as unavailable; Crest does not estimate it. Pairing and device support remain Windows responsibilities.

Enable **Enable Phone Link access**, choose **Check Phone Link support**, then select **Open Microsoft Phone Link** when available. Microsoft Phone Link handles pairing and whichever calls/messages/actions your phone supports. Crest does not read or retain a phone account or messages, and does not provide in-island phone controls.

**Disconnect Windows integrations** turns off all three Windows sources and clears their Crest previews and capability results. It does not unpair devices, uninstall Phone Link, or change Windows' own permission choices.

## Settings

| Page | Options |
|---|---|
| Overview | Set up the island and jump to visibility, modules, or integrations. |
| Appearance | Preview System, Dark, Light, Ocean, Forest, or Rose. Choose island size, motion, and the display whose appearance you want to edit. |
| Behavior | Strict desktop mode, optional app access, selected display, per-display trigger coverage/dimensions/delay/grace, surface gestures, startup, and keyboard reveal. |
| Modules | Choose visible modules and reorder them from the same list. Enable or preview the timer-completion chime. |
| Shortcuts | Add and remove explicit launch entries. |
| Workspaces | Create, rename, edit, launch, and remove groups of selected targets. |
| Privacy | Opt into clipboard history and set retention; explain saved references and history management. |
| Activities | Use timers, stopwatch, focus sessions and reminders in a normal Settings window, including while other apps are active. |
| Integrations | Set up weather and your calendar, review source status, and manage downloads, local Ollama, Windows previews, accessory batteries, and Phone Link. |
| About | Local-data information, reset, tray instructions, and release-verification information. |

In Appearance and Behavior, **Preference display** selects **All displays (default)** or a specific display. A display profile saves its own theme, motion, size/speed, trigger coverage, width/depth, dwell, and grace. Uncustomized displays use the defaults. **Use defaults for this display** removes that override. Crest still uses one selected display at a time; changing the preference-editing scope does not move the island. The selected display falls back to primary if disconnected, and a saved override can be reused when that display returns.

**System** follows the Windows light or dark preference. Crest applies theme changes as you select them. Windows high contrast uses system colors. The contextual island keeps a black surface in normal contrast and uses the selected palette for its idle accent.

Double-click and middle-click actions on an empty island surface can expand, collapse, dismiss, move through enabled modules, open Settings, or do nothing. The optional pointer-wheel action switches modules. Pointer controls such as buttons, text fields, sliders, and lists retain their normal behavior. Gestures and module order are shared preferences; appearance and trigger profiles are per display.

## Data, upgrades, and removal

Preferences, shortcuts, workspaces, timers, stopwatch, focus sessions, reminders, pinned shelf paths, selected capture paths, and explicitly enabled clipboard history are stored in `%LOCALAPPDATA%\Crest`. Optional source choices and the weather cache also live there. Clipboard persistence is protected for the current Windows user. Unpinned shelf references, calendar/notification previews, download history, and AI conversation remain in memory.

Crest has no analytics or required account. Core tools, classic Outlook calendar access, and local calendar files can operate offline. Weather, configured online feeds, and explicit downloads contact their providers. AI contacts your selected local service. Notification and device location access depend on Windows permissions.

### Reset local data

Choose **Settings → About → Reset Crest data**. Crest asks before resetting preferences, shortcuts and workspaces, clearing activities, clipboard history, shelf/capture references and optional-source data, cancelling transfers and model requests, and disabling startup. Original files and completed downloads are untouched. Clipboard capture and optional integrations return to their off-by-default state.

### Upgrade

Download the new package from [GitHub Releases](https://github.com/tanmay-tiwari-20/Crest/releases). There is no automatic updater.

For an installed copy, run the newer installer. It requests a graceful quit from a running Crest instance, reuses the installation location, and preserves local configuration.

Upgrading preserves your enabled-module choices. To show Capture, Workspaces or Clipboard if they are not in your dock, enable them in **Settings → Modules**. Showing Clipboard does not opt into collection; enable text capture separately in **Settings → Privacy**. Existing timers recover their saved state. Unpinned shelf references clear when the old app quits; pin references that should survive future restarts.

An upgrade to 0.4.0 keeps your existing city, selected calendar file, theme, and display preferences. Windows location access, Outlook connection, and online subscription links require your selection. Clipboard history and subscription links encrypted for one Windows user can only be read by that user.

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

### Optional integrations are missing or unavailable

Confirm **Settings → About** shows 0.4.0. Open **Settings → Integrations** to enable a source. If its tools entry is hidden, enable **Optional integrations** in **Settings → Modules**.

For weather, check your connection and saved area. If Windows location is unavailable, check **Windows Settings → Privacy & security → Location** or choose a city. For calendars, verify that classic Outlook has a default calendar, the feed link returns iCalendar data, or the selected `.ics` file is accessible. For downloads, choose a new filename in an existing writable folder and check the source server. For AI, confirm your local Ollama service and selected model are available.

For notification previews, request access from Settings and read the package/permission status. Standard portable or installer packages may lack the necessary Windows capability. For batteries, check that the paired device is connected and exposes a standard BLE value. For phone access, run **Check Phone Link support** before opening the installed Microsoft app. These capability failures leave local Crest tools available.

### Report a problem

Open an issue at [GitHub Issues](https://github.com/tanmay-tiwari-20/Crest/issues). Include the Crest version, Windows version, monitor count, display scaling, visibility mode, relevant app or hardware, and exact steps to reproduce. Note whether a fresh clear-desktop hover works after closing other apps.

The [test report](TEST_REPORT.md) distinguishes completed checks from remaining acceptance. Developers can use the [build guide](BUILD.md), [PRD analysis](PRD_ANALYSIS.md), [release notes](RELEASE_NOTES.md), and [repository README](../README.md) for implementation context.
