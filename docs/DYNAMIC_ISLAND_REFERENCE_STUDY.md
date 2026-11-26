# Dynamic Island reference study and CREST correction

24 November 2026. This is analysis and a proposed design direction; no application code or installed binaries were changed during this study.

## What the reference actually does

Apple combines the physical camera cutout with a software surface for background activities and system alerts. In the official launch clips, the same black silhouette becomes a shallow incoming-call capsule or a taller Face ID shape, then returns to its resting geometry. Foreground content supplies the activity identity. The outer material is opaque black, including over a light screen; it is distinct from the appearance-dependent Lock Screen banner.

The compact presentation is one activity with leading/trailing information around the camera. Minimal presentations allocate small attached/detached regions to concurrent activities. An expanded presentation enlarges the selected activity and supplies relevant details/actions. Supported activity counts and swipe gestures vary by hardware and iOS version. Tapping a custom Live Activity deep-links to related app content; holding it reveals its expanded presentation.

Sources: [Apple Live Activities HIG](https://developer.apple.com/design/human-interface-guidelines/live-activities), [ActivityKit custom views](https://developer.apple.com/documentation/activitykit/creating-custom-views-for-live-activities), [Apple Support tutorial](https://www.youtube.com/watch?v=srHDQ_lqLtE).

## Visual and motion observations

The important visual relationship is a stable anchor with rounded geometry whose size follows the information. Content sits comfortably inside the curved boundary, with optical balance rather than a generic rectangular grid. Distinct activity accents, artwork and legible values establish hierarchy. Thin outlines are situational separation, rather than a border around every element. Expanded content retains recognizable spatial relationships with its compact form.

Apple's design session calls attention to concentric margins, content visual mass, efficient compact fitting and activity-specific shape. The inspected launch footage shows different silhouettes for different purposes. These observations do not imply that every system alert follows ActivityKit's custom-view layout.

Springs support continuity of position and velocity when gestures or destinations change. Shape, content and action transitions can have different start/settling times while conveying one transformation. A spring can settle smoothly without visible bounce. Apple's exact Dynamic Island parameters and internal renderer are not published; the explanatory illustration uses representative physics and approximate proportions.

Sources: [Dynamic Island design chapter, 7:37](https://developer.apple.com/videos/play/wwdc2023/10194/?time=457), [Animate with springs](https://developer.apple.com/videos/play/wwdc2023/10158/), [official activity clip](https://www.apple.com/newsroom/videos/iphone-14-pro-3up/large_2x.mp4), [official Face ID clip](https://www.apple.com/newsroom/videos/iphone-14-pro-dynamic-island-face-id/large_2x.mp4).

## Why the current CREST interface misses the intended quality

The previous implementation improved styling and animation mechanics while retaining the original dashboard architecture. Its compact surface is 304 × 52 DIP. Expansion produces an 820 × 150 deck, a separate navigation dock at Y=158 and a separate 440 × 336 detail panel at Y=214. These are three visible surfaces; placing them in one native window does not make them one shape-changing island. Only the deck is enclosed by the morphing clip. The detail panel disappears immediately on contraction.

The first expansion also exposes unrelated or inactive tools. The 432-DIP music card occupies about 53% of deck width even when nothing is playing; it retains a large placeholder, disabled transport and zeroed timeline. An idle timer preset and an unavailable battery ring still receive full cards. Their visual weight follows the component catalogue rather than the user's current activity.

There is a semantic discontinuity too: compact status can represent a running `Local.Focus` session, while the expanded focus card reads `Local.Timer` and can show an unrelated idle 25-minute preset. Compact and expanded views need one shared selection model.

Changing the color palette, logo, border radii or spring response cannot resolve these structural problems. Passing functional tests establishes preserved workflows and the tested mechanics; it does not establish product design quality.

## Constraints for the next CREST design

1. One contiguous primary envelope grows around the selected activity and its essential controls.
2. Compact, expanded and detailed views share the same selected real activity and preserve its identity and relative placement.
3. First expansion remains contextual. A complete tools view remains available through an intentional secondary action, retaining every existing feature.
4. Concurrent real activities can receive small secondary presentations; navigation icons are not treated as activities.
5. Idle and unavailable modules remain discoverable without occupying persistent live cards, placeholder artwork and disabled timelines.
6. Expanded dimensions follow content, bounded to a practical desktop width. Complex existing forms can scroll inside an intentional tools view.
7. Shape, content masking, actions and contraction are designed as one transition. Rapid reversal retains momentum and reduced motion remains supported.
8. Preserve CREST's desktop visibility, opt-in integrations and notification policy. iOS system behavior is a reference, not authorization to auto-reveal a Windows overlay.

The proposed Windows adaptation need not reserve a fake camera gap, imitate touch-only gestures or copy proprietary artwork. Its defining relationship should be a small live activity that expands coherently into that same activity's controls.
