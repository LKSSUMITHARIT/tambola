# Architecture

## Overview
```
Browser (Razor Pages + jQuery + signalr.js)
   │ HTTPS + Windows Auth        │ SignalR /hubs/game (WebSocket → SSE → long polling)
   ▼                             ▼
ASP.NET Core (Kestrel/IIS)
  Pages ──► EmployeeService / EF queries         GameHub (thin: authorise → service)
                              │                        │
                              └────── GameService (singleton, per-room lock) ──► IHubContext broadcasts
                                          │                         ▲
                                          ▼                         │
                                   EF Core → SQLite (WAL)    AutoCallerService (1 s tick)
Domain (no deps): Entities · TicketGenerator · IWinPattern strategies
```
Deviation from the original plan: **3 projects** (Domain, Web, Tests) instead of 4 – Application/Infrastructure code lives in `Tambola.Web/Services` and `/Data`. Target framework **.NET 10 (LTS)**. UI uses Fluent-style CSS tokens (local `site.css`) rather than the Fluent web-components bundle, so there is no CDN dependency.

## Server-authoritative game
* **Draw:** at room creation the server stores a CSPRNG permutation of 1–90 (`Room.DrawOrder`). It is never serialised to clients; numbers are revealed one at a time (`CalledNumber`).
* **Tickets:** `TicketGenerator` creates valid 3×9 tickets (5 per row, ≥1 per column, column ranges 1–9 … 80–90, ascending) with `RandomNumberGenerator`; uniqueness enforced per room.
* **Claims:** `GameService.ClaimAsync` runs under the room's `SemaphoreSlim`: running? ticket owned? prize in room? not already claimed? pattern satisfied by the **called** numbers (client marks ignored)? quota left? → `won` / `late` / `invalid` / `rejected`. Winners are rows in `PrizeWinner` (unique per prize slot and per employee). When all prizes are full the room completes.
* **Cutting:** Auto → client shows every called number cut; Manual → `Mark` persists taps after verifying the number is on the ticket and has been called.

## Patterns (Strategy)
`IWinPattern` per case in `Tambola.Domain/Game/Patterns.cs`; add a class and register it in `PatternCatalog` to introduce a new winning case.

## Data model (SQLite via EF Core `EnsureCreated`)
`Employee` (unique WindowsUser, EmployeeCode, Email; IsHost, IsActive) · `Room` (code, status, call/cut mode, DrawOrder, NextCallAt) · `RoomPlayer` · `Ticket` (27-value grid, Marks) · `CalledNumber` · `Prize` (pattern, WinnerCount, RewardText) · `PrizeWinner` · `Claim` (Valid/Bogey/Late) · `AuditLog`.

## SignalR contract
Client → server (`GameHub`): `JoinRoom`, `GetState`, `BuyTicket`, `Mark`, `Claim`; host only: `Start`, `CallNext`, `SetPaused`, `SetCalling`, `End`, `SetLocked`, `RemovePlayer`.
Server → clients (group = room code): `NumberCalled`, `StatusChanged`, `PrizesChanged`, `PlayersChanged`, `Feed`, `PlayerRemoved`. Claim results are returned to the caller only.

## Security
* Windows (Negotiate) auth; fallback policy requires authentication. Policies: *Registered*, *Host*, *Admin*. Unregistered users are redirected to `/Register`; hub calls return 403.
* Host methods carry `[Authorize(Policy=Host)]` **and** the service verifies `room.HostId == caller`.
* Anti-forgery on all forms (Razor tag helpers). Output encoded (`T.esc`, Razor).
* `AuthMode=Dev` (cookie login) only when explicitly configured; logs a warning at start-up.
* PII stored: Windows account, name, email, employee code.

## Reliability
State is written to SQLite on every event. Clients rebuild from `JoinRoom` snapshot after reconnect. `AutoCallerService` reads due rooms from the DB every second, so Auto games survive restarts. Single instance by design (SQLite).

## Known limitations / future
* Email OTP verification not implemented (domain/regex validation only).
* `EnsureCreated` (no EF migrations yet) — add migrations before the first schema change after go-live.
* Full 6-ticket sets and ticket "price" are not implemented.
* Load test (200 users) not yet executed.
