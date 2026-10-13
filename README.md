# 🎯 Tambola (Internal)

Real-time online Tambola (Housie) for **internal employees only**. ASP.NET Core 10 · SignalR · SQLite · Windows Authentication · Razor Pages + jQuery (no React/Angular).

| Doc | Purpose |
|---|---|
| [docs/User-Manual.md](docs/User-Manual.md) | Help manual for Players, Hosts and Admins |
| [docs/Deployment-Guide.md](docs/Deployment-Guide.md) | Install on IIS / Linux, configuration, backup |
| [docs/Manual-Server-Deployment-Steps.md](docs/Manual-Server-Deployment-Steps.md) | Step-by-step manual deployment on the Hostinger VPS |
| [docs/GitHub-and-CI-CD-Guide.md](docs/GitHub-and-CI-CD-Guide.md) | Put the code on GitHub, CI build/test, release and deploy pipeline |
| [docs/Load-Test-Report.md](docs/Load-Test-Report.md) | Load test results and capacity advice |
| [docs/Architecture.md](docs/Architecture.md) | Design, data model, SignalR contract, security |
| [ClaudePlans/01-Requirements.md](ClaudePlans/01-Requirements.md) | Requirements |
| [ClaudePlans/02-Development-Plan.md](ClaudePlans/02-Development-Plan.md) | Phased plan |
| [ClaudePlans/PROGRESS.md](ClaudePlans/PROGRESS.md) | Progress log & decisions |
| [UIMockups/index.html](UIMockups/index.html) | Original static UI mockups |

## Quick start (developer)

```powershell
cd src\Tambola.Web
dotnet run                      # Development => AuthMode "Dev" (fake login page), http://localhost:5146
```
Open the site → **Dev login** → pick `CORP\admin.user` → register → *Hosts & Employees* → **Make Host** (yourself) → **Create Room**.
Open more browser profiles/incognito windows and sign in as other dev users (`CORP\anil.kumar` …) to play.

> `AuthMode=Dev` exists **only** for local testing. Production uses Windows Authentication (`AuthMode=Windows`, the default in `appsettings.json`).

## Solution layout
```
src/Tambola.Domain   Entities, ticket generator (CSPRNG), winning-pattern strategies   (no framework dependencies)
src/Tambola.Web      ASP.NET Core: Razor Pages, SignalR hub, EF Core+SQLite, auth, services, wwwroot (jQuery, signalr.js)
tests/Tambola.Tests  xUnit: domain tests + end-to-end game flow through the real hub (25 tests)
tests/Tambola.LoadTest  Load tester: starts the real server and drives it with thousands of SignalR clients
deploy/              remote-deploy.sh used by the CD pipeline (.github/workflows)
docs/                Manuals and architecture
ClaudePlans/         Requirements, plan, progress
UIMockups/           Static HTML mockups (dummy data)
```

## Tests
```powershell
dotnet test            # 25 tests: ticket validity (10 000 tickets), patterns, registration rules,
                       # full game (quota, locking, bogey, late claim, auth), manual cut, auto caller
```

## Key rules (summary)
* One Employee Code ↔ one email ↔ one Windows account (unique indexes + validation).
* Admins come from `appsettings` (`Tambola:AdminUsers`); Admins appoint any number of Hosts.
* Prizes are winning cases (Four Corners, 1st/2nd/3rd Row, Full House, optional Early Five) with a **Host-set winner count each**; first N valid claims win, then the prize closes.
* Number calling and number cutting are each **Manual/Auto**, fixed by the Host per room. Claiming is always manual and validated on the server against the numbers actually called.
