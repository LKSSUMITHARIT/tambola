# Tambola Load Test Report

Date: 2026-10-12 (baseline, v1.0) and 2026-10-13 (v1.1 re-test, §7) · Requirement under test: NFR-1 (≥ 200 concurrent players across ≥ 10 rooms, number-call latency < 1 s)

> **Update 2026-10-13:** v1.1 raised the room cap to 1500 and fixed the main bottlenecks listed in §5. Every scenario now passes, including 1 × 1500 and 40 × 100 (4040 users). See **§7** for the before/after numbers. Sections 1–6 describe the original v1.0 baseline.

## 1. Setup
| Item | Value |
|---|---|
| Server | Published Release build, real Kestrel process, SQLite (WAL), SignalR over **WebSockets**, Dev auth |
| Load generator | `tests/Tambola.LoadTest` — one real SignalR client per simulated user, same machine |
| Machine | Intel i7-4770 (8 logical cores), 32 GB RAM, Windows 11. **Server and clients share this box**, so the figures are conservative |
| Per player | Connect → Join → buy 2 tickets → receive all 90 numbers → claim every prize |
| Number calls | Host calls all 90 numbers, one every **200 ms** (production minimum interval is 3 s, so this is ~15× harsher on call rate) |
| Prize cases | All 6 patterns (Four Corners, First/Second/Third Row, Full House, Early Five), 5 winners each. For each pattern **every player claims at the same instant** |
| Pass rule | 0 connect/op failures, 100 % number delivery, exactly 5 winners per prize per room, broadcast p95 < 1000 ms, server alive |

Re-run: publish `src/Tambola.Web` Release, then `dotnet run -c Release --project tests/Tambola.LoadTest -- --server <publish dir> --out <dir> [--only A3,B2] [--gap 200]`.

## 2. Results — single game (one room)
Latencies in ms (p50 / p95).

| ID | Players | Connect | Join | Buy ticket | Number broadcast | Claim (all at once) | Server CPU peak / mem | Result |
|---|---|---|---|---|---|---|---|---|
| A1 | 50 | 225 / 242 | 783 / 1201 | 191 / 242 | 7 / 20 | 103 / 232 | 36 % / 206 MB | PASS |
| A2 | 100 | 569 / 603 | 1216 / 1263 | 404 / 439 | 7 / 21 | 174 / 396 | 41 % / 220 MB | PASS |
| A3 | 200 | 137 / 599 | 1830 / 1864 | 843 / 926 | 10 / 24 | 313 / 639 | 32 % / 260 MB | PASS |
| A4 | 400 | 152 / 347 | 1743 / 2555 | 849 / 900 | 15 / 47 | 587 / 948 | 44 % / 356 MB | PASS |
| A5 | 700 | 152 / 352 | 4337 / 7471 | 625 / 753 | 14 / 35 | 874 / 1086 | 37 % / 471 MB | PASS |
| A6 | 1000 | 160 / 323 | 5436 / 12969 | 823 / 1174 | 49 / 358 | 943 / 4940 | 53 % / 609 MB | PASS (degraded) |

**Single-game capacity:** 1000 players is the hard limit (`MaxPlayers` is clamped to 1000 in `GameService.CreateRoomAsync`). Everything stayed correct up to 1000 — no failures, 100 % delivery, correct winner counts. Number broadcast is fast at every size. Sizes up to ~400 feel instant; at 700+ the **join rush** (5–13 s) and, at 1000, the **simultaneous-claim rush** (p95 ≈ 5 s) become noticeable.

## 3. Results — many games at once
| ID | Rooms × players | Total clients | Number broadcast p50 / p95 / max | Join p95 | Claim p95 | Server CPU peak / mem | Result |
|---|---|---|---|---|---|---|---|
| B1 | 10 × 20 | 210 | 35 / 1095 / 3589 | 2311 | 2992 | 38 % / 253 MB | FAIL (p95 just over 1 s) |
| B2 | 10 × 50 | 510 | 18 / 675 / 4125 | 5694 | 3527 | 34 % / 312 MB | PASS |
| B3 | 10 × 100 | 1010 | 16 / 334 / 1920 | 5855 | 2658 | 65 % / 437 MB | PASS |
| B4 | 20 × 100 | 2020 | 8 / 321 / 2765 | 6784 | 2867 | 66 % / 533 MB | PASS |
| B5 | 40 × 100 | 4040 | 396 / 2606 / 8113 | 7245 | 3011 | 74 % / 627 MB | FAIL (p95 2.6 s, 3 client connect errors) |

