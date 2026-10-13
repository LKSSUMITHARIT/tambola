# Tambola (Internal) — Requirements Document

| Field | Value |
|---|---|
| Version | 0.1 (Draft for review) |
| Date | 2026-10-11 |
| Reference | https://github.com/harshitazzz/Tambola (React/Node/Socket.io/MongoDB — behaviour reference only, not reused code) |
| Status | Awaiting review of open questions (§13) |

## 1. Purpose
An internal, employee-only, real-time online Tambola (Housie) game. Hosts create rooms, employees join with a room code, receive digital tickets, and play with live number calling and automatic claim validation. Used for company events, team engagement and remote/hybrid gatherings.

## 2. Scope
**In scope:** employee identity via Windows Authentication, employee registration (EmployeeCode ↔ email), host role management, room/game lifecycle, ticket generation, live number calling, claim validation, prizes/winners, history & basic admin reports.
**Out of scope (v1):** real-money payments or wallets, public/external users, native mobile apps, video/voice chat, React/Angular front-end.

## 3. Technology Constraints (mandated)
| Area | Decision |
|---|---|
| Backend | ASP.NET Core (.NET 8 LTS), C# |
| Real-time | ASP.NET Core SignalR (WebSockets, fallback to SSE/long polling) |
| Storage | SQLite (via EF Core + Microsoft.Data.Sqlite), WAL mode |
| Auth | Windows Authentication (Negotiate/Kerberos/NTLM) — IIS or Kestrel with `Microsoft.AspNetCore.Authentication.Negotiate` |
| Front-end | Server-rendered Razor Pages + jQuery + Fluent UI Web Components (or Fluent 2 CSS). **No React/Angular.** |
| Hosting | Internal Windows server (IIS recommended); single instance (SQLite constraint) |
| Tests | xUnit (domain/service), integration tests with in-memory/temp SQLite |

## 4. Users & Roles
| Role | Description | Key abilities |
|---|---|---|
| **Admin** | Seeded from config (AD user/group). Manages hosts. | Assign/revoke Host role, view all rooms, deactivate employees, reports |
| **Host** | One or more employees designated by Admin | Create/configure room, start/pause/end game, call numbers (manual/auto), set prizes, remove players, see claims |
| **Player** | Any registered employee | Join room, get tickets, mark numbers, claim prizes |

A user may be Player and Host (not as a player in a room they host, by default — see Q4).

## 5. Functional Requirements

### 5.1 Authentication & Registration
- FR-1 Identity comes from Windows Auth (`DOMAIN\username`); no passwords stored. Display name resolved from AD/`Name`, falling back to the Windows username.
- FR-2 First visit shows a one-time **Registration** form: Employee Code + Email (Windows username pre-filled, read-only).
- FR-3 **One EmployeeCode per email, and one email per EmployeeCode** (unique constraints on both; also one Windows account ↔ one registration). Duplicates are rejected with a clear message.
- FR-4 Email must match the allowed company domain(s) (configurable). Email verification via one-time code is **optional** (Q2).
- FR-5 Employee Code format validated by configurable regex.
- FR-6 Unregistered users cannot access any game page; unauthenticated/non-domain users get 401.
- FR-7 Admin can deactivate an employee (blocks login to game) and correct a registration.

### 5.2 Host Management
- FR-8 Admins are defined in `appsettings` (users/AD group) and seeded on startup.
- FR-9 Admin screen: search registered employees, grant/revoke Host role (one or many hosts). Changes audited.
- FR-10 Only Host/Admin can see "Create Room".

### 5.3 Room Management
- FR-11 Host creates a room with: name, ticket price label (virtual points, optional), max tickets per player (default 3), max players, number-calling mode (manual/auto + interval 3–15 s), **number-cutting mode (manual/auto)**, prize table (§5.6), scheduled start (optional).
- FR-12 System generates a unique 6-character room code and a shareable link.
- FR-13 Room states: `Lobby → Running → Paused → Completed / Cancelled`.
- FR-14 Players join via code/link; join allowed in Lobby (and optionally late-join while Running, Q5).
- FR-15 Host can lock the room, remove a player, cancel the room.
- FR-16 Lobby shows live player list (SignalR presence).

