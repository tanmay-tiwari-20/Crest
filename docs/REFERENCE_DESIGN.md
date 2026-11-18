# Reference design review

The latest user direction makes the supplied visual references the basis for the island's appearance. The PRD still governs desktop eligibility, focus safety, local feature behavior, privacy, and packaging.

## Video source

The supplied video was inspected on 18 November 2026:

```text
Screen Recording 2026-11-18 223405.mp4
```

The recording became available after the initial source check. It is 62.2 seconds long, 1756 by 790 pixels, at a declared 30 frames per second. OpenCV extracted the actual supplied recording; unrelated recordings were not used.

The ten representative samples are at 0, 4, 10, 17, 24, 31, 38, 45, 53, and 60 seconds. The labelled contact sheet is `tmp/reference-video/contact-sheet.png`. Additional transition samples and `motion-contact.png`, detail crops, source metadata, palette counts, and frame-bound measurements are in the same directory.

## Observed appearance

The shell is opaque black, with little or no visible border and strongly rounded lower corners. Content stays close to the top edge. The music and timer modules are shallow horizontal controls; the clipboard view expands the same outer black shell vertically to contain four charcoal cards. There are no large text navigation tabs: controls use small filter pills, circular icon buttons, and compact metadata.

In the stable 3.5-second sample, the music shell is approximately 758 by 117 recorded pixels, a width-to-height ratio of 6.5. Circular artwork sits on the left at about 43 pixels in diameter, followed by two short metadata lines. A player-selection pill and circular app and transport controls occupy the remaining first row. The main playback circle is approximately 36 pixels in diameter; secondary buttons are approximately 28 pixels. A thin blue seeking track spans most of the second row, with elapsed and remaining time at its ends.

At 17 seconds, the clipboard shell is approximately 862 by 300 recorded pixels, with four cards of roughly 175 by 242 pixels. The cards use the same corner treatment at a smaller radius, tiny type/source labels, restrained text, preview thumbnails, and circular footer actions. At 38 seconds an image preview opens below this surface. This is a visual example of a secondary registered surface, rather than evidence for independent overlay privileges.

At 24 seconds, a stopwatch occupies a shallow horizontal shell: a tiny timer/stopwatch selector in the first row, a large left-aligned countdown, a compact description, and circular controls toward the right. The 61-second compact music pill is about 208 by 43 recorded pixels, with a small status dot, circular art, one title line, elapsed time, and a tiny equalizer.

The recording presents modules independently. It does not show the still reference's timer-left, music-center, battery-right composition or a persistent lower icon dock in those exact arrangements. The supplied still images should therefore govern the combined deck composition; the video provides control styling and transition references.

## Recorded color samples

Solid areas are dominated by these approximate colors; compressed video introduces neighboring values:

| Element | Approximate sample |
| --- | --- |
| Outer shell | `#000000` |
| Inner cards and secondary buttons | `#131313` to `#181818` |
| Raised secondary pills | `#262626` |
| Music and timer primary controls | Mint around `#70E0C0` |
| Stopwatch selection, countdown, and primary control | Pale blue around `#8EB2F8` |
| Ordinary text | Off-white |
| Supporting labels | Muted gray |

Use one consistent icon weight, large enough targets for actual pointer operation, and stable text/art anchors. The compressed reference's small labels are a visual hierarchy reference, not a reason to reduce Crest below the PRD's readability and accessibility targets.

## Motion observations

Frame measurements at 30 fps show the first opening between approximately 3.03 and 3.23 seconds. The shell grows from a small top-edge region into its shallow horizontal form with fast early movement and a short settling tail. A stopwatch-to-music width transition at 28.57 to 28.80 seconds takes approximately 230 milliseconds. The shape expands mostly in width while the content remains vertically restrained.

An ordinary close around 13.67 to 14.10 seconds takes roughly 430 milliseconds in this recording, but that sequence also includes viewport pan/zoom; it is unsuitable for an exact application-animation budget. The last vertical collapse spans roughly 13.97 to 14.10 seconds. Other parts of the recording also change magnification and position, so recorded pixel sizes cannot be interpreted as physical display pixels or DIP values without an independent scale reference. No strong or repeated bounce is visible in the inspected transitions.

