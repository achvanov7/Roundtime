# DailyTimer Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver a runnable Windows standup timer with a mutable speaker queue and one shared discussion allowance.

**Architecture:** A UI-independent meeting state machine measures elapsed time through an injected monotonic clock. A WPF window renders snapshots and owns sounds and user input; a JSON store saves only configuration and roster.

**Tech Stack:** C#, WPF, .NET Framework 4.8, dependency-free console test runner. Revised from .NET 10 because no SDK is installed and its download was unavailable; see execution ledger.

**Spec:** `docs/superpowers/specs/2026-11-15-daily-timer-design.md`

## Global Constraints

- Windows only; default personal time 90 seconds and shared time 600 seconds.
- Personal expiry starts shared time and sounds once; never auto-advance speakers.
- Only one allowance runs at once. Shared time carries over and may become negative.
- Names/order/settings persist locally; live meeting state never persists.
- No runtime network access or accounts. Keep dependencies minimal.

## Review Focus

- Long UI stalls cross both time boundaries: account for all elapsed time and emit each alert once (Task 1).
- Editing the roster cannot reset a current turn or change its identity (Task 1).
- Duplicate names and empty rosters must remain safe (Tasks 1 and 2).
- Malformed, locked, and unwritable settings must not crash a meeting (Task 2).
- Large names, a long queue, high DPI, and small windows must preserve access to controls (Task 3).

## Task 1: Meeting engine

**Files:** `src/DailyTimer.Core/{DailyTimer.Core.csproj,Meeting.cs,Speaker.cs,MeetingSettings.cs}`, `tests/DailyTimer.Tests/{DailyTimer.Tests.csproj,Program.cs}`; root build configuration and solution.

**Interfaces:** `Meeting(Func<double> clock, IEnumerable<Speaker> speakers, MeetingSettings settings)`; properties `State`, `ActiveSpeaker`, `PersonalRemaining`, `SharedRemaining`, `Speakers`, `HasStarted`; methods `Tick()`, `StartOrTogglePause()`, `Discuss()`, `Next()`, `Reset()`, `Add(string)`, `Rename(Guid,string)`, `Remove(Guid)`, `Move(Guid,int)`, `ShuffleRemaining()`, `Configure(MeetingSettings)`; event `Alert` with `PersonalExpired` or `SharedExpired`. Speaker exposes stable `Id`, mutable `Name`, `Completed`.

- [x] Write behavioral tests using a fake clock for boundaries, early discussion, delayed ticks, pause/resume, advancing, finishing, negative shared balance, reset, configure-on-reset, active removal, duplicate names, reordering, and shuffling remaining only.
- [x] Run the test project; expect a missing engine failure before implementation.
- [x] Implement engine with elapsed-time accounting, retained paused phase, and stable active identity.
- [x] Run test project; expect all engine cases pass.

## Task 2: Settings persistence

**Files:** `src/DailyTimer.Core/SettingsStore.cs`, persistence tests in `tests/DailyTimer.Tests/Program.cs`.

**Interfaces:** `SettingsStore(string path)`, `Load()` returning `SavedConfiguration` plus optional notice, `Save(SavedConfiguration)` returning optional error. Configuration contains settings, ordered speaker names and always-on-top. MeetingSettings contains positive bounded durations and sound preference.

- [x] Write roundtrip, corrupted file, invalid durations/names, missing file, and failed-save tests; run and observe failure.
- [x] Implement validation and atomic JSON save through a sibling temporary file; recover safely without overwriting a corrupted source until an explicit save occurs.
- [x] Run tests and verify successful roundtrip and nonthrowing error paths.

## Task 3: WPF application and delivery

**Files:** `src/DailyTimer.App/{DailyTimer.App.csproj,App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs,SoundService.cs}`, `README.md`, `build.ps1`, `.gitignore`.

**Consumes:** Engine and persistence interfaces from Tasks 1 and 2. Uses a Stopwatch clock and a DispatcherTimer solely for refresh.

- [x] Build one styled, resizable window: editable queue, move buttons and drag ordering, shuffle waiting speakers, two large timers, state text, start/pause/resume, discuss, next/finish and reset; clear empty state.
- [x] Wire engine alerts to distinct generated WAV sounds; add sound toggle/test, duration inputs, and always-on-top.
- [x] Save roster/settings on deliberate changes and closing. Show nonblocking notices on save/load errors. Require confirmation for active removal and live reset. Apply duration changes on reset.
- [x] Build Release with warnings as errors and run the complete test suite; expect zero warnings/errors and all tests pass.
- [x] Launch and inspect the real window; run a short meeting through personal expiry, discussion, shared expiry, pause, next, finish, reset and saved roster relaunch. Check a smaller window and long names.
- [x] Obtain one independent whole-project review; resolve material findings and rerun affected verification.
- [x] Package the verified .NET Framework executable, DLL and config into `artifacts/DailyTimer-Windows.zip`, document build/run/package commands, and leave the runnable app available.

## Execution notes

Implement inline in the explicitly supplied project directory. It contains no existing repository or baseline code, so no worktree or integration step is applicable. Keep a progress ledger in `docs/superpowers/plans/2026-11-15-progress.md`. The approved design authorizes implementation; continue without further approval rounds.