### 5.4 Tickets
- FR-17 Standard Indian Tambola ticket: 3 rows × 9 columns, 15 numbers, 5 per row, ≥1 per column; column *c* holds numbers from 1–9, 10–19, … 80–90; numbers ascending within column.
- FR-18 Tickets are generated **server-side**, uniquely within a room, with a server-secret seed (no client generation).
- FR-19 Player "buys" tickets (virtual, free by default) up to the room limit before the game starts; tickets are final once the game starts.
- FR-20 Optional full set of 6 tickets covering 1–90 exactly (Q6).

### 5.5 Gameplay
- FR-21 Server owns the number draw: cryptographically random permutation of 1–90 created at game start; numbers revealed one at a time. Clients never receive uncalled numbers.
- FR-22 Manual mode: host presses "Call next". Auto mode: server timer calls at the configured interval; host can pause/resume.
- FR-23 Every client receives each call instantly via SignalR, with the called-number board (1–90 grid) and history.
- FR-24 Number announced by on-screen display and optional browser text-to-speech (per-user toggle) and sound effects.
- FR-25 **Number-cutting mode (per room, fixed by Host at creation):** *Manual* — player taps a called number on their ticket to cut it; *Auto* — the system cuts called numbers on every player's tickets instantly. Players cannot change the mode. In both modes the player must still press **Claim**. Manual cuts are validated against called numbers (a number not yet called cannot be cut).
- FR-26 Reconnect: refreshing/reconnecting restores full state (tickets, marks, called numbers, prizes) from SQLite.

### 5.6 Prizes & Claims
- FR-27 Prizes are **winning cases, not points**. Available cases: **Four Corners, 1st Row, 2nd Row, 3rd Row, Full House**, optional Early Five (extensible via Strategy pattern).
- FR-27a **Winner count per prize:** for each enabled case the Host sets the number of winners (≥1) at room creation, e.g. Full House = 1, Four Corners = 4, 1st Row = 5, 2nd Row = 5, 3rd Row = 5. Optional free-text reward description per prize (e.g. "Gift voucher"), informational only.
- FR-27b Winner counts/prizes are locked once the game starts (Host may edit in Lobby only).
- FR-28 Player presses **Claim** for a case; server validates against *called numbers only* (not player cuts), identical for Auto and Manual cutting.
- FR-29 Valid and prize still open → player is recorded as winner #k of N for that prize and it is broadcast; prize **closes** automatically when N winners are reached (Claim button disabled for it). Invalid → bogey (penalty per Q8).
- FR-30 **First N valid claims win**, ordered by server arrival time. One ticket/player can win several different prizes but at most one slot per prize (a player holding multiple tickets can win a given prize once).
- FR-31 Claims processed serially per room (concurrency-safe) so ordering and quota are exact.
- FR-32 Game auto-completes when every enabled prize has reached its winner count (Full House closing is typically last) or all 90 numbers are called. Host may also end it manually.

### 5.7 History & Reporting
- FR-33 Persist rooms, tickets, call sequence, claims, winners.
- FR-34 "My Games" page: past games, tickets, wins. Host: past rooms. Admin: leaderboard, participation counts, CSV export.
- FR-35 Audit log: registrations, role changes, room create/cancel, claim decisions.

