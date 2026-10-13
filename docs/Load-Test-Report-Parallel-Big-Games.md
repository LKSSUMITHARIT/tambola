# Load Test Report: 2–3 Parallel Games, 1000+ Players Each

Date: 2026-10-13 · App version: v1.1 · Companion to [Load-Test-Report.md](Load-Test-Report.md)

## 1. Question
Usually one game is live. Occasionally 2–3 games run at the same time, each with 1000+ players. Will the app cope, and what should be done to have more headroom?

## 2. Short answer
**Yes.** The app handled the worst case tested: **3 games at once with 1500 players each (4500 users)**. Every number reached every player, and every prize went to exactly the right number of winners. Numbers arrived in well under 1 second (95 % within 64 ms, slowest single delivery 0.4 s).

The only things players will feel are two **rush moments**: everyone joining at the same instant (typically 3–4 s, worst case 9 s) and everyone pressing Claim at the same instant (about 1 s typical, worst case 3 s). Both are explained, with fixes, in §6.

Not yet proven: behaviour on your real server and with real browsers (see §7).

## 3. What was tested
| Item | Value |
|---|---|
| Server | The real v1.1 Release build on Kestrel, SQLite database, SignalR WebSockets |
| Players | Simulated: each opens its own connection, joins, buys 2 tickets, receives all 90 numbers, then claims **all 6 prize cases** (Four Corners, 3 Rows, Full House, Early Five) |
| Rush moments | All players join together, buy tickets together and claim each prize **at the same instant**, the harshest realistic pattern |
| Test machine | Intel i7-4770, 8 threads, 32 GB, which **also ran all the simulated players**, so the server had less than the whole machine |
| Call speed | Run 1: a number every 200 ms (stress). Run 2: a number every **3 s** (the real minimum) |
| Pass rule | No connection or operation failures, 100 % delivery, exact winner counts, number delivery p95 under 1 s |

## 4. Results
(ms; p95 means 95 out of 100 players were at least this fast.)

### 4.1 Reference: one game (the usual case)
| Game | Join p95 | Number delivery p95 | Claim p95 | Server peak CPU / memory | Result |
|---|---|---|---|---|---|
| 1 × 1000 | 2831 | 51 | 631 | 41 % / 511 MB | Pass |
| 1 × 1500 | 2657 | 59 | 664 | 56 % / 540 MB | Pass |

### 4.2 2–3 games in parallel, stress call speed (200 ms)
| Games × players | Total users | Join p95 (worst) | Buy ticket p95 | Number delivery p95 (worst) | Claim p95 (worst) | CPU peak / memory | Result |
|---|---|---|---|---|---|---|---|
| 2 × 1000 | 2002 | 3149 (5698) | 933 | 59 (214) | 1272 (1557) | 63 % / 652 MB | **Pass** |
| 3 × 1000 | 3003 | 2466 (5337) | 634 | 45 (366) | 997 (1848) | 44 % / 625 MB | **Pass** |
| 2 × 1500 | 3002 | 4204 (7569) | 710 | 64 (250) | 868 (1381) | 61 % / 643 MB | **Pass** |
| 3 × 1500 | 4503 | 2880 (9392) | 567 | 61 (363) | 1063 (2049) | 53 % / 719 MB | 1 of 4503 connections failed* |

### 4.3 3 games in parallel at the real call speed (3 s per number)
| Games × players | Total users | Join p95 (worst) | Buy ticket p95 | Number delivery p95 (worst) | Claim p95 (worst) | CPU peak / avg / memory | Result |
|---|---|---|---|---|---|---|---|
| 3 × 1000 | 3003 | 2353 (4811) | 906 | 51 (380) | 1019 (3098) | 47 % / 4 % / 549 MB | 1 of 3003 connections failed* |
| 3 × 1500 | 4503 | 3595 (7446) | 960 | 55 (404) | 935 (1887) | 50 % / 6 % / 670 MB | **Pass** |

\* **About the two single connection failures.** One user out of thousands could not open a connection. In the 3 × 1000 run the message was *"Only one usage of each socket address is normally permitted"*, which is the **test PC running out of local ports** when opening thousands of connections in a second; it is not a server fault. The other one (3 × 1500 stress run) was a generic "WebSockets failed"; the same 4503-user scenario then passed cleanly in run 2 with zero failures, which points the same way, but I did not prove the cause. Real users connect from many different PCs, so this port limit does not apply to them, though a single user refreshing can still occasionally need a retry (the page reconnects automatically).

