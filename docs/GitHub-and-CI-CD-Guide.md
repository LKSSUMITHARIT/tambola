# Publish the Code on GitHub and Set Up the CI/CD Pipeline

You run every command yourself. Nothing here has been executed or pushed for you, and I could not run the pipeline, so **expect to fix small things on its first run** (§9 lists the usual ones).

## 0. What you will end up with
```
 you ──push branch──► Pull Request ──► CI: build + 25 tests ──► merge to main
 you ──push tag v1.2.0──► "Release and deploy":
        build + test + package ─► GitHub Release (tar.gz + SHA256)
                                 └► waits for your approval ("production" environment)
                                      └► copies package to the VPS over SSH ─► remote-deploy.sh
                                           backup DB ─► new release folder ─► switch link ─► restart
                                           health check ─► auto-rollback if unhealthy
                                      └► checks https://tambola.sumitharit.com/health
```
Files already created in the project for this (review them before pushing):
| File | Purpose |
|---|---|
| `.gitignore` | Keeps build output, databases, logs, keys and IDE files out of GitHub |
| `.gitattributes` | Forces Linux line endings (LF) on `.sh` and `.yml` files, otherwise the script fails on the server |
| `.github/workflows/ci.yml` | **CI**: build and test every pull request and every push to `main` |
| `.github/workflows/deploy.yml` | **CD**: package, create a GitHub Release, deploy to the VPS after approval |
| `deploy/remote-deploy.sh` | Runs on the server: verifies the checksum, installs the release, restarts, health-checks, rolls back on failure |
| `.github/dependabot.yml` | Weekly pull requests for outdated NuGet packages and GitHub Actions |

---

# PART 1: Put the full code on GitHub

## 1.1 Prerequisites (on your PC)
1. A GitHub account with **two-factor authentication on** (Settings → Password and authentication).
2. Git for Windows: https://git-scm.com/download/win (accept the defaults; it includes *Git Credential Manager*).
3. Check in PowerShell:
   ```powershell
   git --version
   git config --global user.name  "Sumit Harit"
   git config --global user.email "your-github-email@example.com"
   git config --global init.defaultBranch main
   git config --global core.autocrlf true
   ```
> **Your office network blocks SSH (port 22), so use the HTTPS URL for GitHub, not `git@github.com:...`.** HTTPS works through normal web access.

## 1.2 Create the empty repository on github.com
1. github.com → **+** (top right) → **New repository**.
2. Owner: your account (or your company organisation). Name: `tambola`.
3. Visibility: **Private** (this is an internal company app).
4. **Leave all three boxes unticked**: no README, no .gitignore, no license (the project already has them).
5. **Create repository**. Keep the page open: it shows the URL `https://github.com/<OWNER>/tambola.git`.

## 1.3 Check what will be uploaded (before the first commit)
```powershell
cd C:\Users\sumit.harit\Desktop\Claude_Projects\Tambola
git init
git add --dry-run . | Select-String -Pattern "/bin/|/obj/|\.db|logs/|\.env|_ed25519|tambola_vps"
```
**Check:** the last command prints **nothing** (those paths are excluded by `.gitignore`). If it prints lines, stop and tell me.

Look for secrets (should print nothing sensitive; the Admin user ID in `appsettings.json` is fine):
```powershell
git grep --untracked -n -i -E "password\s*[=:]\s*['""][^'""]{4,}|BEGIN (RSA|OPENSSH|EC) PRIVATE KEY|ghp_[A-Za-z0-9]{20,}" -- . ":!docs" ":!ClaudePlans" ":!*.md"
```
Also confirm the web libraries are included (the app needs them):
```powershell
git add --dry-run src/Tambola.Web/wwwroot/lib | Select-Object -First 5
```
**Check:** it lists `jquery.min.js` and `signalr.min.js`.