### 5.6a Player Game Screen & Claim Experience
- FR-36 The Player Game Screen always shows: (a) **Current Number Called** — large, animated, with the previous few calls and call count (e.g. 23 of 90); (b) **the player's ticket(s)** with called numbers cut (auto or manual per room); (c) a **Claim panel** listing every enabled prize with its status; (d) the 1–90 called-numbers board.
- FR-37 **Claim panel:** one Claim button per prize, showing winners so far (e.g. "1st Row — 3/5 won"). A button is **locked/disabled** when: the player has already claimed or won that prize, the prize has reached its winner count (Closed), the player was barred from it after a bogey (per Q8), or the game is not Running. Locked buttons show the reason (e.g. "You won this", "Prize closed"). Lock state is driven by server state and survives refresh/reconnect.
- FR-38 Pressing Claim sends the request to the server, disables the button immediately (prevents double-click), and shows a pending state until the server answers.
- FR-39 **Server-side validation (authoritative):** the server re-checks game is Running, the player owns the ticket, the prize is still Open, the player has no existing win/claim for it, and the pattern is fully covered by numbers actually called so far. Client state is never trusted.
- FR-40 **On a successful claim** the player sees a congratulations popup/toast with confetti and sound — e.g. "🎉 Congratulations! You won *1st Row* (winner 3 of 5)" plus the reward text if set. All players see a live "X won 1st Row" announcement.
- FR-41 **On a claim that is valid but too late** (prize closed by other claims first) the player sees an encouraging speed-up message, e.g. "So close! Another player got there first — claim faster next time!".
- FR-42 **On an invalid claim** (bogey) the player sees "Not a valid claim yet — check your ticket" and the penalty per Q8 is applied.
- FR-43 Claim outcome messages are delivered via SignalR to the claimant (private) and prize status updates to the whole room, so every screen's Claim panel locks/updates in real time.

## 6. Non-Functional Requirements
| ID | Requirement |
|---|---|
| NFR-1 | Support ≥ 200 concurrent players across ≥ 10 rooms on a single instance; number-call latency < 1 s end-to-end on LAN/VPN |
| NFR-2 | Security: Windows Auth only, anti-forgery on forms, server-authoritative game logic, authorization on every hub method, input validation, no secrets in client |
| NFR-3 | Fairness: CSPRNG (`RandomNumberGenerator`) for draws and ticket generation; draw sequence not exposed until called |
| NFR-4 | Reliability: game state persisted after every event; app restart resumes active rooms; SQLite WAL + busy timeout; nightly DB backup |
| NFR-5 | Usability: responsive (desktop + mobile browser), keyboard accessible, WCAG 2.1 AA colour contrast, light/dark theme via Fluent tokens |
| NFR-6 | Browser support: current Edge/Chrome (corporate default); Firefox/Safari best-effort |
| NFR-7 | Observability: structured logging (Serilog → file), health endpoint, correlation of room/game id |
| NFR-8 | Maintainability: layered solution (Domain / Application / Infrastructure / Web), unit tests on ticket generator, claim strategies, draw logic ≥ 80 % coverage on Domain |
| NFR-9 | Data: PII limited to name, windows user, email, employee code; retention configurable (default 24 months) |
| NFR-10 | Deployment: single-folder publish, documented IIS setup, config via `appsettings` + environment variables |

## 7. High-Level Architecture
```
Browser (Razor Pages + jQuery + Fluent UI WC)
   │  HTTPS (Windows Auth)        │  SignalR WebSocket (/hubs/game)
   ▼                              ▼
ASP.NET Core Web  ── Application services (RoomService, TicketService, DrawService, ClaimService)
                       │  Domain (Ticket, Room, Game, Patterns/Strategy)
                       ▼
                 EF Core → SQLite (WAL)
Background: AutoCallerService (hosted service, per-room timers)
```
SignalR groups = room code. In-memory per-room lock/actor for serialized call and claim handling; SQLite is the durable source of truth.

## 8. Data Model (initial)
- **Employee**(Id, WindowsUser UNIQUE, EmployeeCode UNIQUE, Email UNIQUE, DisplayName, IsActive, RegisteredAt)
- **EmployeeRole**(EmployeeId, Role[Admin|Host], GrantedBy, GrantedAt)
- **Room**(Id, Code UNIQUE, Name, HostId, Status, Config JSON, DrawSeedEncrypted, CreatedAt, StartedAt, EndedAt)
- **RoomPlayer**(RoomId, EmployeeId, JoinedAt, IsRemoved)
- **Ticket**(Id, RoomId, EmployeeId, Grid JSON[3×9], Serial)
- **CalledNumber**(RoomId, Sequence, Number, CalledAt)
- **Claim**(Id, RoomId, TicketId, Pattern, ClaimedAtSeq, Result[Valid|Bogey], Reason, At)
- **Prize**(Id, RoomId, Pattern, WinnerCount, RewardText?, Status[Open|Closed])
- **PrizeWinner**(Id, PrizeId, Slot 1..N, EmployeeId, TicketId, AwardedAtSeq, AwardedAt) — UNIQUE(PrizeId, EmployeeId), UNIQUE(PrizeId, Slot)
- **AuditLog**(Id, At, Actor, Action, Details)

