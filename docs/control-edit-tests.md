# Pending-control verification

Crest 0.1.0 source was updated after the reference-layout redesign. No native app or UI was launched for this verification.

## Changes

Settings now restores its prior Normal or Maximized state before activation. Its initial/restored dimensions and minimums fit the current display's native working area, including DPI changes. This addresses the previous700 DIP height/560 DIP minimum exceeding the laptop's working height at200 percent DPI. Baseline125 percent dimensions remain unchanged.

Volume, brightness and both seek controls retain the requested value through debounce and asynchronous completion. Writes use a captured value; an old write cannot clear a newer requested edit. A newer edit drains after the active write. Hidden/unloaded controls stop their UI debounce timers while a final already-requested action can finish without revealing Crest. Capability loss clears pending state.

Seek controls allow up to1.5 seconds for a lagging player timeline to acknowledge the target. If no acknowledgement arrives, they return to reported player data; the pending value cannot remain indefinitely. The deck starts its commit guard synchronously before releasing its pointer-down guard, removing a refresh gap.

## Automated evidence

Release compilation succeeds with zero warnings and errors. The combined console suite passes24 tests: nine desktop-interaction outcomes, ten local timer/settings/launcher/shelf outcomes, and five pending-control outcomes. The five added checks verify that old hardware data does not overwrite a queued value, an old completion does not erase a newer edit, cancellation/recovery rejects stale completions, a lagging timeline can acknowledge a seek target, and an unacknowledged target expires after bounded grace.

## Integration evidence still required

The tests validate deterministic write coordination, not a real player's seek semantics. Real keyboard/pointer seeking, hardware-key refresh during a slider edit, and device removal need root/owner UI verification. Root subsequently verified Normal-state minimized Settings restoration on the final installed app through its Settings entry point; see final-native-qa.json. Maximized restoration remains pending. The200 percent Settings fit is implemented but has not been exercised by changing the owner's Windows scaling.

The release gates still need real clear-desktop hover and interrupted suppression, desktop file OLE drag/drop, active media sessions, actual sleep/restart/device edge cases, screen-reader/high-contrast checks and display reconfiguration. The initial cold launch exceeded the proposed3-second budget. The final build's ten-minute hidden CPU/memory sample and presented-frame/suppression percentiles are not established by this source check.
