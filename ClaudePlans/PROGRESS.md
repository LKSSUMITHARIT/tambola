# Tambola — Development Progress Log

_Last updated: 2026-10-13_

| Phase | Status | Notes |
|---|---|---|
| 0 Requirements | ✅ Done | 01-Requirements.md; open questions §13 resolved by defaults (see Decisions) |
| 1 Foundation | ✅ Done | Solution, .NET 10, EF Core+SQLite (WAL), Serilog, health `/health`, local jQuery + signalr.js |
| 2 Auth & Registration | ✅ Done | Negotiate auth (+ Dev cookie auth for local), registration with unique code/email/Windows account, deactivate |
| 3 Roles & Admin | ✅ Done | Admins from config, Make/Revoke Host UI, audit log |
| 4 Core Domain | ✅ Done | Ticket generator (10 000-ticket test), draw, pattern strategies + tests |
| 5 Rooms & Lobby | ✅ Done | Create room, codes, join, tickets, hub, presence, lock/remove |
| 6 Live Gameplay | ✅ Done | Start/pause/resume/end, manual+auto calling (hosted service), manual/auto cutting, board, TTS |
| 7 Claims & Prizes | ✅ Done | Winner quotas, lock rules, congrats/late/invalid messages, server validation, auto-complete |
| 8 Resilience | ✅ Done | Reconnect re-sync + restart-safe auto caller; load tested 2026-10-12 (see docs/Load-Test-Report.md) |
| 9 History & Reports | ✅ Done | My Games, results, leaderboard, CSV, audit |
| 10 Polish & Hardening | 🟡 Partial | Responsive + dark theme, authZ on hub; **no browser/a11y pass yet** |
| 11 Deployment & Handover | 🟡 Partial | Deployment guide (IIS + Linux) + manuals written; **not yet deployed anywhere / no UAT** |
| 12 v1.1 | ✅ Built | Linux `Manual` auth (domain+userID+password), `AllowedDomains` validator (also for Windows), wrong-claim limit per room, 1500-player cap + perf fixes, soft-delete + Live/Past games, schema auto-upgrade |

## Verification status (honest)
* `dotnet test`: **25/25 pass** (v1.1 added wrong-claim limits, 1500 cap, delete/restore, domain validator, passwords/throttle, schema upgrade of an old DB, Manual-mode register/login/logout over HTTP, blocked-domain page). Earlier: 14/14 — — ticket validity ×10 000, all patterns, registration uniqueness, full game through the real SignalR hub (host-only methods, bogey lock, late claim, quota, forged ticket, uncalled-number cut, auto-complete, auto caller, unregistered user blocked).
* App started and smoke-tested with curl: login → register → make host → create room → host console renders; non-host gets 403 on Create Room.
* **Not yet done:** interactive browser test of `play.js`/`host.js` (syntax-checked only), IIS/Windows-auth run, load test, accessibility audit.

## Decisions log
- 2026-10-11: Stack fixed by user — ASP.NET Core, SQLite, SignalR, jQuery (no React/Angular), Windows Auth, internal employees only, one EmployeeCode per email, multiple Hosts assignable.
- 2026-10-11: Number-cutting (Auto/Manual) fixed by Host per room; claim always manual. Prizes are cases with Host-set winner count each; first N valid claims win; optional reward text.
- 2026-10-11: Player screen spec (FR-36–43): current number, ticket, lockable Claim buttons, congrats / "claim faster" messages, full server validation.
- 2026-10-11 (build): defaults chosen for open questions — Host cannot play own room; late join/tickets allowed until 10 calls; bogey locks that prize for the player; no email OTP; prizes are non-monetary; no full 6-ticket set.
- 2026-10-11 (build): .NET 10 (installed SDK) instead of .NET 8; 3 projects (Domain/Web/Tests); Fluent-style local CSS instead of Fluent web-components; EF `EnsureCreated` instead of migrations; single Play page serves lobby+game, single Host page serves lobby+console.
- 2026-10-11: Defaults changed — dark theme and sound ON (numbers read aloud + WebAudio win/late/invalid effects). Configurable via `Tambola:DefaultTheme` / `Tambola:DefaultSoundOn`; each user's 🌓/🔊 choice is remembered in the browser. Also fixed Windows-auth 500 (status-code re-execute ran Negotiate twice) — `Program.cs` no longer re-executes on 401.
- Tie rule: claims processed serially per room; ordering = server arrival. A player can win each prize once per ticket they hold (v1.2).

- 2026-10-13: v1.1 decisions — Linux login = domain dropdown + userID + password (email may be any domain); Windows mode also rejects users whose domain is not in `AllowedDomains`; wrong claims: per-room numeric limit (default 3, 0 = unlimited, old rooms = 1); room cap 1500; delete = soft delete (host/Admin, finished games only), Admin can restore; joins sent as `PlayerJoined` delta instead of full list.

- 2026-10-16: v1.2 — funny Hindi caller clips (recorded-style mp3 + preload + offline cache), caller sound options moved to an Admin-only Sound page (global, live via SignalR), game screens redesigned (Design C: number strip, 3 tickets/row, tabbed right panel, mobile dock), one prize win per ticket. Tests 33/33.

## Backlog / next
1. Browser UAT of Play/Host pages with multiple users; fix UI issues.
2. Re-run load test on the real target server; consider remaining items in docs/Load-Test-Report.md §5.
3. EF migrations (currently `SchemaUpgrader` patches columns at start-up).
3b. **Next phase: deploy to Hostinger VPS** — first create DNS A record `tambola` -> 72.61.172.114 and verify, then check ports 80/443, then install (systemd + Caddy, AuthMode=Manual).
4. Optional: email OTP, 6-ticket sets, Fluent web components, host-side claim list with ticket view.

## Session log
- 2026-10-11: Reviewed reference repo; requirements + phased plan; HTML mockups (UIMockups/); built full app (src/), tests (tests/), docs (docs/, README.md).