## 9. SignalR Contract (draft)
Client→Server: `JoinRoom(code)`, `LeaveRoom`, `BuyTickets(n)`, `MarkNumber(ticketId, n)`, `Claim(ticketId, pattern)`; Host only: `StartGame`, `CallNext`, `Pause`, `Resume`, `EndGame`, `RemovePlayer`.
Server→Client: `RoomState(snapshot)`, `PlayerJoined/Left`, `GameStarted`, `NumberCalled(seq, n)`, `ClaimResult(...)`, `PrizeAwarded(...)`, `GamePaused/Resumed`, `GameEnded(summary)`.

## 10. Key Screens
Register · Home/Dashboard (join by code, my games) · Host: Create Room, Host Console (call, board, claims, players) · Lobby · Player Game (current-number display, tickets, called-numbers board, prize/claim panel with lockable Claim buttons, congratulation / "be faster" messages — see §5.6a) · Results · Admin: Hosts, Employees, Reports, Audit.

## 11. Acceptance Criteria (samples)
- AC-1 Registering a second account with an existing EmployeeCode **or** email is rejected.
- AC-2 A non-host cannot invoke any host hub method or open host pages (403).
- AC-3 Every generated ticket satisfies FR-17 across 10 000 random generations (automated test).
- AC-4 A claim for a pattern not fully covered by called numbers is rejected as bogey regardless of client marks.
- AC-7 With 1st Row = 5 winners, the 5th valid claim wins and closes the prize; the 6th is rejected as "prize closed". Full House = 1 closes after one winner.
- AC-9 After a player wins or claims a prize, its Claim button is locked for that player (also after refresh); when a prize reaches its winner count it locks for everyone in real time.
- AC-10 A forged claim (tampered client, uncalled numbers, someone else's ticket, closed prize) is rejected by the server and never awards a prize.
- AC-11 A successful claim shows the congratulations message; a valid-but-late claim shows the "claim faster next time" message.
- AC-8 In an Auto-cut room, called numbers appear cut on all tickets without player action; in a Manual room they do not.
- AC-5 Restarting the app mid-game, clients reconnect and see identical state and the next call continues the same sequence.
- AC-6 No client network payload contains an uncalled number.

## 12. Assumptions & Risks
- Employees are on the corporate network/VPN with domain-joined devices (Windows Auth works seamlessly in Edge/Chrome; Firefox needs `network.negotiate-auth.trusted-uris`).
- SQLite ⇒ single server instance; scale-out needs DB change (acceptable for internal use).
- Windows Auth via Kestrel needs domain-joined host; IIS in-process hosting recommended.
- Risk: simultaneous-claim races → mitigated by per-room serialization.
- Risk: Fluent UI web components CDN is blocked internally → bundle assets locally (libman/npm copy).

## 13. Open Questions (defaults assumed if unanswered)
1. **Hosting**: IIS on a domain-joined server? *(default: IIS in-process)*
2. **Email verification** by OTP, or trust the entered email? *(default: domain check only; OTP phase optional)*
3. ~~Prizes~~ **Resolved:** case-based prizes with a winner count per case (no points).
4. Can a Host also play in their own room? *(default: no)*
5. Late join while running? *(default: allowed until a configurable call count, e.g. 10)*
6. Full 6-ticket set purchase option? *(default: no)*
7. ~~Auto-mark~~ **Resolved:** Host fixes Auto/Manual cutting per room; claim always manual.
8. Bogey penalty (lose claim rights for that prize)? *(default: yes)*
9. Branding (logo/colours), languages (English only?) *(default: neutral Fluent theme, English)*
10. Expected peak concurrent players? *(default: 200)*
11. AD group names for Admin seeding? *(needed at deploy time)*