Notes:
* All 100 % delivered, all claim counts exactly right in every run, server never crashed or leaked (≤ 627 MB).
* **B1 is an unstable result, not a smaller-is-worse trend:** 10 hosts each firing a call every 200 ms makes 50 calls/s, and each call is a SQLite write. Rooms contend on the single DB writer, which produced the spikes. B2–B4 at the same or higher call rate passed, so B1 looks like run-to-run variance (possibly JIT/cold start as the first multi-room run); this was not isolated. Treat B1 as "borderline at 200 ms cadence", not as proof that 210 users fail.
* B5's 3 connect failures were *client-side* ephemeral-port exhaustion (`Only one usage of each socket address`) from opening 4000+ loopback sockets, not a server fault.

## 4. Capacity summary
| Case | Verified OK (this machine, 200 ms call rate) | Comfortable (all steps < ~2 s) | Limit seen |
|---|---|---|---|
| One game | 1000 players (hard cap) | ≤ 400 players | Join and claim rushes at 700–1000 |
| Many games | 20 rooms × 100 = 2000 players | ≤ ~10 rooms × 100 (1000) | 40 rooms / 4000 users: broadcast p95 2.6 s |
| NFR-1 (200 players, 10 rooms) | **Met** (B2: 500 players/10 rooms passed; A3 200 in one room) | | |

At the real 3 s minimum call interval the call/write rate is ~15× lower, so real capacity should be higher than the multi-room numbers above; this was not separately measured.

## 5. Where the time goes (from the code) and how to raise capacity
1. **Join rush (A5/A6: 5–13 s).** `JoinAsync` ends with `BroadcastPlayers`, which sends the *whole player list* to the whole room on every join → O(N²) data. *Fix:* send only the joining player as a delta (`PlayerJoined`), or debounce the full-list broadcast to once per 500 ms–1 s. Expect large gains for 700–1000 players.
2. **Buy-ticket cost grows with room size.** `BuyTicketAsync` loads every ticket grid in the room to check uniqueness (O(N)) and counts tickets again for the serial. *Fix:* rely on a unique index on (RoomId, Grid) and catch the conflict, keep a per-room counter, and cache grids in memory for the room.
3. **Claim rush at 1000 (p95 ≈ 5 s).** Claims run under a per-room lock and each reloads the room (`LoadRoom` with prizes + winners) and writes to SQLite, then broadcasts prizes and feed. *Fix:* cache the room, called numbers, and prize state in memory (invalidate on change), validate claims against the cache, and persist writes in one batched transaction; coalesce `PrizesChanged`/`Feed` broadcasts.
4. **Many rooms contend on one SQLite writer (B1, B5).** Every number call, join, ticket and claim is a DB write. *Fixes, in order of effort:* (a) enable `PRAGMA synchronous=NORMAL` (safe in WAL) and a larger `busy_timeout`; (b) write called numbers in batches / keep draw state in memory and persist asynchronously; (c) move to SQL Server or PostgreSQL if sustained load beyond ~2000 simultaneous players is expected.
5. **Per-room hard cap of 1000.** Raise the `Math.Clamp(..., 1, 1000)` limit only after fixes 1–3, and re-run A6 at 1500–2000.
6. **Hardware / hosting.** Server peaked at 74 % CPU while *sharing* the box with the load generator and used < 650 MB, so the app itself has headroom; a dedicated server with 4+ cores, Server GC enabled (`<ServerGarbageCollector>true`) and fast SSD for the SQLite file will beat these numbers. Put clients through the corporate LAN/VPN with WebSockets allowed (avoid long polling), and keep request logging at Warning.
7. **Scale-out beyond one machine.** Today all state lives in one process plus one SQLite file. For multiple servers you need a shared database plus a SignalR backplane (Redis or Azure SignalR) and the per-room lock moved to a distributed lock; at that point capacity scales roughly linearly with servers.
8. **Test-harness limits.** To test beyond ~4000 clients, run the load generator from several machines (loopback exhausts ephemeral ports) and widen the port range.

