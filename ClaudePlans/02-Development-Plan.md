# Tambola (Internal) — Phased Development Plan

Depends on: [01-Requirements.md](01-Requirements.md). Progress is tracked in [PROGRESS.md](PROGRESS.md). Each phase ends with a demo + tests green before the next starts.

| Phase | Name | Key deliverables | Exit criteria |
|---|---|---|---|
| 0 | Requirements & sign-off | Requirements doc, open questions resolved | Requirements approved |
| 1 | Foundation | Solution (Domain/Application/Infrastructure/Web), .NET 8, EF Core + SQLite (WAL), migrations, Serilog, config, health check, xUnit projects, Fluent UI + jQuery assets bundled locally | App runs, DB created, CI-style `dotnet test` passes |
| 2 | Auth & Registration | Negotiate (Windows) auth, registration page, unique EmployeeCode/email rules, domain/regex validation, claims-transformation for roles, deactivate | AC-1; unregistered users blocked |
| 3 | Roles & Admin | Admin seeding from config, Host grant/revoke UI, audit log, employee search | AC-2 (page-level) |
| 4 | Core Domain | Ticket generator, draw (CSPRNG shuffle), pattern Strategy classes (Early5, lines, Full House, Corners…), claim validator + unit tests | AC-3, AC-4 (service level) |
| 5 | Rooms & Lobby | Create/configure room, codes/links, join, buy tickets, SignalR hub + groups, presence, lobby UI | Two browsers see live lobby |
| 6 | Live Gameplay | Start/pause/resume/end, manual + auto caller (hosted service), number board, marking, TTS/sounds, per-room serialization | Full game playable end-to-end |
| 7 | Claims & Prizes | Claim flow, bogey, ties, prize panel, auto-complete, results page | AC-4, AC-6 |
| 8 | Resilience | Reconnect/rehydrate, app-restart recovery, concurrency tests, 200-user load test | AC-5, NFR-1 |
| 9 | History & Reports | My Games, host history, admin leaderboard, CSV export | Reports match DB |
| 10 | Polish & Hardening | Responsive/a11y pass, theme, security review (authZ on every hub method, anti-forgery), error pages | NFR-2, NFR-5 |
| 11 | Deployment & Handover | IIS publish guide, backup script, runbook, user guide, UAT | UAT sign-off |

## Per-phase detail documents (to be written at the start of each phase)
`ClaudePlans/phase-NN-<name>.md` — tasks, files, tests, risks. Written just-in-time so they reflect decisions made earlier.

## Conventions
- Solution layout: `src/Tambola.Domain`, `Tambola.Application`, `Tambola.Infrastructure`, `Tambola.Web`; `tests/Tambola.*.Tests`.
- Game logic lives in Domain/Application (no SignalR/EF dependencies) so it is unit-testable.
- Hub methods are thin: authorize → call service → broadcast.
- Every phase updates PROGRESS.md and the Claude memory entry.
