# Manual Deployment: Tambola on the Hostinger VPS (tambola.sumitharit.com)

You run every command yourself. Nothing here is executed by an assistant.
**Caveat:** I have not been able to run these commands on your server (your office network blocks SSH), so they are written carefully but **untested on Ubuntu 26.04**. Each step has a "Check" so you notice problems early. If a check does not match, stop and send me the output.

## 0. Where things stand
| Item | State |
|---|---|
| DNS record `tambola.sumitharit.com → 72.61.172.114` (TTL 300) | **Done** |
| VPS `srv1122934` (Ubuntu 26.04, 1 vCPU, 4 GB RAM, 50 GB disk) | Running, fresh. Ports 80/443 closed, no firewall |
| An SSH public key `tambola-deploy@DEL1-PWDPC-5281` was added to the server's root user | Done earlier (see §10 to remove it if you want) |
| App | Not on the server yet |

**Where you type the server commands:** Hostinger hPanel → **VPS → your server → Browser terminal** (you are `root`). It works from your office network because it connects from Hostinger's side.
Paste one block at a time and read the output before the next block.

**Settings used in this document**
| Setting | Value |
|---|---|
| Site name | `tambola.sumitharit.com` |
| Allowed domains (login dropdown) | `KEYWORDSINTL`, `Custom` |
| First Admin | `KEYWORDSINTL\sumit.harit` |
| Time zone | `Asia/Kolkata` |
| App folder / data folder | `/opt/tambola` / `/var/lib/tambola` |

---

## 1. On your PC: build the package (PowerShell)
```powershell
cd C:\Users\sumit.harit\Desktop\Claude_Projects\Tambola
dotnet test
New-Item -ItemType Directory -Force C:\temp | Out-Null
Remove-Item -Recurse -Force C:\temp\tambola-publish -ErrorAction SilentlyContinue
dotnet publish src\Tambola.Web -c Release -r linux-x64 --self-contained false -o C:\temp\tambola-publish
tar -czf C:\temp\tambola.tar.gz -C C:\temp\tambola-publish .
Get-FileHash C:\temp\tambola.tar.gz -Algorithm SHA256
Get-Item C:\temp\tambola.tar.gz | Select-Object Length
```
**Check:** `dotnet test` says 25 passed; the file `C:\temp\tambola.tar.gz` exists (roughly 10 MB). **Write down the SHA256 value.** You will compare it on the server.

*(This is a "framework-dependent" build: small, and the server installs the .NET runtime in §3, step S6, which also lets Ubuntu patch it.)*

## 2. Get the package onto the server
The server must be able to download it, because the browser terminal cannot receive file uploads. Choose **one** option.