## 1.4 First commit and push
```powershell
git add .
git status --short | Measure-Object -Line          # roughly 100-200 files, not thousands
git commit -m "Tambola v1.1: initial import"
git branch -M main
git remote add origin https://github.com/<OWNER>/tambola.git
git push -u origin main
```
* A browser window opens for **GitHub sign-in** (Git Credential Manager). Approve it. If it asks for a password in the terminal instead, use a **personal access token** (GitHub → Settings → Developer settings → Fine-grained tokens → this repo → *Contents: Read and write*), not your account password.
* If the push is rejected for a large file, run `git ls-files -z | xargs -0 du -h | sort -h | tail` (Git Bash) to find it and add it to `.gitignore`.

## 1.5 Verify on GitHub
1. Refresh the repo page: you should see `src`, `tests`, `docs`, `deploy`, `.github`, `README.md`, etc.
2. **Actions** tab → the **CI** workflow started by itself from your push. Open it: **Build and test** should turn green in 2–4 minutes (25 tests pass).
   If it is red, open the failed step and send me the log text.
3. **Settings → Actions → General → Workflow permissions**: choose **Read repository contents** (the workflows ask for more only where needed).

## 1.6 Repository protection (recommended)
**Settings → Branches → Add branch ruleset** (or *Add rule*) for `main`:
* Require a pull request before merging
* Require status checks to pass → select **Build and test**
* Block force pushes
(On private repositories this needs a paid GitHub plan; on the free plan, simply agree as a team to always use pull requests.)

Also turn on **Settings → Code security**: *Secret scanning*, *Push protection*, *Dependabot alerts* (free for public repos, plan-dependent for private).

---

# PART 2: One-time server preparation for the pipeline
The pipeline logs into the server as a limited user called **`deployer`**, not as root. This assumes the server was set up with `docs/Manual-Server-Deployment-Steps.md` (service `tambola`, folders under `/opt/tambola`, backup service).

## 2.1 On your PC: create a key pair just for the pipeline
```powershell
ssh-keygen -t ed25519 -C "github-actions-deploy" -f "$env:USERPROFILE\.ssh\gh_deploy" -N '""'
Get-Content "$env:USERPROFILE\.ssh\gh_deploy.pub"          # the PUBLIC key (one line): goes to the server
```
The private key `gh_deploy` goes into GitHub (§3.1), **never** to the server or into the repository.

## 2.2 On the server (Hostinger browser terminal, as root)
Replace `PASTE_PUBLIC_KEY_LINE` with the line from the previous step.
```bash
useradd --create-home --shell /bin/bash deployer
install -d -m 700 -o deployer -g deployer /home/deployer/.ssh
echo 'no-port-forwarding,no-agent-forwarding,no-X11-forwarding,no-pty PASTE_PUBLIC_KEY_LINE' > /home/deployer/.ssh/authorized_keys
chown deployer:deployer /home/deployer/.ssh/authorized_keys && chmod 600 /home/deployer/.ssh/authorized_keys

# deployer owns the release folders (the app itself runs as the separate 'tambola' user and only reads them)
chown -R deployer:deployer /opt/tambola
ls -ld /opt/tambola /opt/tambola/releases

# deployer may restart the app and run the backup, nothing else
ls -l /usr/bin/systemctl
cat > /etc/sudoers.d/tambola-deployer <<'EOF'
deployer ALL=(root) NOPASSWD: /usr/bin/systemctl restart tambola, /usr/bin/systemctl start tambola-backup.service
EOF
chmod 440 /etc/sudoers.d/tambola-deployer
visudo -c
```
**Check:** `visudo -c` says "parsed OK". If `ls -l /usr/bin/systemctl` shows the file is elsewhere (e.g. `/bin/systemctl`), use that path in both the sudoers file and `deploy/remote-deploy.sh`.

## 2.3 Get the server's host key (so the pipeline can verify it is talking to YOUR server)
Still on the server:
```bash
echo "72.61.172.114 $(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)"
```
Copy the printed single line (`72.61.172.114 ssh-ed25519 AAAA...`). It becomes the secret `VPS_KNOWN_HOSTS`.

## 2.4 Keep recovery access
Leave root/SSH password settings as they are for now. The Hostinger **browser terminal** stays your emergency way in. (Optional hardening later: set `PasswordAuthentication no` in `/etc/ssh/sshd_config.d/` only **after** the pipeline has logged in successfully with its key.)

---

# PART 3: Configure GitHub for deployments

