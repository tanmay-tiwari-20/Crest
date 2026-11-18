# Using Crest 0.1.0

## Install and open

Run the x64 Crest installer. It installs for your Windows user account, creates Start-menu entries, and can optionally create a desktop shortcut. Startup with Windows is off by default. The application includes its runtime.

Crest normally starts quietly in the system tray. Choose **Crest Settings** from Start, double-click its tray icon, or right-click the icon and choose **Settings**.

## Reveal the island

1. Close Settings and press **Win+D** to show your desktop.
2. Move the pointer away from the top edge. A pointer already there does not reveal Crest after returning to the desktop.
3. Move into the top-center edge and pause for about 120 milliseconds.
4. Click the chevron to open the horizontal activity deck. Its focus, music, and battery cards show local state together. Use the small icon dock underneath to open a detail panel; the album artwork also opens Music.
5. Move away to dismiss. Opening an application suppresses Crest immediately when the foreground event is handled.

Strict desktop mode also requires visible apps on the selected display to be minimized. File Explorer, Start, Search, Task View, and other Windows panels cannot summon Crest. Background music or timer activity never reveals it automatically.

## Modules

**Music:** Displays the active Windows media session and the actions its player supports. A player must publish a compatible session. Several sessions expose a preferred-player selector. With no session, Crest explains the empty state and disables playback actions.

**Controls:** Adjust the default output volume, mute/unmute, and built-in brightness. Battery percentage, power state, and remaining time come from Windows. A remaining-time estimate appears only when Windows provides one. Unavailable hardware controls explain the limitation.

**Focus card:** Start the25-minute preset, then Pause/Resume or acknowledge completion. Click its time to open all timer controls.

**Timer:** Choose a preset or enter minutes, then Start. Pause, Resume, Cancel, or acknowledge a completed timer. The countdown continues while hidden and includes sleep. System-clock adjustments do not alter a live countdown; restarting uses the saved UTC deadline. A paused timer retains its duration. Sound is off unless enabled.

**Launch:** Add apps/files, folders, or websites in Settings → Shortcuts. Targets open through Windows. Invalid references receive an actionable message. Only HTTP/HTTPS website targets are supported; nothing is discovered automatically.

**Shelf:** Drag a file from the wallpaper desktop into the island after hovering at the trigger. Crest stores at most 20 references for this app session. Open a reference, drag it to another destination, or remove the reference. Removing a reference never deletes its original. Missing files are marked. Quitting clears the shelf. A drag from an active File Explorer window cannot reveal Crest.

## Personalize and manage

- **Appearance:** System/light/dark theme, follow-system/normal/reduced motion, size, and motion speed.
- **Behavior:** Strict or desktop-active policy, selected display, trigger coverage/width/depth, dwell, pointer grace, startup, and an optional Ctrl+Alt+Space accessibility shortcut. The shortcut still requires an eligible desktop.
- **Modules:** Choose visible modules and opt into timer sound.
- **Tray:** Pause disables triggers; Resume requires a fresh hover. Quit releases native subscriptions, timers, tray, and windows.

Text input explicitly focuses Crest. Tab and Shift+Tab move through controls; Enter/Space activate buttons; Escape dismisses. The optional reveal shortcut focuses the compact controls so keyboard navigation can begin. Windows high contrast and reduced-motion preferences are respected.

## Data and removal

Preferences, shortcuts, and timer state are stored under `%LOCALAPPDATA%\Crest`. The shelf is held only in memory. No clipboard capture, account access, analytics, or online integration is included.

Settings → About → **Reset Crest data** asks before resetting preferences/shortcuts, cancelling the timer, clearing shelf references, and disabling startup. Original files are untouched.

Use Windows **Installed apps** to uninstall. Choose whether to keep Crest's local settings for a later installation. For portable use, Quit before removing the portable folder.

## If Crest does not appear

Check that it is running and not paused, close Settings, minimize apps, and enter the trigger afresh. Verify the selected display and trigger settings. Failure to establish desktop, DPI, or virtual-desktop state keeps Crest hidden. Media players and brightness devices can be unavailable independently without affecting the rest of the application.

See the test report for checks still awaiting owner acceptance, particularly real desktop dragging and animation quality.