These observations support restrained accelerated opening and a short settling tail, with size and opacity animated together. Crest's PRD starting values of 240 milliseconds for reveal and 220 milliseconds for expansion are consistent with the clearest measured reference transitions. Keep foreground-loss suppression immediate, independent of cosmetic animation.

## Behavior boundary

The reference includes a persistent thin idle edge, a floating idle capsule, side docking, clipboard capture, a stopwatch, and an island visible near Settings. Those appearances do not authorize changing Crest's desktop-only rule or adding unverified future modules. The design implementation must retain no visible placeholder while hidden, fresh hover after eligibility returns, explicit opt-in data access, and registered focus/popup exceptions.

## Implemented composition

The implemented island uses the supplied still's wide composition: a two-shortcut rail, blue focus character with a compact countdown and action column, music artwork and controls, and battery status. Its baseline dimensions are:

| Surface | Baseline size and position in DIP |
| --- | --- |
| Compact pill | 260 by 44, centered at the top |
| Main deck | 820 by 150 |
| Lower icon dock | 284 by 40, 8 below the deck |
| Selected module panel | 420 by 316, 8 below the dock |
| Transparent native window layout | 836 by 530 including outer margins |

The deck, dock, and module panel are children of one native island window and one visibility controller. Separate rectangles are used for visible regions, with rounded-corner membership checks; the gaps and unused portions of the native window do not count as the island's pointer region. Display placement uses the selected display's DPI and fits the complete layout above the Windows working-area bottom. Actual mixed-DPI and display-disconnect behavior remains part of native acceptance testing.

The initial prototype used a crop from the user-supplied 1352 by 762 still image. For public repository distribution, the owner requested original artwork instead. The current `assets/focus-orb.png` is a transparent export of the original project illustration in `assets/focus-orb.svg`, using a blue cloud silhouette, soft gradient volume, two eyes, and a small smile. It contains no reference crop and is covered by the project MIT license. See `THIRD_PARTY_NOTICES.md` for provenance. Historical preview reviews below refer to the earlier prototype unless explicitly identified as the repository release build.

The final static preview review checked dark and light overview surfaces and the expanded Controls layout. Labels, artwork, buttons, and the lower dock fit without clipping in those previews. Static previews establish layout and asset loading; they do not establish native foreground behavior, physical frame delivery, screen-reader operation, desktop drag acceptance, or the PRD's measured performance budgets.

## Video comparison for the redesign

The existing extracted frames were reused for this comparison; the source file is confirmed as 46,827,212 bytes with a last-write time of 22:34:05. No unrelated footage or duplicate extraction was needed.

The redesign's 240-millisecond reveal and 220-millisecond shape/detail transitions closely match the recording's clearest 200–230 millisecond changes. Its near-black shell, rounded cards, compact icon dock, mint controls, and source-derived blue focus character follow the visual references. The combined 820-by-150 deck is intentionally governed by the supplied still: the recording itself shows one independent module at a time.

The two motion refinements identified in the comparison are now implemented and were reviewed in source:

- The lower dock fades with the same 220-millisecond duration and cubic ease as the deck shape. Reversal reads its current opacity; a generation check rejects obsolete completions. Suppression cancels the dock animation immediately, and ordinary collapse disables its controls before fading out.
- Selecting the already-active module refreshes its data without rebuilding its controls or restarting the fade. Changing an already-visible detail module starts its next transition from the current opacity and vertical offset. This preserves an input on repeated selection and avoids resetting an interrupted fade to zero.

Source review found no new blocker in these refinements. This establishes the intended cancellation and continuity paths, not presented-frame smoothness on the physical display. Foreground loss continues to hide every surface immediately, even if a cosmetic transition is in progress. The recording's approximately 430-millisecond closing sequence includes camera movement, so it does not justify slowing Crest's 190-millisecond ordinary dismissal.