## 3.1 Create the `production` environment and its secrets
1. Repo → **Settings → Environments → New environment** → name exactly `production`.
2. **Required reviewers**: add yourself (and a colleague). Every deployment will wait for someone to click *Approve*.
3. **Deployment branches and tags**: *Selected branches and tags* → add the tag rule `v*`, and the branch `main`.
4. In the same page, **Environment secrets → Add secret** (four secrets):
   | Secret name | Value |
   |---|---|
   | `VPS_HOST` | `72.61.172.114` |
   | `VPS_USER` | `deployer` |
   | `VPS_SSH_KEY` | the **entire** private key file: `Get-Content "$env:USERPROFILE\.ssh\gh_deploy" -Raw`, including the `-----BEGIN OPENSSH PRIVATE KEY-----` and `-----END ...-----` lines |
   | `VPS_KNOWN_HOSTS` | the single line from §2.3 |
5. After saving, **delete the private key from your PC** or store it only in a password manager:
   `Remove-Item "$env:USERPROFILE\.ssh\gh_deploy"` (keep the `.pub` if you like).

> Environment secrets are only readable by the deploy job after it has been approved, which is safer than repository secrets.

If the `production` environment cannot require reviewers on your GitHub plan, the deploy still works, but then **anyone who can push a `v*` tag deploys to production**. In that case restrict who can create tags (ruleset on `v*` tags).

---

# PART 4: Releasing a new version (the daily CD flow)

## 4.1 First test: build only (no server touched)
1. Repo → **Actions → Release and deploy → Run workflow** → Branch `main`, **untick** "Deploy to production" → Run.
2. The **Build, test, package** job should pass and upload an artifact `tambola-package`. This proves the packaging works.

## 4.2 Real release
```powershell
cd C:\Users\sumit.harit\Desktop\Claude_Projects\Tambola
git checkout main
git pull
git tag -a v1.1.0 -m "Tambola 1.1.0"
git push origin v1.1.0
```
1. **Actions → Release and deploy**: the run for `v1.1.0` starts.
2. Job 1 builds, tests, packages and creates a **GitHub Release** `v1.1.0` with `tambola-v1.1.0.tar.gz` and its `.sha256`.
3. Job 2 (**Deploy to production**) shows *Waiting for approval*. Check that **no game is live**, then **Review deployments → tick production → Approve and deploy**. (The restart disconnects players for a few seconds; Auto-calling games continue.)
4. Watch the log. The success path ends with `healthy. Deployment complete.` and `Live site healthy: https://tambola.sumitharit.com`.
5. Open the site and check quickly. The Admin → *Reports → Audit log* is untouched; the database is migrated automatically on start.

Version numbers: `v1.1.0` = major.minor.patch. Bug fix → `v1.1.1`; new feature → `v1.2.0`.

## 4.3 If a deployment fails
* The script **rolls back by itself** if the new release does not become healthy. The log says `NEW RELEASE IS NOT HEALTHY. Rolling back.` and the old version keeps serving.
* Checksum mismatch or incomplete package: nothing is changed on the server.
* To go back manually later: **Actions** → open an older successful run → **Re-run all jobs**; or on the server `ln -sfn /opt/tambola/releases/<older folder> /opt/tambola/current && systemctl restart tambola`.

---

# PART 5: Everyday development with GitHub
```powershell
git checkout main; git pull
git checkout -b feature/short-name
# ... edit, run `dotnet test` ...
git add -A
git commit -m "Describe the change"
git push -u origin feature/short-name
```
1. On GitHub press **Compare & pull request** → Create. **CI** runs automatically; wait for green.
2. Review the changes in the PR, **Merge** (squash is fine), delete the branch.
3. When ready to go live, create a tag (§4.2).

Never edit directly on the server; changes there would be lost at the next deploy.

---

# PART 6: What the pipeline does, and how to change it
| Need | Where |
|---|---|
| Add more checks (code analysis, format) | `ci.yml` → new step after Test |
| Change who/what can deploy | GitHub environment `production` (reviewers, tag rule) |
| Change allowed domains / admin ID | **Not in the pipeline**: server file `/etc/systemd/system/tambola.service`, then `systemctl daemon-reload && systemctl restart tambola` |
| Keep more old releases | `KEEP=4` in `deploy/remote-deploy.sh` |
| Add a test server | Copy the server prep to another host, create a `staging` environment with its own four secrets, and duplicate the `deploy` job with `environment: staging` |
| Deploy only after CI is green on main | Already guaranteed because the build job runs the tests again; keep branch protection on |

