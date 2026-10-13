# Deployment & Operations Guide

## 1. Requirements
Choose one hosting style (the same build runs on both; only `Tambola:AuthMode` differs):

| | **A. Windows / IIS** | **B. Linux** |
|---|---|---|
| Sign-in | Windows Authentication (automatic, no password) | Domain + User ID + password pages built into the app |
| `Tambola:AuthMode` | `Windows` | `Manual` |
| Needs | Windows Server joined to the domain, IIS + **ASP.NET Core Hosting Bundle (.NET 10)**, WebSocket Protocol enabled (*Server Manager → Add roles → Web Server → Application Development → WebSocket Protocol*) | Any Linux server with systemd and a reverse proxy for HTTPS (Caddy or nginx) |

In both styles, set `Tambola:AllowedDomains` to the domains whose users may use the app (see §4).
* **Single instance only** (SQLite). Do not scale out / run two copies against one database.

## 2. Publish
```powershell
dotnet publish src\Tambola.Web -c Release -o publish
```
Copy `publish\` to the server, e.g. `C:\inetpub\tambola`.

## 3. IIS setup
1. New **Application Pool** → *No Managed Code*, identity = a service account with **Modify** rights on the `App_Data` and `logs` folders.
2. New **Site** pointing to the folder, HTTPS binding (company certificate).
3. Site → **Authentication**: enable **Windows Authentication**, disable Anonymous.
4. Provider order: *Negotiate* then *NTLM*.
5. Browse to the site; you should be sent to the registration page.

> IIS must run with `Tambola:AuthMode = "Windows"` (default). `"Dev"` enables a fake login page and must never be used on a server.

## 3b. Linux setup (`AuthMode = Manual`)
1. Publish for Linux (self-contained, so the server needs no .NET install):
   ```bash
   dotnet publish src/Tambola.Web -c Release -r linux-x64 --self-contained -o publish
   ```
2. Copy `publish/` to `/opt/tambola`; create `/var/lib/tambola` (database) and a service user `tambola` that owns it and `/opt/tambola/logs`.
3. `/etc/systemd/system/tambola.service`:
   ```ini
   [Unit]
   Description=Tambola
   After=network.target
   [Service]
   WorkingDirectory=/opt/tambola
   ExecStart=/opt/tambola/Tambola.Web
   User=tambola
   Restart=always
   Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
   Environment=Tambola__AuthMode=Manual
   Environment=Tambola__BehindProxy=true
   Environment=Tambola__AllowedDomains__0=CORP
   # systemd needs the backslash doubled
   Environment=Tambola__AdminUsers__0=CORP\\jane.doe
   Environment=ConnectionStrings__Tambola=Data Source=/var/lib/tambola/tambola.db;Default Timeout=30
   [Install]
   WantedBy=multi-user.target
   ```
4. Reverse proxy with automatic HTTPS (Caddy): `tambola.example.com { reverse_proxy 127.0.0.1:5000 }` — WebSockets are passed through automatically. (nginx needs the `Upgrade`/`Connection` headers for `/hubs/`.)
5. The person who registers with an `AdminUsers` account (e.g. `CORP\jane.doe`) is the Admin. In Manual mode the app refuses to start unless `AllowedDomains` has at least one domain.

## 4. Configuration (`appsettings.json` or environment variables)
| Key | Meaning | Default |
|---|---|---|
| `Tambola:AuthMode` | `Windows` (IIS), `Manual` (Linux sign-in pages) or `Dev` (test only) | `Windows` |
| `Tambola:AllowedDomains` | Domains that may use the app, e.g. `["CORP","SUBSIDIARY"]`. **Windows mode:** users from any other domain see an *Access not permitted* page. **Manual mode:** these are the choices in the sign-in/register dropdown (required). Empty = any domain (Windows mode only). | empty |
| `Tambola:PasswordMinLength` | Manual mode minimum password length | 8 |
| `Tambola:BehindProxy` | `true` when Caddy/nginx/a load balancer terminates HTTPS in front of the app (trusts `X-Forwarded-*`). Leave `false` if the app is reachable directly. | `false` |
| `Tambola:AdminUsers` | Accounts (`DOMAIN\user`) that are Admins, e.g. `["CORP\\jane.doe"]` | sample only — **change this** |
| `Tambola:AllowedEmailDomains` | e.g. `["company.com"]`; empty = any | empty |
| `Tambola:EmployeeCodeRegex` | Employee code format | `^[A-Za-z0-9\-]{3,20}$` |
| `Tambola:LateJoinMaxCalls` | Late ticket purchase allowed until N numbers called | 10 |
| `Tambola:DefaultMaxTicketsPerPlayer` | Default in Create Room | 3 |
| `Tambola:DefaultTheme` | `dark`, `light` or `system` for users who have not chosen | `dark` |
| `Tambola:DefaultSoundOn` | Number announcements + sound effects on by default | `true` |
| `ConnectionStrings:Tambola` | SQLite file | `Data Source=App_Data/tambola.db;Default Timeout=30` |

Environment-variable form: `Tambola__AdminUsers__0=CORP\jane.doe`.

## 5. Data & backup
* Database: `App_Data\tambola.db` (WAL mode: also `-wal`/`-shm` files while running).
* **Backup:** use the SQLite online backup so the copy is consistent:
  ```powershell
  sqlite3 App_Data\tambola.db ".backup 'D:\backup\tambola-$(Get-Date -f yyyyMMdd).db'"
  ```
  Schedule nightly with Task Scheduler. Or stop the app pool and copy the three files.
* The database is created automatically on first start. When a newer version adds columns (wrong-claim limit, game deletion, passwords) the app adds them automatically at start-up and keeps all existing data; games created by older versions keep the old "one wrong claim locks the prize" rule. Take a backup before upgrading.
* **Limits:** up to **1500 players per room**; load-test results and sizing advice are in [Load-Test-Report.md](Load-Test-Report.md).
* Logs: `logs\tambola-YYYYMMDD.log` (30 days kept). Health check: `GET /health`.

## 6. Restart behaviour
Game state lives in SQLite. After an app-pool recycle/restart, clients auto-reconnect and rebuild their screen; rooms in *Auto* calling continue automatically.

## 7. Troubleshooting
| Symptom | Fix |
|---|---|
| Everyone sees a credentials prompt | Site not in Local Intranet zone / not using the machine's FQDN; check SPN or use NTLM fallback. |
| 401 on `/hubs/game` | Windows Authentication not enabled for the site, or WebSockets disabled. |
| “database is locked” | A second instance or a file-copy backup is open; use `.backup`. |
| New user sees Register page forever | Account deactivated, or claims lookup failing — check the log. |
| *Access not permitted* page | The user's domain is not in `Tambola:AllowedDomains`. |
| Linux: app exits at start-up mentioning `AllowedDomains` | Add `Tambola__AllowedDomains__0=...`. |
| Linux: players can sign in but not connect, or the sign-in cookie is lost | Set `Tambola__BehindProxy=true` when behind an HTTPS proxy, and make sure the proxy forwards WebSockets. |
| Employee forgot password (Linux) | Admin → Hosts & Employees → *Reset password*, give the temporary password to the user. |