### Option A: GitHub release (simplest, works from the office network)
1. On github.com create a repository, e.g. `tambola-release` (**Private** is fine; see A-private below).
2. Repo → **Releases → Draft a new release** → tag `v1.1` → attach `C:\temp\tambola.tar.gz` → **Publish release**.
3. If the repo is **public**: right-click the attached file → copy link address. Download on the server (§3, S8) with that link.
   If the repo is **private**: create a *fine-grained personal access token* (Settings → Developer settings → Fine-grained tokens → this repo only → **Contents: Read-only**) and use:
   ```bash
   TOKEN=YOUR_TOKEN
   REPO=OWNER/tambola-release        # your GitHub user/org and repo name
   ASSET_ID=$(curl -s -H "Authorization: Bearer $TOKEN" https://api.github.com/repos/$REPO/releases/tags/v1.1 \
     | python3 -c "import sys,json; print(json.load(sys.stdin)['assets'][0]['id'])")
   curl -fL -H "Authorization: Bearer $TOKEN" -H "Accept: application/octet-stream" \
     "https://api.github.com/repos/$REPO/releases/assets/$ASSET_ID" -o /root/tambola.tar.gz
   unset TOKEN
   ```
   (Simpler alternative: make the repo public just for the few minutes it takes, then delete the release. The package contains only compiled code and no passwords: settings are in the server's service file.)
4. **Delete the release (and token) after the download.**

### Option B: from a network where SSH works (mobile hotspot / home Wi-Fi)
```powershell
scp -i $env:USERPROFILE\.ssh\tambola_vps C:\temp\tambola.tar.gz root@72.61.172.114:/root/tambola.tar.gz
```
(The SSH key was already created on your PC and added to the server.)

### Option C: any other HTTPS link the server can download
Any temporary direct-download link (not a web page that needs a login) works the same way as A.

---

## 3. Server steps (Hostinger Browser terminal, as root)

### S1. Look around
```bash
whoami; lsb_release -ds; nproc; free -m; df -h /
getent hosts tambola.sumitharit.com
ss -ltnp | grep -E ':(80|443|5000)\b' || echo "ports 80/443/5000 are free"
```
**Check:** `root`, Ubuntu 26.04, `getent` shows `72.61.172.114`, ports free. If `getent` shows nothing, DNS has not reached the server yet. Wait a few minutes, do not continue to S10.

### S2. Update the system, set the time zone, install tools
```bash
export DEBIAN_FRONTEND=noninteractive
apt-get update && apt-get -y upgrade
timedatectl set-timezone Asia/Kolkata
timedatectl set-ntp true
apt-get install -y curl ca-certificates gnupg ufw fail2ban unattended-upgrades sqlite3 tar gzip libicu-dev
date
[ -f /var/run/reboot-required ] && echo ">>> REBOOT REQUIRED: run 'reboot', wait 1-2 minutes, reopen the terminal" || echo "no reboot needed"
```
**Check:** `date` shows IST (+0530). If a reboot is required, do it now, then continue from S3.

### S3. Add 2 GB swap (protects the 4 GB server from running out of memory)
```bash
if ! swapon --show | grep -q /swapfile; then
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  grep -q '/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
echo 'vm.swappiness=10' > /etc/sysctl.d/99-swappiness.conf && sysctl -p /etc/sysctl.d/99-swappiness.conf
free -m
```
**Check:** the `Swap:` line shows about 2047.

### S4. Firewall (only SSH, web and HTTPS)
```bash
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
ufw status verbose
```
**Check:** status active, with 22, 80, 443 allowed. (Port 5000 stays closed: only Caddy on the same machine reaches the app.)

### S5. Brute-force protection and automatic security updates
```bash
systemctl enable --now fail2ban
echo 'APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";' > /etc/apt/apt.conf.d/20auto-upgrades
systemctl enable --now unattended-upgrades
fail2ban-client status
```
**Check:** `Number of jail: 1` (sshd) or more.

### S6. Install the .NET 10 ASP.NET Core runtime
```bash
if apt-cache show aspnetcore-runtime-10.0 >/dev/null 2>&1; then
  apt-get install -y aspnetcore-runtime-10.0
else
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
  ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
fi
dotnet --list-runtimes
```
**Check:** lists `Microsoft.AspNetCore.App 10.0.x` and `Microsoft.NETCore.App 10.0.x`.
(If installed with the script, not apt, repeat the script monthly to pick up runtime security fixes.)

### S7. Create the service user and folders
```bash
id tambola >/dev/null 2>&1 || useradd --system --home-dir /var/lib/tambola --shell /usr/sbin/nologin tambola
mkdir -p /opt/tambola/releases /var/lib/tambola/App_Data /var/lib/tambola/logs /var/backups/tambola
chown -R tambola:tambola /var/lib/tambola /var/backups/tambola
chmod 750 /var/lib/tambola
ls -ld /opt/tambola /var/lib/tambola /var/backups/tambola
```

### S8. Download and unpack the app (the "website folder")
Get the file to `/root/tambola.tar.gz`:
* **Option A (public link):** `curl -fL "PASTE_THE_RELEASE_FILE_LINK_HERE" -o /root/tambola.tar.gz`
* **Option A (private):** the `curl` with the token from §2.
* **Option B:** already uploaded by `scp`.

Then:
```bash
sha256sum /root/tambola.tar.gz          # must equal the SHA256 you wrote down on your PC (compare ignoring upper/lower case)
REL=/opt/tambola/releases/$(date +%Y%m%d-%H%M%S)
mkdir -p "$REL" && tar -xzf /root/tambola.tar.gz -C "$REL"
test -f "$REL/Tambola.Web.dll" && test -d "$REL/wwwroot" && echo "package OK: $REL"
chown -R root:root /opt/tambola && chmod -R a+rX /opt/tambola
ln -sfn "$REL" /opt/tambola/current
ls -l /opt/tambola/current
```
**Check:** the hash matches, "package OK" is printed, and `current` points at your new folder.

### S9. Create and start the Tambola service
```bash
cat >/etc/systemd/system/tambola.service <<'EOF'
[Unit]
Description=Tambola (ASP.NET Core)
After=network.target

[Service]
Type=simple
User=tambola
Group=tambola
WorkingDirectory=/var/lib/tambola
ExecStart=/usr/bin/dotnet /opt/tambola/current/Tambola.Web.dll
Restart=always
RestartSec=5
KillSignal=SIGINT
TimeoutStopSec=30
SyslogIdentifier=tambola
LimitNOFILE=65535

Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_CONTENTROOT=/opt/tambola/current
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
Environment=Tambola__AuthMode=Manual
Environment=Tambola__BehindProxy=true
Environment=Tambola__AllowedDomains__0=KEYWORDSINTL
Environment=Tambola__AllowedDomains__1=Custom
Environment="Tambola__AdminUsers__0=KEYWORDSINTL\\sumit.harit"
Environment="ConnectionStrings__Tambola=Data Source=/var/lib/tambola/App_Data/tambola.db;Default Timeout=30"

NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/tambola

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now tambola
sleep 6
systemctl is-active tambola
journalctl -u tambola -n 25 --no-pager
curl -s http://127.0.0.1:5000/health; echo
systemctl show tambola -p Environment --no-pager | tr ' ' '\n' | grep -E "AdminUsers|AllowedDomains|AuthMode"
```
**Check:**
* `active`, and the log shows `AuthMode=Manual: ... Allowed domains: KEYWORDSINTL, Custom`. (A "No XML encryptor configured" warning is normal. The log does **not** print "Now listening", because the app's log settings hide that message.)
* The health call prints `{"status":"ok",...}`. This is the real proof that the app is serving.
* Every `Environment=` line whose value contains a **space** must be quoted as a whole, e.g. `Environment="ConnectionStrings__Tambola=Data Source=...;Default Timeout=30"`. Unquoted, systemd cuts the value at the first space and the app crashes at start-up with "Format of the initialization string does not conform to specification".
* The environment line shows `Tambola__AdminUsers__0=KEYWORDSINTL\sumit.harit` with **one** backslash. If it shows two (`\\`), change that line in the unit file to `KEYWORDSINTL\x5csumit.harit` (hex code for a backslash), then run `systemctl daemon-reload && systemctl restart tambola` and check again.
* If the service fails with a message about **ICU / globalization**, add the line `Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` under the other Environment lines, reload and restart.

### S10. Install Caddy (the web front door with free HTTPS)
```bash
apt-get install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' -o /etc/apt/sources.list.d/caddy-stable.list
chmod o+r /usr/share/keyrings/caddy-stable-archive-keyring.gpg /etc/apt/sources.list.d/caddy-stable.list
apt-get update && apt-get install -y caddy
caddy version
```
(If the repository step fails, try `apt-get install -y caddy` directly. Ubuntu may carry its own.)

Now the site configuration. **Do this only when `getent hosts tambola.sumitharit.com` (S1) showed the right address and ports 80/443 are open (S4):**
```bash
cat >/etc/caddy/Caddyfile <<'EOF'
{
	acme_ca https://acme-v02.api.letsencrypt.org/directory
}

tambola.sumitharit.com {
	encode zstd gzip
	reverse_proxy 127.0.0.1:5000
	header {
		Strict-Transport-Security "max-age=31536000"
		X-Content-Type-Options nosniff
		Referrer-Policy strict-origin-when-cross-origin
	}
}
EOF
caddy validate --config /etc/caddy/Caddyfile
systemctl enable caddy
systemctl restart caddy
sleep 20
journalctl -u caddy -n 40 --no-pager | grep -iE "obtain|certificate|error|tambola" || journalctl -u caddy -n 20 --no-pager
```
**Check:** `Valid configuration`, and the log mentions the certificate being obtained for `tambola.sumitharit.com`. Caddy requests the certificate from **Let's Encrypt** and stores it in `/var/lib/caddy`. WebSockets (live game updates) pass through with no extra settings.
If you see ACME or "challenge" errors: DNS not reached yet, or ports 80/443 blocked (`ufw status`; hPanel → VPS → Firewall must not be blocking). Let's Encrypt allows only 5 failed attempts per hour, so fix the cause before restarting again.

### S11. Check HTTPS and automatic renewal
```bash
curl -sI https://tambola.sumitharit.com | head -5
curl -sI http://tambola.sumitharit.com | head -3
echo | openssl s_client -connect tambola.sumitharit.com:443 -servername tambola.sumitharit.com 2>/dev/null | openssl x509 -noout -issuer -subject -dates
systemctl is-enabled caddy tambola
```
**Check:** HTTPS returns a normal page or a redirect to `/Account/Login`; HTTP redirects (301/308) to HTTPS; the issuer is **Let's Encrypt** (`O=Let's Encrypt`); validity about 90 days; both services print `enabled`.
**Renewal:** it is automatic. Caddy renews about 30 days before expiry for as long as the `caddy` service runs, and it starts at every boot, so there is no cron job to maintain. Optional monthly glance: re-run the `openssl` line and make sure `notAfter` is still more than 3 weeks away.

### S12. First use in a browser
1. Open **https://tambola.sumitharit.com**. You should see the sign-in page with a lock icon.
2. **Register** → domain `KEYWORDSINTL`, user ID `sumit.harit`, a strong password (8+ chars), your name, Employee Code, email. You become **Admin** (menu shows *Hosts & Employees* and *Reports*).
3. In *Hosts & Employees*, press **Make Host** on yourself. Create a room.
4. Open the site on a phone (different browser), register a second user (domain `Custom` or `KEYWORDSINTL`), join with the room code, take a ticket.
5. Start the game, call a few numbers, and confirm the phone shows them **live** (that proves WebSockets work through Caddy). Claim, end the game, then **Delete** it from *Past games*.
6. *Reports* → *Audit log* should show these actions.

### S13. Nightly backup
```bash
cat >/usr/local/bin/tambola-backup.sh <<'EOF'
#!/bin/bash
set -euo pipefail
DB=/var/lib/tambola/App_Data/tambola.db
OUT=/var/backups/tambola
STAMP=$(date +%Y%m%d-%H%M%S)
sqlite3 "$DB" ".backup '$OUT/tambola-$STAMP.db'"
gzip -f "$OUT/tambola-$STAMP.db"
find "$OUT" -name 'tambola-*.db.gz' -mtime +14 -delete
echo "backup ok: $OUT/tambola-$STAMP.db.gz"
EOF
chmod +x /usr/local/bin/tambola-backup.sh

cat >/etc/systemd/system/tambola-backup.service <<'EOF'
[Unit]
Description=Tambola database backup

[Service]
Type=oneshot
User=tambola
Group=tambola
ExecStart=/usr/local/bin/tambola-backup.sh
EOF

cat >/etc/systemd/system/tambola-backup.timer <<'EOF'
[Unit]
Description=Nightly Tambola database backup

[Timer]
OnCalendar=*-*-* 02:00:00
Persistent=true

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now tambola-backup.timer
systemctl start tambola-backup.service
journalctl -u tambola-backup -n 5 --no-pager
ls -la /var/backups/tambola
systemctl list-timers tambola-backup.timer --no-pager
```
**Check:** "backup ok" and a `.db.gz` file exist; the timer shows the next run at 02:00.
**Test a restore once** (reads a copy, changes nothing):
```bash
F=$(ls -t /var/backups/tambola/tambola-*.db.gz | head -1)
zcat "$F" > /tmp/restore-test.db && sqlite3 /tmp/restore-test.db "select count(*) from Employees;" && rm /tmp/restore-test.db
```
Also turn on Hostinger's own **backups/snapshots** in hPanel (VPS → Backups) as a second copy, since the local backups live on the same disk.

---

## 4. Updating the app later
On your PC repeat §1 and §2 (new file, new SHA256). On the server:
```bash
systemctl start tambola-backup.service                      # backup first
sha256sum /root/tambola.tar.gz                              # compare with your PC
PREV=$(readlink -f /opt/tambola/current)
REL=/opt/tambola/releases/$(date +%Y%m%d-%H%M%S)
mkdir -p "$REL" && tar -xzf /root/tambola.tar.gz -C "$REL"
chown -R root:root "$REL" && chmod -R a+rX "$REL"
ln -sfn "$REL" /opt/tambola/current
systemctl restart tambola
sleep 6; curl -s http://127.0.0.1:5000/health || { echo "UNHEALTHY: rolling back"; ln -sfn "$PREV" /opt/tambola/current; systemctl restart tambola; }
ls -1d /opt/tambola/releases/* | head -n -3 | xargs -r rm -rf   # keep the 3 newest releases
```
Database changes needed by new versions are applied automatically when the app starts, and data in `/var/lib/tambola` is never touched by an update. Running games in Auto mode resume after the restart; players' browsers reconnect by themselves. Update outside game time.

## 5. Rolling back
| Problem | Command |
|---|---|
| New release is bad | `ln -sfn /opt/tambola/releases/<older folder> /opt/tambola/current && systemctl restart tambola` (list folders with `ls /opt/tambola/releases`) |
| Data damaged | `systemctl stop tambola; zcat /var/backups/tambola/<file>.db.gz > /var/lib/tambola/App_Data/tambola.db; chown tambola:tambola /var/lib/tambola/App_Data/tambola.db; rm -f /var/lib/tambola/App_Data/tambola.db-wal /var/lib/tambola/App_Data/tambola.db-shm; systemctl start tambola` |
| Site down, give up for now | `systemctl stop tambola caddy` (the DNS record can stay) |
| Remove everything | `systemctl disable --now tambola caddy tambola-backup.timer; rm -rf /opt/tambola /var/lib/tambola /etc/systemd/system/tambola*; apt-get purge -y caddy` (**deletes all game data**) |
| Undo DNS | Hostinger hPanel → Domains → sumitharit.com → DNS → delete only the `tambola` A record |

## 6. Everyday commands
| Need | Command |
|---|---|
| Is it running? | `systemctl status tambola caddy --no-pager` |
| App log (live) | `journalctl -u tambola -f` (Ctrl+C to stop) |
| App log files | `ls /var/lib/tambola/logs` |
| Restart app | `systemctl restart tambola` |
| Busy server? | `top` (press `q` to quit), `free -m`, `ss -s` |
| Players connected now | `ss -tn state established '( sport = :443 )' | wc -l` |
| Certificate expiry | the `openssl s_client ...` line in S11 |
| Disk space | `df -h /` and `du -sh /var/lib/tambola /var/backups/tambola` |

## 7. Troubleshooting
| Symptom | Likely cause and fix |
|---|---|
| Browser says site can't be reached | `ufw status` must allow 80/443; `systemctl status caddy`; DNS: `getent hosts tambola.sumitharit.com` on the server must show `72.61.172.114` |
| Certificate error / not secure | Caddy log: `journalctl -u caddy -n 100 --no-pager`. Usually DNS not propagated or ports blocked. Fix, then `systemctl restart caddy` (no more than a few times an hour) |
| 502 Bad Gateway | The app is down: `systemctl status tambola`, `journalctl -u tambola -n 50 --no-pager` |
| App won't start: "AllowedDomains" | Missing `Tambola__AllowedDomains__0` line in the unit file |
| App won't start: ICU/globalization | Add `Environment=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` to the unit file (S9) |
| App won't start: permission denied | `chown -R tambola:tambola /var/lib/tambola`; check `ReadWritePaths` in the unit |
| Page loads but live updates don't arrive | WebSockets blocked by a network device; test from a phone on mobile data |
| Sign-in works but you're sent back to sign-in | `Tambola__BehindProxy=true` missing, or the site is opened with `http://` |
| Everyone sees wrong times | `timedatectl` must show Asia/Kolkata |
| "database is locked" | Don't copy the .db file while running; use the backup script |
| Server slow with a big game | `top`: if CPU stays near 100 %, the 1-core plan is too small; upgrade in hPanel (see the load-test reports) |
| Forgotten password | Admin → Hosts & Employees → **Reset password** |
| You are locked out as Admin | Make sure the unit line shows `KEYWORDSINTL\sumit.harit` (one backslash, S9), restart, then register with exactly that domain and user ID |

## 8. Final checklist
- [ ] `https://tambola.sumitharit.com` loads with a padlock; `http://` redirects to `https://`
- [ ] Certificate issuer is Let's Encrypt; `caddy` shows `enabled`
- [ ] You registered as Admin; a full test game with two devices worked, live numbers included
- [ ] `systemctl is-enabled tambola caddy tambola-backup.timer` all say `enabled`
- [ ] A backup exists and a test restore worked; Hostinger snapshot/backup turned on
- [ ] The release file/repo and any token used in §2 were deleted; `/root/tambola.tar.gz` removed (`rm /root/tambola.tar.gz`)
- [ ] Reboot test: run `reboot`, wait 2 minutes, confirm the site is back by itself
- [ ] (Optional) run the load test from a different PC against the live site before a 1000+ player game; the 1-core server is likely to need an upgrade for that

## 9. Safe-keeping
* Keep this file, the SHA256 of each release, and your Admin password in a safe place.
* Do not edit files inside `/opt/tambola/releases/*`; settings live in `/etc/systemd/system/tambola.service` (run `systemctl daemon-reload && systemctl restart tambola` after editing).
* Change a setting such as the allowed domains: edit the `Environment=Tambola__AllowedDomains__n` lines in that file, then reload and restart.

## 10. Optional: remove the SSH key that was added through the API
It lets whoever holds the private key on your PC (`C:\Users\sumit.harit\.ssh\tambola_vps`) log into the server as root. If you do not plan to use SSH from another network, remove it: in the browser terminal run
```bash
sed -i '/tambola-deploy@DEL1-PWDPC-5281/d' /root/.ssh/authorized_keys
cat /root/.ssh/authorized_keys
```
and delete the key files on your PC (`del $env:USERPROFILE\.ssh\tambola_vps*`). The server's own key (`root@srv1122934`) must stay.
