# Lucas Screentime

A Windows tray application that tracks daily screen time for a child (Lucas) and sends a summary notification to parents' iPhones each evening.

## Purpose & Context

- Lucas is a trustworthy child — the app is **not hidden or locked**
- Parents receive daily email notifications readable on iPhones
- Once installed, the app must be **fully hands-off** on Lucas's PC
- Parents configure everything via the in-app settings UI — no manual file editing

---

## Tech Stack

- **Language/Framework**: C# / .NET 8, WPF
- **Database**: SQLite (via Microsoft.Data.Sqlite or EF Core)
- **Auto-update**: Clowd.Squirrel + GitHub Releases
- **Notifications**: Email via SMTP (Gmail App Password)
- **Tray icon**: WPF NotifyIcon (or Hardcodet.NotifyIcon.Wpf)

---

## Screentime Tracking Logic

### Primary Signal — Monitor State
**Monitor ON = counting. Monitor OFF/asleep = not counting.**

Listen for `WM_POWERBROADCAST` with `GUID_CONSOLE_DISPLAY_STATE`:
- `1` = monitor on → start/continue counting
- `0` = monitor off → stop counting
- `2` = dimmed → treat as off (configurable)

This naturally handles all scenarios:
- Active use → monitor stays on → counted
- Walks away → monitor sleeps after ~3 min → stops counting
- Watching YouTube → browser suppresses sleep during video → counted
- Paused game, walks away → no input → monitor sleeps after ~3 min → stops
- PC sleeping → monitor off → not counted

### Secondary Signal — Screen Lock
Listen for `WM_WTSSESSION_CHANGE`:
- `WTS_SESSION_LOCK` → **immediately** stop counting (don't wait for monitor sleep)
- `WTS_SESSION_UNLOCK` → resume only when monitor-on signal is also active

### Sleep / Hibernate
Listen for `WM_POWERBROADCAST` `PBT_APMSUSPEND` → immediately stop counting.
`PBT_APMRESUMEAUTOMATIC` → wait for monitor-on signal before resuming.

### Storage
- All timestamps stored as **UTC** in SQLite
- Day boundaries calculated in **local time** (`TimeZoneInfo.Local`)
- Handles PST/PDT transitions correctly — "today" always means the local calendar day
- Table: `sessions(id, start_utc, end_utc)` — active segments only
- Daily total = sum of all session durations for the local calendar day

---

## Notifications

- **Method**: SMTP email (Gmail with App Password recommended)
- **Recipients**: Up to 2 parent email addresses
- **Timing**: Random time within a configurable window (default 8:45–9:00 PM local time)
- **Content**: `"Lucas had X hours Y minutes of screen time today."`
- **Timezone**: Notification fires at correct local wall-clock time, handles DST

---

## Auto-Update

- **Mechanism**: Clowd.Squirrel checking GitHub Releases
- **Check interval**: Configurable in settings (start frequent during development, reduce later)
- **Behavior**: Downloads and installs silently in background, applies on next launch
- **Deploy process**: Publish new installer to GitHub Releases — that's it

---

## System Tray Icon

- **Normal state**: Static icon — just shows the app is running
- **Error state**: Red icon — something went wrong (failed notification, tracker crash, etc.)
- **Tooltip**: Shows today's running total, e.g. `Today: 2h 15m`
- **Right-click menu**:
  - `Today: 2h 15m` (display only)
  - `Settings...`
  - `Exit`

No color-coding beyond the error state. The icon is for the parent's peace of mind, not for Lucas to interact with.

---

## Settings Window

All configuration is in the in-app settings UI — **no manual file editing required**.
Opens automatically on first launch if nothing is configured.

| Setting | Default |
|---|---|
| SMTP host | `smtp.gmail.com` |
| SMTP port | `587` |
| SMTP username | (blank) |
| SMTP password | (blank, stored securely) |
| Parent email 1 | (blank) |
| Parent email 2 | (blank, optional) |
| Notification window start | `20:45` |
| Notification window end | `21:00` |
| Update check interval (minutes) | `15` (reduce once stable) |
| **Test Notification** button | Sends a real email immediately |

---

## Project Structure

```
LucasScreentime/
├── Tracking/
│   ├── MonitorStateMonitor.cs     ← WM_POWERBROADCAST / GUID_CONSOLE_DISPLAY_STATE
│   ├── SessionMonitor.cs          ← WM_WTSSESSION_CHANGE (lock/unlock)
│   ├── PowerMonitor.cs            ← sleep / hibernate / wake events
│   └── ScreentimeTracker.cs       ← coordinates signals, writes sessions to DB
├── Storage/
│   └── ScreentimeRepository.cs    ← SQLite reads/writes, UTC↔local conversion
├── Notifications/
│   ├── EmailService.cs            ← SMTP send
│   └── DailyReportJob.cs          ← 8:45–9pm scheduler, builds message
├── Update/
│   └── AutoUpdater.cs             ← Clowd.Squirrel update check loop
├── UI/
│   ├── TrayIcon.cs                ← NotifyIcon, context menu, error state
│   └── SettingsWindow.xaml        ← all user-facing configuration
├── Settings/
│   └── AppSettings.cs             ← typed settings model, persistence
└── App.xaml.cs                    ← startup, single-instance enforcement
```

---

## Key Constraints & Decisions

- **Single instance**: Only one copy of the app should run. Use a named Mutex on startup.
- **Startup with Windows**: Register in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **No admin required**: Everything runs as the logged-in user (Lucas's account)
- **Child is trustworthy**: No obfuscation, no anti-tamper, no hidden processes
- **Hands-off**: After initial setup by parent, zero maintenance on Lucas's PC
- **SMTP password storage**: Use Windows `DPAPI` (`ProtectedData`) — encrypts to the current user account, stored in local app settings file
