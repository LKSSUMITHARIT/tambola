# Deployment Plan: Tambola on Hostinger VPS (tambola.sumitharit.com)

Status: **PLAN ONLY. Nothing has been changed** on DNS or the server. Prepared 2026-10-13 for Tambola v1.1.

## 1. What exists today (checked read-only)
| Item | Finding |
|---|---|
| VPS | Hostinger **KVM 1**, id 1122934, `srv1122934.hstgr.cloud`, Ubuntu 26.04 LTS, **1 vCPU, 4 GB RAM, 50 GB disk**, IPv4 `72.61.172.114`, IPv6 `2a02:4780:12:757c::1`, state *running* |
| Server state | Fresh: ports **80 and 443 closed** (nothing listening), SSH (22) open, **no Hostinger firewall group** attached |
| SSH access | The VPS has one root key (its own, `root@srv1122934`). **This PC has no SSH key**, so a key must be created and added (§4, step 1) |
| DNS | Zone `sumitharit.com` is managed in Hostinger (API works). `tambola.sumitharit.com` **does not exist yet**. No CAA records, so Let's Encrypt is not blocked. Apex/www/admin/hrdashboard records point to GitHub Pages and must stay untouched |
| App | `Tambola.Web`, .NET 10, SQLite, SignalR. Build as `linux-x64` self-contained (no .NET needed on server). Run in `AuthMode=Manual` |

## 2. Target design
```
Browser ──HTTPS 443──► Caddy (free Let's Encrypt cert, auto-renew) ──► Tambola (Kestrel, 127.0.0.1:5000)
                                                                          └─ SQLite  /var/lib/tambola/App_Data/tambola.db
```
* **Website folder:** `/opt/tambola/releases/<timestamp>/` holds each uploaded build; `/opt/tambola/current` is a symlink to the live one (instant rollback = repoint symlink and restart).
* **Data:** `/var/lib/tambola` (database, logs). **Never inside a release folder**, so redeploys do not touch it.
* **Service:** systemd unit `tambola` (runs as unprivileged user `tambola`, restarts on failure, starts on boot).
* **TLS:** **Caddy** terminates HTTPS, obtains and **renews certificates automatically** (no cron needed), forwards WebSockets (SignalR) with no extra config. Fallback if ever needed: nginx + certbot (systemd timer).
* **Firewall:** `ufw` on the server allowing only 22, 80, 443.

## 3a. Decisions received (2026-10-13)
| Input | Answer |
|---|---|
| 1 Server size | Deploy on KVM 1 now; load test; upgrade later only if needed |
| 2 Allowed domains | `KEYWORDSINTL` and `Custom` (two entries in the dropdown) |
| 3 Admin account(s) | **Still needed**, as `DOMAIN\userid` (e.g. `KEYWORDSINTL\sumit.harit`) |
| 4 Time zone | `Asia/Kolkata` (IST) |
| 5 Certificate email | none |
| 6 SSH key via Hostinger API | Approved |
| 7 to 8 (IPv6, backups) | Defaults: A record only; local nightly backup, 14 days |

**Blocking item before execution:** the first Admin's `DOMAIN\userid`, and your go-ahead to start Phase 1 (DNS).

