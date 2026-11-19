# DailyTimer design polish

Approved direction: calm, precise, practical; a meeting-room timing instrument. Keep the navy/lime palette, Segoe UI, the speaker queue, both timers, and the existing section order. The ivory countdown is the main visual; the shared timer gains emphasis during discussion. The thin remaining-time rule is the signature detail.

1. **Spacing:** Standardized the workspace, queue, cards, controls, and settings on a 4px rhythm. Speaker rows share the same minimum height and spacing.
2. **Color:** Quieted navy surfaces and ordinary metadata. Lime remains on the mark, primary action, active speaker status, keyboard focus, and active discussion balance. Exhausted shared time retains its warning color.
3. **Icons:** Kept the functional native glyphs. The settings chevron and checkbox marks use simple 1.5px strokes; no decorative icons were added.
4. **Copy:** Replaced uppercase status labels with sentence case and shortened hints. Meeting state, speaker progress, and budget meaning remain explicit.
5. **Type:** Kept Segoe UI throughout. Both countdowns use tabular figures at normal weight; small labels are at least 12px.
6. **Layout:** Retained the two-column arrangement, ivory personal timer, shared budget, actions, and collapsible settings. The queue is 280px wide and the remaining content stays fluid.
7. **Details:** Rounded inputs match buttons. Independent focus outlines avoid changing control geometry; active-speaker status and row selection remain distinct. Disabled controls remain readable. Settings retain accessible labels.
8. **Visuals:** The working countdown and remaining-time rule carry the presentation. No generated images or new decorative elements were needed.
9. **Motion:** Replaced the stock progress template with a static track and proportional fill, removing its decorative shimmer. No looping or entrance motion was added.

Validation: `build.ps1 -Test` passes 30 behavioral checks, full-duration input readability, and constrained-screen reachability. The duration check was also verified to fail with the crowded input template. The native app was reviewed with a sample roster and in discussion mode; an independent source/template review found no concrete regressions. Physical Tab traversal and screen-reader announcements were not fully exercised.

Design workflow: unslop by Damir Sokolovsky ([@heydamir](https://x.com/heydamir)), [source](https://github.com/heydamir/unslop), CC BY 4.0.