Everything else in those runs was perfect: **100 % of 270 000–405 000 number deliveries arrived, and winner counts were exact in every run.**

## 5. What this means for your usage
| Situation | What players will experience |
|---|---|
| One game, 1000–1500 players (most of the time) | Numbers appear instantly (under 0.1 s). Joining at the same moment takes ~3 s; a simultaneous Claim rush answers in ~1 s. |
| 2–3 games at once, 1000–1500 each | Same as above. Number delivery did **not** get slower with more games (59 ms vs 51 ms). Join rushes are 3–4 s typical, with a few players waiting up to ~9 s. Claim rush ~1 s typical, a few up to 3 s. |
| Game running (calling numbers, 3 s apart) | The server is almost idle: **average CPU 4–6 %**. The load is almost entirely in the join and claim rushes, which last seconds. |

Headroom: at 4500 users the server used at most 63 % CPU of the test PC (while also running the simulated players) and 720 MB of memory. Memory grows roughly 100 MB per 1000 users on top of ~150 MB base.

## 6. How to get more capacity or smoother rushes
In order of benefit for your case:

1. **Spread the join rush (no code).** Share the room code 5–10 minutes before the start, and have the host keep the room open in the lobby. Players then join over minutes, not within one second, and the 3–9 s waits disappear. This is the cheapest and biggest improvement.
2. **Give the server real CPU.** Use a dedicated server with **4+ cores**, fast SSD, and nothing else heavy on it. The rushes are CPU- and disk-bound (SQLite write per join, ticket and claim). The Hostinger VPS currently held (KVM 1: 1 core, 4 GB) is the **weakest option**; it was not tested and I would not promise 3 × 1500 on it. A 4-core plan is the sensible minimum for this use.
3. **Make the claim rush smoother (code).** Keep each prize's winner state in memory and write results to the database in batches, so 1500 simultaneous claims do not each wait for their own database write. Expected: claim worst case from ~3 s to under 1 s.
4. **Make the join rush smoother (code).** Skip the per-join player-count/list update for rooms above a few hundred players (send a count every second instead), and open the page's data requests lazily. Expected: join rush from ~3 s to ~1 s.
5. **Use a bigger database engine (code, larger change).** If you ever need more than ~4500 users at the same instant, or several unrelated big events, move from SQLite to SQL Server or PostgreSQL. Not needed for 2–3 games.
6. **Split very large events.** Beyond one room of 1500, run a second room (the cap is per room). Several rooms in parallel were shown to work.
7. **Scale-out (largest change).** Multiple servers need a shared database plus a SignalR backplane (Redis). Only worth it for 10 000+ simultaneous users.

## 7. What this test does not prove (read before relying on it)
* **Different machine.** Your target server (IIS Xeon Gold 5315Y 4 cores, or a Linux VPS) is not the machine tested. Per-core speed is probably better than the test PC, but with only 4 threads against my 8. Treat these numbers as **indicative**, and re-run the same test on the real server (the tool is ready: `tests/Tambola.LoadTest`).
* **Real browsers and networks.** Simulated players are lean connections. Real browsers render a 1–90 board, play sounds and sit behind office Wi-Fi/VPN. Expect somewhat larger waits from the network. Egress bandwidth and reverse-proxy limits were not measured.
* **Windows login / password login cost.** Each Linux password sign-in runs a deliberately slow password hash, and Windows sign-in runs Kerberos. 1500 people signing in within the same few seconds was not tested. Have people sign in earlier than the game, not at the start.
* **One run per scenario.** The two failed connections show some run-to-run variation. Repeat runs would give ranges.
* **Manual number-cutting.** The runs used automatic cutting; manual-cut rooms add one small request per tap per player.

## 8. Recommended go-live plan
1. Decide the server (4+ cores recommended) and deploy.
2. Run this same load test against it from a separate machine (3 × 1500 and 1 × 1500). Pass criteria in §3.
3. Hold a rehearsal with 200–300 real users, then one real event with one game before the first double-game event.
4. During events, watch CPU and memory. If CPU stays above ~80 % for more than a few seconds, or number delivery slows above 1 s, stop admitting players to new rooms.