## 3. What I need from you (decisions / inputs)
| # | Needed | Why | My default if you say "use defaults" |
|---|---|---|---|
| 1 | **Server size.** KVM 1 (1 core / 4 GB) is the minimum that runs the app but is **weak for 1000+ players** (load tests assumed 4+ cores; see Parallel-Big-Games report). Upgrade now or later? | Capacity | Deploy on KVM 1 now, test, upgrade the plan from hPanel later (an upgrade should keep the server; I cannot buy or upgrade plans for you) |
| 2 | **Allowed domains** (e.g. `CORP`, `SUBSIDIARY`): the names shown in the login dropdown | `Tambola:AllowedDomains` (required in Manual mode) | none, you must tell me |
| 3 | **Admin account(s)** as `DOMAIN\userid` (e.g. `CORP\sumit.harit`) | `Tambola:AdminUsers`; the first person to register with this ID becomes Admin | none, you must tell me |
| 4 | **Time zone** of the server (the app shows dates in the server's zone) | Correct times on History/Results | `Asia/Kolkata` (IST) |
| 5 | **Email for certificate notices** (Let's Encrypt warns if renewal ever fails) | TLS | none (optional; skipped) |
| 6 | **OK to add an SSH key to the VPS** via the Hostinger API (I create the key pair on this PC; the private key never leaves it) | I need shell access to install anything | yes |
| 7 | **IPv6 record**: also add `AAAA tambola → 2a02:4780:12:757c::1`? | Some users connect over IPv6 | No (A record only; less to go wrong) |
| 8 | **Backups**: nightly local copy only, or also off-server (e.g. your drive/S3)? Hostinger VPS backups/snapshots are separate and are not under my control | Data safety | Local nightly backup, 14 days kept; you enable Hostinger's own backup/snapshot in hPanel |

Approvals I will ask for at the moment they are needed (each is an outward-facing change): create the DNS record; add the SSH key; open ports/firewall; install packages on the server; start the service.

## 4. Step-by-step plan

### Phase 0: Preparation on this PC (no server changes)  ~15 min
1. Run `dotnet test` (25 must pass).
2. `dotnet publish src/Tambola.Web -c Release -r linux-x64 --self-contained -o <scratch>/publish` and check the output contains `Tambola.Web` (Linux executable) and `wwwroot/`.
3. Create the files that make deploys repeatable, in a new `deploy/` folder: `tambola.service`, `Caddyfile`, `backup.sh` (+ systemd timer), `deploy.ps1` (build, upload, switch, restart, health check, auto-rollback).
4. Create the SSH key pair (`ssh-keygen -t ed25519`) in `~/.ssh/tambola_vps`.

### Phase 1: DNS  ~5 min + propagation (usually minutes, up to 1 h)
1. Validate then add **`A tambola → 72.61.172.114`, TTL 300** using the Hostinger DNS API. Use **append mode** (`overwrite=false`) so every existing record (GitHub Pages A records, MX/Zoho mail, CNAMEs, TXT) is left exactly as is.
2. Re-read the zone and confirm only one record was added.
3. Verify from this PC and from a public resolver: `nslookup tambola.sumitharit.com` returns `72.61.172.114`. **Do not continue to TLS until this resolves**, because Let's Encrypt must reach the server by name.
* Rollback: delete that single A record.

### Phase 2: Server access and base hardening  ~20 min
1. Add the public key to the VPS (`vps_ssh-keys_add-virtual-machine`); test `ssh -i ~/.ssh/tambola_vps root@72.61.172.114`.
2. `apt update && apt -y upgrade`; reboot if the kernel changed; set the time zone (input 4); `timedatectl set-ntp true`.
3. Install: `ufw`, `fail2ban`, `unattended-upgrades`, `libicu` (runtime library for the self-contained app), `sqlite3` (backups), `curl`.
4. Add a **2 GB swap file** (the server has only 4 GB RAM; protects against out-of-memory kills during busy games).
5. `ufw`: default deny incoming, allow 22/tcp, 80/tcp, 443/tcp; enable. (Test SSH from a second window before closing the first.)
6. After confirming key login works, disable SSH password login (`PasswordAuthentication no`). Recovery path if ever locked out: Hostinger hPanel browser terminal.
* Rollback: `ufw disable`; hPanel console.

### Phase 3: Website folder + service  ~20 min
1. Create user and folders:
   `useradd --system --home /var/lib/tambola --shell /usr/sbin/nologin tambola`
   `/opt/tambola/releases`, `/var/lib/tambola/App_Data`, `/var/lib/tambola/logs` (owned by `tambola`).
2. Upload the build (`scp`/`rsync`) to `/opt/tambola/releases/<stamp>/`, `chmod +x Tambola.Web`, link `/opt/tambola/current`.
3. `/etc/systemd/system/tambola.service` (key points):
   * `ExecStart=/opt/tambola/current/Tambola.Web`, `User=tambola`, `Restart=always`
   * `WorkingDirectory=/var/lib/tambola` (so `App_Data/` and `logs/` land in the data folder)
   * `Environment=ASPNETCORE_CONTENTROOT=/opt/tambola/current` (so `wwwroot` is found even though the working directory differs)
   * `ASPNETCORE_URLS=http://127.0.0.1:5000`, `ASPNETCORE_ENVIRONMENT=Production`
   * `Tambola__AuthMode=Manual`, `Tambola__BehindProxy=true`, `Tambola__AllowedDomains__0..n` (input 2), `Tambola__AdminUsers__0..n` (input 3; backslash doubled in systemd), `ConnectionStrings__Tambola=Data Source=/var/lib/tambola/App_Data/tambola.db;Default Timeout=30`
   * Hardening: `NoNewPrivileges=true`, `ProtectSystem=strict`, `ReadWritePaths=/var/lib/tambola`, `PrivateTmp=true`
4. `systemctl enable --now tambola`; check `journalctl -u tambola` shows "Now listening on 127.0.0.1:5000" and `AuthMode=Manual`.
5. Verify locally: `curl http://127.0.0.1:5000/health` returns `{"status":"ok"}`.
* Rollback: `systemctl stop tambola`.

### Phase 4: HTTPS with free auto-renewing certificate  ~15 min
1. Install Caddy from its official apt repository.
2. `/etc/caddy/Caddyfile`:
   ```
   tambola.sumitharit.com {
       encode zstd gzip
       reverse_proxy 127.0.0.1:5000
       header { Strict-Transport-Security "max-age=31536000"; X-Content-Type-Options nosniff; Referrer-Policy strict-origin-when-cross-origin }
   }
   ```
   (Add `email you@example.com` in the global block if you gave one.)
3. `systemctl enable --now caddy`. Caddy requests the certificate from **Let's Encrypt** automatically (HTTP-01 on port 80), stores it in `/var/lib/caddy`, and **renews it itself about 30 days before expiry** (certificates last 90 days). HTTP is redirected to HTTPS automatically.
4. Verify: `curl -I https://tambola.sumitharit.com` returns 200/302 with a valid certificate; check the issuer and expiry with `openssl s_client`; confirm the renewal setup by looking at the Caddy log ("certificate obtained") and `systemctl is-enabled caddy` (renewal runs as long as Caddy runs and starts on boot).
* If issuance fails: check DNS has propagated, port 80/443 reachable from outside (`ufw`, Hostinger firewall), then `journalctl -u caddy`. Let's Encrypt allows 5 failed validations per hour, so I will not retry blindly.

### Phase 5: Application go-live checks  ~20 min
1. Open `https://tambola.sumitharit.com` in a browser: redirected to the sign-in page.
2. Register the Admin account (input 3), then: grant yourself Host, create a room, open a second browser/phone as a player, join, buy ticket, run a few number calls, claim, end the game, delete it; confirm numbers arrive live (WebSocket works through Caddy).
3. Check the Admin → Reports audit log shows the actions; check `journalctl` for errors.
4. Test the password reset and a deliberately blocked domain.

### Phase 6: Backups, monitoring, upgrade routine  ~20 min
1. **Backup:** systemd timer nightly 02:00 runs `sqlite3 tambola.db ".backup /var/backups/tambola/tambola-YYYYMMDD.db"`, gzip, keeps 14 days (consistent copy, safe while running). Optional off-server copy (input 8). Test a restore once.
2. **Health:** `GET /health`; set a free uptime monitor (e.g. UptimeRobot) on `https://tambola.sumitharit.com/health` with email alerts.
3. **OS updates:** `unattended-upgrades` for security patches; reboot window agreed with you (the app restarts itself; games in Auto call mode continue after restart).
4. **Certificate check:** monthly glance at expiry, or a monitor with SSL expiry alert.
5. **Deploy updates:** `deploy.ps1` = test, publish, upload to a new release folder, switch the symlink, restart, hit `/health`; if unhealthy, switch back automatically. The database upgrades itself on start (`SchemaUpgrader`); the backup runs before each deploy.

### Phase 7: Capacity sign-off (before a real big game)  ~1 h
* Run `tests/Tambola.LoadTest` against the live server from a **different machine** (not the server). Start with 1 × 500, then 1 × 1500; compare with the reports.
* On KVM 1 expect to need the plan upgrade for 1000+ players or 2–3 parallel games (input 1). Re-run after any upgrade.

## 5. Timeline
Phase 0 to 5 can be done in **one sitting of about 2 hours** once inputs 2, 3 (and ideally 1 and 4) are confirmed; DNS propagation is the only waiting. Phases 6 and 7 can follow the same day or before the first event.

## 6. Risks and mitigations
| Risk | Mitigation |
|---|---|
| Locked out of SSH after hardening | Test key login first; keep the first session open; hPanel browser terminal as recovery |
| Existing DNS broken by my change | Append-only add of a single record, zone re-read before/after; rollback = delete that record |
| Certificate not issued (DNS/port 80/rate limit) | Verify DNS and ports first; Caddy retries with back-off; fallback certbot |
| 1 vCPU / 4 GB too small under load | Swap file; monitor; upgrade plan (input 1); test before big events |
| Database lost (disk failure/mistake) | Nightly backup + Hostinger snapshot; deploys never touch `/var/lib/tambola` |
| Name-based admin in Manual mode | Admin is protected by a password; choose a strong one; reset is Admin-only |
| App update breaks the site | Release folders + symlink; auto-rollback in `deploy.ps1` |
| Server time zone differs from users | Set zone in Phase 2 (input 4) |

## 7. Success criteria
* `https://tambola.sumitharit.com` loads with a valid Let's Encrypt certificate and HTTP redirects to HTTPS.
* A full game (create, join, play, claim, end, delete) works from two different devices with live number updates.
* `systemctl` shows `tambola` and `caddy` enabled and running, and they come back after a reboot.
* A backup file exists and a test restore opens in `sqlite3`.
* GitHub Pages site (`sumitharit.com`, `www`, `admin`, `hrdashboard`) and email (Zoho MX) are unchanged.
