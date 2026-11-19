# DailyTimer desktop application

## Intent

A Windows desktop application for a facilitator running a daily team standup. Each person has a short update slot; overtime and clarifying questions consume one shared discussion budget. The facilitator can change attendance and speaking order without restarting the meeting.

## Confirmed requirements

- Windows only; C# is acceptable. WPF is the selected interface framework.
- Default personal allowance: 1 minute 30 seconds, configurable.
- Default shared discussion allowance: 10 minutes, configurable.
- Personal time expiry plays a sound and automatically begins consuming the shared budget.
- The shared budget also pays for questions or discussion after a speech ends early.
- A button ends the current turn and advances to the next person.
- Add, remove, reorder, and shuffle speakers easily.

## Approaches considered

1. **C# / WPF (selected):** flexible layout, large readable timers, styled states, and a native desktop window. The user accepted this recommendation.
2. **C# / Windows Forms:** suitable for a basic utility, but less convenient for the proposed visual presentation. Not selected.

## Proposed interface

One resizable window with an editable speaker queue on the left and the current speaker plus two large timers on the right. The current state is always stated in words as well as indicated with color. Show how many people have finished and who is next.

- Queue: add a name, rename, remove, drag to reorder, move up/down, and shuffle remaining speakers. Completed and active speakers are visually distinct.
- Meeting: Start / Pause / Resume, End speech / Discuss, Next speaker, and Reset meeting.
- Settings: personal duration, shared duration, sound enabled, test sound, and optional always-on-top.
- Small hints explain that the shared timer covers overtime and questions. Controls are keyboard accessible.

## Timer behavior

Both timers start paused. Start begins the first speaker's personal allowance. Only one allowance is consumed at any instant.

| State | Clock consumed | End speech / Discuss | Next speaker |
| --- | --- | --- | --- |
| Ready | None | Disabled | Disabled |
| Speaking | Current personal time | Switch to discussion | Mark speaker done and start next personal slot |
| Discussion | Shared budget | Disabled | Mark speaker done and start next personal slot |
| Paused | None | Disabled | Advance while remaining paused |
| Finished | None | Disabled | Disabled |

Pause retains whether the meeting was speaking or discussing. Resume returns to that state. Personal expiry plays one alert and switches to discussion automatically; any elapsed time beyond the boundary is deducted from the shared budget. Starting the next person gives them the full configured personal allowance and preserves the shared balance.

When the shared budget expires, play a distinct alert once and display shared overtime as a negative, clearly highlighted balance. Allow the meeting to continue, so exhaustion never blocks a speaker or discussion. Subsequent speakers still receive their personal allowance.

After the last speaker's Next speaker action, finish the meeting and stop both clocks. Discussion for the last person remains possible until that action; label it Finish meeting. No names means Start is disabled with a helpful prompt.

Use a monotonic elapsed-time source, not a decrement-per-tick counter. UI refresh delays must not lose time. Time spent while explicitly paused is excluded. Computer suspension may advance the elapsed clock; account for it consistently on resume rather than attempting a background alarm while asleep.

## Queue changes during a meeting

Completed speakers remain completed; shuffle affects only people waiting to speak. Reordering a waiting person does not change the active speaker. Adding a person during a meeting appends them to the waiting queue. Removing a waiting or completed person is immediate. Removing the active person requires an explicit confirmation, then advances to the next waiting person, preserving pause state and shared balance. Renaming the active person preserves all timing. Speaker identity is independent of their name, so duplicate names cannot corrupt the queue.

## Persistence and reset

Save names, queue order, durations, sound preference, and always-on-top preference in a local per-user JSON settings file. Relaunch opens a fresh, paused meeting using the saved roster; live clock state and completed flags are not restored. Malformed or unreadable settings must fall back to usable defaults and give a nonblocking notice. Saving failures must not terminate a meeting.

Reset meeting asks for confirmation if a meeting has started, restores the two configured allowances and marks everyone waiting. Preserve roster order. Duration edits apply to the next reset or new meeting; settings explain this while a meeting is in progress. No cloud services, login, or network calls at runtime.

## Implementation boundaries

Separate the clock/meeting state machine from the WPF view. Use stable speaker IDs, an injectable monotonic clock, and an alert event that the view handles once. Keep dependencies minimal. Generate or bundle two short alert sounds and provide a sound-off preference.

Use an SDK-style solution targeting a supported .NET Windows version, with a README explaining build, launch, and publish. This machine has a .NET runtime but no SDK; obtain build tools locally without changing system settings if possible. Provide a runnable build if the environment permits, and report any actual build limitation.

## Goals and boundaries

Success means a facilitator can run a complete meeting, including early finishes, automatic overtime, discussion, pauses, and attendance changes without losing or resetting the shared balance. The main timers must remain readable when screen sharing.

The first version does not need meeting history, remote control, calendar integration, individual time limits, accounts, automatic speech detection, or cloud synchronization. Do not auto-advance on personal expiry: the current person may still be speaking or answering questions.

## Validation

Automated checks cover timer boundaries, delayed updates, shared-budget carryover and exhaustion, pause/resume, early discussion, advancing without discussion, last speaker, reset, queue mutation, and persistence recovery. Build the actual WPF application and inspect the running window at normal and reduced sizes. Exercise the complete meeting flow with short durations, verify that alerts fire once, and confirm that settings survive relaunch.

## Designer defaults for approval

The interface arrangement, shared negative overtime, persistence behavior, active-speaker removal confirmation, final Finish meeting action, and settings applying on reset are proposed defaults. Detailed styling, internal class structure, and exact SDK version are left to implementation.