# PART 7: Security checklist
- [ ] Repository is **Private**; two-factor authentication is on for every member
- [ ] No passwords or keys in the repo (settings such as admin and domains live in the systemd file on the server)
- [ ] Deployment secrets stored as **Environment secrets**, with required reviewers on `production`
- [ ] The pipeline uses a **limited `deployer` user** (can only restart Tambola and trigger the backup)
- [ ] The private key was deleted from your PC after saving it in GitHub (§3.1)
- [ ] Rotate the pipeline key about once a year: create a new pair, replace `authorized_keys` and the secret
- [ ] Optional: pin each `uses:` action to a full commit SHA instead of `@v4`, and review Dependabot's PRs weekly
- [ ] Optional: after the first successful deploy, disable SSH password logins on the server (§2.4)

# PART 8: Cost and limits
GitHub Actions minutes are free for public repos; private repos on the free plan get a monthly allowance (a CI run uses about 3–4 minutes, a release about 6 minutes). This is far below the limit for normal use. Reviewers and branch rulesets on private repos depend on your GitHub plan.

# PART 9: Troubleshooting
| Symptom | Cause and fix |
|---|---|
| `git push` asks for a password and fails | Use the browser sign-in or a personal access token, never the account password; use the `https://` remote URL, not `git@` |
| `git push` hangs or times out on SSH | Your office network blocks SSH. `git remote set-url origin https://github.com/<OWNER>/tambola.git` |
| CI red: "No .NET SDK found for 10.0" | The `setup-dotnet` version string; try `dotnet-version: 10.0.x` with `dotnet-quality: ga` or check https://dotnet.microsoft.com for the current 10.x |
| CI red: test fails only on GitHub | Open the failing test log; usually a timing test (the auto-caller test waits 7 s). Re-run once; if it repeats, send me the log |
| Deploy: `Permission denied (publickey)` | The public key in `/home/deployer/.ssh/authorized_keys` doesn't match the secret `VPS_SSH_KEY`; the secret was pasted without the BEGIN/END lines; or the file permissions are wrong (`chmod 600`, directory `700`) |
| Deploy: `Host key verification failed` | `VPS_KNOWN_HOSTS` wrong. Redo §2.3 (it must start with the server IP) |
| Deploy: `Connection timed out` | Server firewall (`ufw status` must allow 22) or Hostinger firewall blocks GitHub's addresses; GitHub's runner IPs change, so port 22 must be open to the internet (protected by key-only deployer and fail2ban) |
| `sudo: a password is required` | The sudoers file path of `systemctl` is wrong (§2.2), or the line has a typo; run `visudo -c` |
| `/usr/bin/env: 'bash\r': No such file` | The script was committed with Windows line endings. Check `.gitattributes` is in the repo, then `git add --renormalize . && git commit` |
| `CHECKSUM MISMATCH` | The upload was corrupted or an old file was in `/tmp`. Re-run the workflow |
| Health check fails and rolls back | `journalctl -u tambola -n 80 --no-pager` on the server. Common: a new required setting missing in the systemd file |
| Tag pushed but no workflow ran | Tag must start with `v` (e.g. `v1.1.0`) and be pushed (`git push origin v1.1.0`) |
| Approval button never appears | The `production` environment has no reviewers, or you are not one of them |

# PART 10: Optional extras
* **Status badge** in `README.md`: `![CI](https://github.com/<OWNER>/tambola/actions/workflows/ci.yml/badge.svg)`
* **Notifications** (Teams/Slack/email) on failed deployments through a webhook step at the end of `deploy.yml`.
* **CodeQL scanning** (Security → Code scanning → Set up) for automatic vulnerability checks.
* **Self-hosted runner** on a machine inside your network, if you later prefer not to expose SSH to the internet. The deploy job would then connect to the server privately.
