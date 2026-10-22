# Viewer swipe navigation: visual pager (APPROVED by Isiac Oct 22, 2026, no changes)

Canonical copy for agents. Repo path once committed: `docs/specs/swipe-nav.md`.

## Behavior
1. Drag: current image tracks finger 1:1; neighbor slides in from that side with a 16 px gap. Name and position (12 / 48) stay on current until commit.
2. Commit: release past 30% of viewer width, or flick > 0.5 px/ms in the drag direction. Both pages animate the rest of the way in 220 ms (range 200-250) with cubic-bezier(0.1, 0.9, 0.2, 1). Name/position chip cross-fades at animation start. A flick back against the drag cancels even past 30%.
3. Snap back: below threshold with no flick, spring back to 0 in 280 ms (range 250-300), overshoot <= 4 px. Nothing else changes.
4. Edge resistance: on first/last item, offset = d * 0.3 / (1 + |d| / viewerWidth). Soft accent edge glow plus 'End of folder' / 'Start of folder' hint. Release always snaps back. No wrap-around.
5. Zoomed: Photos-style handoff. Drag pans until the image edge; extra travel in the same gesture pages, and the threshold counts only the handoff travel. Zoom resets to Fit on commit; going back restores the previous zoom/pan.
6. Video: when not playing, swipes page. While playing, a horizontal drag on the scrub bar (28 px tall hit zone) scrubs; a drag elsewhere pages. Leaving pauses and saves position; neighbor shows its poster frame.
7. Review mode is distinct: left/right = Reject/Keep with tilt + red/green tint card; the neighbor is NOT revealed during drag; after commit the next image arrives with the auto-advance slide (same 220 ms curve). Up = Skip. Review chip always visible. Outside Review, swipe never keeps or rejects.

## Timing
Drag 1:1; threshold 30% or 0.5 px/ms; commit 220 ms cubic-bezier(0.1,0.9,0.2,1); snap back 280 ms spring (<= 4 px overshoot); edge 0.3x with growing damping. If Windows Animation effects are off (UISettings.AnimationsEnabled == false), swaps are instant but the drag still tracks.

## Implementation
- Custom pager, not FlipView (its ScrollViewer paging fights zoom/pan, the edge handoff, Review and the scrub bar, and gives no control over threshold or rubber-band).
- Three page visuals (prev/current/next) in a container. Translation driven by a Composition InteractionTracker (VisualInteractionSource on the viewer with redirection so the zoom ScrollPresenter/pan gets first claim), or manipulation-driven Translation, plus ExpressionAnimations for the neighbor offset and edge glow opacity, so it runs on the compositor thread at 120 Hz while the UI thread decodes.
- Commit/snap via tracker inertia modifiers (snap points at 0 / +-(width+16)) or a KeyFrame animation with the curve above.
- Preload: decode +-1 neighbors at screen size (HDR as FP16) when an item opens; after commit shift the ring and decode the new neighbor. Full resolution only on zoom.