## 6. Caveats
* Client and server on one machine; real LAN/VPN latency adds to every figure.
* Dev authentication was used, so Windows-auth handshake cost is not included (it only affects connect time).
* Players did not manually mark numbers (rooms used Auto cut mode); manual-cut rooms add `Mark` calls.
* One run per scenario; B1's borderline result shows run-to-run variance. Repeat runs before treating a single number as a promise.

## 7. v1.1 re-test (2026-10-13)
Same machine, same harness and settings as §1 (200 ms call rate, all 6 prize cases, everyone claiming at once). New scenario **A7: one room of 1500 players**. v1.1 changes under test: join sent as a single-player delta instead of the full player list, ticket purchase without loading every grid (unique index + retry), called numbers cached per room, lighter room loads for join/ticket/claim, `PRAGMA synchronous=NORMAL`.

**Result: 12 / 12 scenarios pass** (0 connect failures, 0 operation failures, 100 % number delivery, exact winner counts, broadcast p95 < 1 s everywhere).

### Single game (p50 / p95 ms)
| Players | Join v1.0 → v1.1 | Buy ticket v1.0 → v1.1 | Number broadcast v1.0 → v1.1 | Claim (all at once) v1.0 → v1.1 | Result |
|---|---|---|---|---|---|
| 50 | 783/1201 → 746/1199 | 191/242 → 166/240 | 7/20 → 5/13 | 103/232 → 67/177 | PASS |
| 200 | 1830/1864 → 1516/1567 | 843/926 → 608/735 | 10/24 → 6/19 | 313/639 → 218/427 | PASS |
| 400 | 1743/2555 → 1789/3456 | 849/900 → 623/714 | 15/47 → 11/29 | 587/948 → 420/880 | PASS |
| 700 | 4337/7471 → 1452/2372 | 625/753 → 600/728 | 14/35 → 13/33 | 874/1086 → 470/592 | PASS |
| 1000 | 5436/12969 → 1639/2831 | 823/1174 → 445/1276 | 49/358 → 23/51 | 943/4940 → 497/631 | PASS |
| **1500** (new) | — → 1788/2657 | — → 445/544 | — → 30/59 | — → 527/664 | **PASS** |

At 1000 players the join rush dropped from 13 s to 2.8 s (p95) and the simultaneous claim rush from 4.9 s to 0.6 s. A full room of 1500 used 56 % peak CPU and 540 MB on the test PC, which also ran the 1500 clients.

### Many games at once
| Rooms × players | Total | Broadcast p95 v1.0 → v1.1 | Claim p95 v1.0 → v1.1 | Server CPU peak / mem (v1.1) | Result v1.0 → v1.1 |
|---|---|---|---|---|---|
| 10 × 20 | 210 | 1095 → 167 | 2992 → 1173 | 42 % / 236 MB | FAIL → PASS |
| 10 × 50 | 510 | 675 → 160 | 3527 → 1235 | 38 % / 322 MB | PASS → PASS |
| 10 × 100 | 1010 | 334 → 161 | 2658 → 1996 | 32 % / 430 MB | PASS → PASS |
| 20 × 100 | 2020 | 321 → 315 | 2867 → 1925 | 49 % / 543 MB | PASS → PASS |
| 40 × 100 | 4040 | 2606 → 671 | 3011 → 1752 | 61 % / 719 MB | FAIL → PASS |

The 3 client connection errors seen at 4040 users in v1.0 did not recur (same harness, so they were a local-port limit that varies from run to run, not a server fault).

### Remaining weak spots and what to do next
* **Claim rush with many rooms** (10 × 100: p95 2.0 s) and ticket purchase at 4000 users (p95 1.2–1.6 s): every claim/purchase is still a SQLite write on one writer. Next steps: batch claim writes, cache prize and winner state in memory, or move to SQL Server/PostgreSQL if sustained load beyond ~4000 users is needed.
* **Join rush** p95 is 2–4 s for 400–1500 players joining at once. A coalesced "player count" update instead of per-join events would remove most of it.
* Single run per scenario, one machine, Dev auth, 200 ms call interval (real minimum is 3 s) — numbers are indicative, not a guarantee. Re-run on the real target server before go-live.
* Not tested: Manual-mode login cost (each sign-in runs a 210 000-iteration PBKDF2 hash, a CPU cost I did not measure; sign-ins are not part of the game loop, but a few hundred people signing in within seconds should be tried on the real server), and browsers (the load tool drives the hub directly).
