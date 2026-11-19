# DailyTimer design polish implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this approved polish in the existing workspace. Steps use checkbox syntax for tracking.

**Goal:** Make DailyTimer calmer and easier to read while preserving its navy/lime palette, Segoe UI, layout, and meeting behavior.

**Architecture:** Retain the WPF window and all named controls. Update XAML styles and the presentation strings/colors in MainWindow.cs together, so timer refreshes preserve the approved treatment. No new dependencies or meeting-state changes.

**Tech stack:** C#, WPF, .NET Framework 4.8.

**Spec:** Direction and audit approved in this chat on 2026-11-16; existing product behavior is documented in docs/superpowers/specs/2026-11-15-daily-timer-design.md.

## Approved direction

- Product: A standup timer for facilitators managing speaking turns and shared discussion.
- Character: Calm, precise, practical.
- World: A meeting-room timing instrument.
- Moments: The large ivory countdown; shared time gaining emphasis when discussion starts.
- Signature: A thin remaining-time rule beneath the countdown.

## Global constraints

- Keep navy, lime, Segoe UI, and the existing queue/timers/settings arrangement.
- Preserve features, shortcuts, named controls, persistence, timer behavior, and offline operation.
- Use a 4px spacing rhythm, 12px minimum labels, weights 400–600, and tabular timer figures.
- Reserve lime for primary action, active state, and keyboard focus; keep ordinary metadata neutral.
- Keep the workspace scrollable at 640×480 logical resolution.

## Review focus

- Expanded settings must remain reachable on a constrained screen: run the existing layout check.
- Long speaker names must retain trimming and tooltips: inspect the populated window.
- Selection, speaking status, hover, disabled controls, and keyboard focus must remain distinguishable: inspect templates and live controls.
- Runtime refresh must not restore uppercase text or old colors: review Render and QueueEntry.
- Shared discussion and negative overtime must keep their meaning: run existing behavioral checks and inspect presentation branches.

## Task 1: Apply the nine passes

**Files:** src/DailyTimer.App/MainWindow.xaml; src/DailyTimer.App/MainWindow.cs.

**Interfaces:** Keep all named controls and event handlers unchanged. Only presentation properties and interface copy change.

- [x] Normalize spacing throughout the window, queue, controls, and expanded settings.
- [x] Quiet the existing navy surfaces and lime metadata; retain the ivory personal timer.
- [x] Keep the existing simple glyphs; remove no functional controls and add no decorative icons.
- [x] Use sentence case and consistent short-label punctuation.
- [x] Keep Segoe UI, reduce weight, improve small labels, and use tabular timer figures.
- [x] Preserve the two-column layout and section order; align control proportions.
- [x] Match input/button radii and provide stable, distinct focus/selection/hover states.
- [x] Retain the timer and progress rule as the product visual; add no generated artwork.
- [x] Keep static presentation and remove any stock progress-bar shimmer.

## Task 2: Verify and deliver

**Files:** build.ps1; package.ps1; existing tests/DailyTimer.Tests files (run unchanged).

- [x] Build and run `./build.ps1 -Test`; all 30 behavioral checks and both layout checks pass.
- [x] Inspect the rebuilt native window with a populated isolated roster and a live meeting state; expanded settings were exercised in the first review build and verified by the layout checks after the input fix. Three app renders total, including the original screen.
- [x] Review changes against the audit, then regenerate the portable ZIP with `./package.ps1`; verified all four packaged files.
- [x] Record outcomes below and deliver the runnable application.

## Execution notes

- This workspace has no .git directory, so no branch, commit, or worktree operation applies.
- User approved implementation of this focused polish. Continue within that scope without an additional plan approval.
- Use existing regression checks and visual verification; do not add tests that merely mirror cosmetic values.
- Design workflow: unslop by Damir Sokolovsky (@heydamir), https://github.com/heydamir/unslop, CC BY 4.0.
- Added a behavioral layout regression check after observing duplicated input padding: the check fails against the crowded template and passes after removing redundant PART_ContentHost margins. Both 60:00 and 240:00 remain fully readable.
- Independent read-only review found no concrete regressions. It confirmed native checkbox names, a non-focusable TextBox content host, and proportional progress fill. Original source backups were ephemeral, so the reviewer assessed current files against the approved plan rather than claiming exact before/after equivalence.
- Physical Tab navigation and screen-reader announcements were not fully exercised; live input was interrupted by user activity. Focus-ring presentation and template structure were reviewed, and runtime probes found no extra content-host tab stop.
