# CREST premium interface specification

Prepared before implementation, 24 November 2026. This is an adaptation of interaction principles to a native Windows desktop utility; it does not reproduce Apple's proprietary artwork or claim knowledge of Apple's private animation constants.

Document history: the sections below record the initial audit and first redesign proposal. That proposal retained the three-card deck and detached navigation/detail arrangement, which did not achieve the intended interaction. The **0.4.0 contextual correction** section at the end supersedes that floating-interface proposal and describes the subsequent native implementation. The original audit remains a record of the starting application.

## Reference study

Primary references consulted:

- [Apple: Design dynamic Live Activities, WWDC23](https://developer.apple.com/videos/play/wwdc2023/10194/). A single contextual surface can retain a family resemblance while giving each activity its own identity. Compact content should communicate live essentials, remain narrow and balanced, and expand into inset information and essential controls. Relative placement between presentations creates coherence. The organic shape and elasticity communicate that the activity inhabits the surface.
- [Apple: Animate with springs, WWDC23](https://developer.apple.com/videos/play/wwdc2023/10158/). Position and velocity continuity are central to fluidity. Springs can inherit velocity on gesture release and interruption, with restrained or zero overshoot. Multiple coordinated properties need not have identical settling times. Settling thresholds should stop negligible updates rather than leave animations running forever.
- [Apple Human Interface Guidelines: Live Activities](https://developer.apple.com/design/human-interface-guidelines/live-activities). The compact presentation prioritizes essential information, the expanded presentation preserves relative placement, inset rounded content follows concentric margins, and dynamic layouts use only the space they need. Heavier, legible text and considered color establish hierarchy. Width-constrained presentations abbreviate information rather than accumulating padding.
- [Apple Human Interface Guidelines: Motion](https://developer.apple.com/design/human-interface-guidelines/motion). Motion communicates state and continuity, responds to interaction, and should account for accessibility and people who prefer reduced motion.

Application interpretation: use a stable top-center anchor, an organic rounded envelope, meaningful compact status, grouped expanded controls, and continuous retargetable motion. CREST does not have an iPhone sensor cutout; it should not reserve an artificial empty region. Taps and keyboard buttons remain primary controls on Windows. Existing double-click, middle-click, wheel, hover, and accessibility shortcut preferences remain intact. Long-press should not replace familiar desktop activation or interfere with file drag/drop.

## Existing project audit

The app is C#/.NET 10 WPF, with Windows Forms tray/display support, Windows media sessions, Core Audio, WMI, and optional local/remote integrations. `App` owns persistent preferences and feature hubs. `DesktopPolicy`, `InteractionController`, and `InteractionStateMachine` enforce visibility, suppression and fresh pointer entry. The visual tree is built in C#; `App.xaml` supplies shared styles and `Design` supplies theme resources and factories. There is no web frontend or routing framework to replace.

The floating window currently uses a fixed 820 × 150 expanded deck, 260 × 44 compact pill, detached icon dock and a 420 × 316 detail panel. `ActivityDeck` renders focus, Windows media, battery and shortcuts. `IslandWindow` builds Music, Controls, Launch, Shelf, Capture, Workspaces, Clipboard, Timer and Integrations bodies. `SettingsWindow` has Overview, Appearance, Behavior, Modules, Shortcuts, Workspaces, Privacy, Activities, Integrations and About pages. The reusable `TimerToolsPanel`, `ProductivityExtrasPanel`, `ClipboardHistoryPanel`, `P2SystemPanel`, `IntegrationsPanel`, `WeatherCalendarPanel`, `DownloadAiPanel` and `WindowsIntegrationsPanel` serve detail and/or settings views.

Visual issues: fragmented shell/dock/detail hierarchy, rigid card widths, undersized 9–11px supporting labels, repeated arbitrary margins, weak hierarchy in dense action rows, opacity-only hover feedback that dims labels, mixed corner radii, strong gray input outlines beside weak card boundaries, scattered status colors, a raster logo inconsistent with the focus illustration, and gradient card fills that obscure elevation differences. Settings reads as a long preference form rather than a composed product. Stock checkbox and context menu treatment differs from the custom controls. Fixed cubic/Quartic time curves restart when retargeted, retaining position but losing velocity, and several independent timing paths make continuity difficult to maintain.

Existing functionality to retain: multi-session playback/seek/player selection; audio output switching/fallback, volume, mute, brightness, battery notices, CPU/memory/network and Wi-Fi/Bluetooth links; eight named timers, stopwatch/laps, focus phases and persistent reminders; opt-in protected searchable/pinnable clipboard history; capture references; workspace presets and launch shortcuts; bounded file references with drag/drop, pin/open/remove and missing-source recovery; all six theme choices including System, size/motion/gestures and per-display preferences; weather, read-only ICS calendars and verified meeting links; managed HTTP(S) downloads/cancel/retry; existing local Ollama typed conversations/cancel/clear; permission/capability-gated notifications/BLE/Phone Link; startup/reset/tray and native suppression behavior.

## CREST design language

Direction: quiet precision. Use ink dark surfaces, warm chalk light surfaces, cool neutral secondary text, and a restrained mineral/teal accent. Ocean, Forest and Rose keep their existing selectable identities with coherent surface/text/line tokens. No neon, decorative fake activity, excessive blur or sweeping gradients. Depth is conveyed by a fine outer outline, inset raised surfaces and controlled shadow.

Token roles: Surface (page), Raised (card), Inset (field/recess), Text, Muted, Line (control boundary), Divider (quiet separation), Accent, AccentFill, AccentText, status success/warning/error, IslandShell/Card/Text/Muted/Accent/Outline. All themable surfaces and labels use dynamic references. High contrast maps roles to Windows system colors. Light uses subtly different chalk and white elevations with ink text; dark uses several deliberate charcoal elevations with near-white primary and readable gray secondary text.

Spacing follows a 4px base: 4/8/12/16/24/32/40. Settings uses a generous sidebar, clear page eyebrow/title/subtitle and grouped cards; smaller screens fit working area and keep content scrollable. Detail views use compact, well-aligned sections, 12–16px insets, and bounded scrolling. Labels are normally 12–14px, body 13–14px, card headings 15–18px, page headings 28–32px, and statistics approximately 28–36px. Use Segoe UI Variable/Segoe UI with medium/semibold emphasis; no proprietary Apple font requirement.

Original brand direction: a geometric crest/summit that also suggests an open C, with a strong monochrome silhouette and restrained lowercase wordmark. Implement vector WPF geometry for application navigation and appropriately sized SVG/PNG/ICO assets for tray, taskbar, installer and documentation. Light/dark wordmark colors derive from theme tokens. Keep existing focus character as a feature illustration; it is not the application logo.

## Interaction and motion implementation

Compact state keeps actual prioritized battery warning, timer/focus/reminder and media content/actions. Idle content is explicitly idle. Context should never be manufactured. Expanded state groups the three live cards and keeps the dock as the related navigation layer; module detail uses the same surface and hierarchy. Existing notification/completion behavior must not unexpectedly reveal the island. Disabled unsupported integrations remain truthful and explain availability.

Use a reusable native spring engine driven by WPF composition rendering, independent of low-frequency feature refresh. Track current position and velocity for each animated scalar and retarget without resetting velocity. Use a damped oscillator, clamped frame intervals or stable closed-form integration, and displacement/velocity settling thresholds. Morph the existing shell's clip geometry rather than relaying out the entire visual tree every frame. Translate/scale/opacity handle content reveal and dock/detail motion; avoid continuously animating Window.Width/Height or expensive shadows. Keep expanded content present during contraction until no longer visible, and gate interaction/visibility with generation-aware completion so rapid expand/collapse/suppress cannot resurrect stale content. Stop and detach rendering callbacks at rest and on suppression/window close.

Use a slightly elastic shell spring, a calmer/critical content spring, and shorter feedback for buttons. Preserve the top-center anchor and present readable content only as the shell permits. Press states slightly compress the control or shift its surface, hover adds a refined fill/highlight, and keyboard focus remains a visible ring. Do not add motion to every status refresh. Reduced motion makes geometry/content position changes immediate or minimal and disables scaling/bounce. Respect existing MotionSpeed and System preference, including High Contrast.

## Implementation ownership and verification

1. Shared tokens/styles/brand resources establish the visual system for every screen.
2. Settings layout/navigation/page grouping and all reusable panels adopt the system while preserving handler wiring and automation names.
3. Island/activity deck layout and shared spring transitions implement the defining floating interaction.
4. Rebuild with warnings treated as errors; run domain outcomes, WPF outcomes with `--skip-native-clipboard`, and platform geometry checks. Existing probes use isolated hubs/settings rather than user data.
5. Render light/dark island, settings and representative detail screens at varied DPI/sizes; inspect actual images, test compact/expanded/rapid retarget/reduced-motion and accessible names, and record any unresolved physical capability or performance acceptance.

Baseline and final screenshots must be labeled as WPF static fixture renders where that is the source. Do not claim measured 60/120 FPS from a static render or extrapolate a performance guarantee from a successful build. Native display/screen-reader/hardware testing and frame delivery depend on the physical environment. Test results and any omitted native clipboard outcomes must be reported explicitly.

## Implementation realization

The implementation uses analytic spring timelines through native WPF animation clocks instead of a managed `CompositionTarget.Rendering` callback. This keeps the existing animation-property and clock inspection behavior, avoids a persistent managed frame subscription, and delegates sampling to WPF's animation system. `SpringDynamics` evaluates position and velocity in closed form; `SpringMotion` tracks targets and inherits velocity when interrupted. Eleven exponential time constants allow a subpixel final settle even for the critical spring's linear term and representative high initial velocities. Geometry clips preserve the fixed outer layout during morphing; surface/content translations and bounded opacities coordinate the reveal. Reduced motion preserves immediate geometry and a minimal 60ms opacity transition where completion acknowledgement depends on actual presentation.

Added regression coverage checks initial position/velocity continuity, 30/60/120/144Hz sampling equivalence, bounded repeated reversals, subpixel convergence, monotonic opacity, real WPF clock release, suppression during overlapping transitions, obsolete close callbacks and live reduced-motion changes. The sampling-equivalence test establishes mathematical independence from sampling rate, not a measured display frame rate.

## 0.4.0 contextual correction

This correction follows the [second Dynamic Island reference study](DYNAMIC_ISLAND_REFERENCE_STUDY.md). The first overhaul improved styling and spring mechanics while retaining a dashboard: an 820-DIP deck, a separate navigation dock, and a separate detail panel. Its large idle media placeholder, unstarted timer preset, and unavailable battery ring gave inactive modules more prominence than the user's actual activity. This section replaces that arrangement with a small selected activity that expands inside one contiguous black envelope.

### One surface, four presentations

`IslandWindow` now owns Compact, Activity, Tools, and Detail presentations inside the same clipped shell. `IslandEnvelope` paints the geometry used for masking and native hit testing. The previous detached navigation dock is replaced by a Tools catalogue inside that shell; its legacy `_dock` field name does not denote a second visible island. Feature panels replace the current body rather than appearing below a permanent deck.

Baseline dimensions are device-independent pixels before the user's size preset and display-fit scaling:

| Presentation | Width | Height and behavior |
| --- | --- | --- |
| Idle compact | 186 | 48; original Crest mark, literal Idle status, and the expand affordance |
| Active compact | 272 | 48; selected activity identity, concise status/value, and a supported contextual action |
| Selected activity | 376 | Derived from the selected content, bounded to 128–500; essential activity controls rather than unrelated module cards |
| Tools catalogue | 440 | Measured catalogue content, bounded to 166–528 |
| Feature details | 440 | Measured body content, bounded to 206–528; complex forms scroll inside the envelope while the header and footer remain outside the scrolling body |

The transparent 480 × 560 composition canvas provides space for these presentations and concurrent bubbles. It is not a permanently filled dashboard or the visible hit area. The visible surface stays anchored at the top-center of the selected display. Compact geometry uses a 24-DIP corner radius; expanded geometry uses 30 DIP. The shape, corner radius, header position, and content visibility change together. There is no imitation camera opening or artificial camera gap.

Clicking the compact activity opens that activity's controls. When nothing is active, clicking the idle island opens Tools. The complete enabled module collection remains discoverable there, in the user's configured order. Music/player controls, timers, stopwatch, focus sessions, reminders, system controls, shortcuts, shelf, capture references, workspaces, clipboard history, and optional integrations retain their existing feature panels. No feature is removed to make the compact presentation small.

### One selected identity and truthful activity state

`IslandActivity` is an immutable snapshot containing source identity, kind, module, title, subtitle, value, phase, accent role, supported primary action, and optional known progress. `IslandActivities` derives snapshots from real feature state, and `IslandActivityCatalog` connects them to the existing app services and commands. Refresh replaces a snapshot for the same source identity; immutable does not mean its displayed data stops updating.

The shell selects an activity ID, then the compact header and `ActivityDeck` consume that same activity. A focus session therefore expands into its own stage, round, remaining time, and controls rather than an unrelated 25-minute timer preset. Music artwork appears only when Windows supplies actual artwork. An idle or unsupported source does not create a live card, zeroed timeline, empty battery ring, or placeholder progress.

Default selection orders real warnings and completed/due activities ahead of ordinary ongoing work. A user's explicit selection remains attached to its source while viewing that activity and while that source remains available. When the selected source ends, the presentation contracts instead of retaining obsolete controls. Primary actions revalidate the current identity, phase, and operation before invocation. The shared action gate prevents simultaneous duplicated starts/actions, and focus commands validate the expected focus ID while holding the hub's state lock.

Up to two 44 × 44 minimal bubbles can represent other real activity kinds while Compact is visible. Each bubble identifies an existing source and selects that source when activated. They are concurrent activity presentations, not navigation icons. Additional timers and other sources remain available through the existing complete controls.

Opted-in optional operations participate only while they actually run. Crest-managed downloads use their transfer IDs, received bytes, and server-reported totals; a percentage/progress bar appears only when the total is known. A busy local AI request supplies its configured model and actual service status without an invented percentage. Both also require the Integrations module to be enabled. Their contextual links open the corresponding existing panel: transfer cancel/retry and list management remain in Downloads; response cancel and conversation clear remain in AI. Existing source-disable, data-clear, opt-in, and connection requirements are unchanged.

### Black island family, independent Settings themes

The island's normal surface is opaque black in System, Dark, Light, Ocean, Forest, and Rose. Near-black card, field, and hover elevations support complex controls; white primary text and readable neutral gray supporting text provide hierarchy. A fine neutral outline separates the envelope where necessary. `IslandAccentTimer`, `IslandAccentMusic`, and `IslandAccentNotice` describe actual activity context. Bright action fills use the separate dark `IslandActionText` role, avoiding the white action-label color used by Light Settings.

The shell locally aliases the shared feature-control resource roles to this island family. Those aliases do not change the application's Settings palette. Settings retains all theme choices and displays an idle preview matching the 186 × 48 compact geometry. High Contrast overrides the island family with Windows system colors, including the action foreground and boundary roles.

### Desktop motion, interaction, and accessibility

The native WPF analytic spring system is retained. Geometry and header position inherit velocity when retargeted; content uses coordinated, calmer opacity/translation clocks inside the same mask. Generation-aware completion prevents a superseded transition from hiding or restoring the wrong view. Contraction keeps the outgoing presentation within the changing envelope until its visibility transition completes. Clocks are released at rest and stopped on suppression or window teardown. The implementation does not animate the native window's size every frame or attach a permanent managed frame callback.

Reduced motion applies geometry and header position immediately, without bounce or translation; a minimal opacity transition remains where presentation completion is used to acknowledge a notice. Existing motion speed, Windows animation preference, size presets, per-display preferences, configurable double/middle-click and wheel gestures, and keyboard reveal remain supported. Expansion/collapse controls expose accessible names and visible focus; Escape dismisses the island through its existing controller. Text input and explicit keyboard activation keep their native focus behavior.

Desktop eligibility remains the authority for reveal and suppression. Fresh hover entry, desktop-only/app-access preferences, selected display, Settings, Pause, lock, sleep, and secure desktop retain their existing policy. Real activity updates can change the visible context but do not automatically reveal a hidden island. Existing due notices and optional timer sound remain separate from automatic surface reveal.

Visual acceptance for this correction must inspect undistorted native-aspect compact, activity, concurrent, Tools, detail, and Settings renders. Fixture activity data must be labeled as isolated test state. Passing geometry or source-state tests is not a claim of measured frame delivery, physical accessibility acceptance, hardware compatibility, or product-design quality.
