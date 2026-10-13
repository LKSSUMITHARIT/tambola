#!/usr/bin/env bash
# Runs ON THE SERVER as the "deployer" user. Called by .github/workflows/deploy.yml, or by hand:
#   bash remote-deploy.sh /tmp/tambola-v1.2.0.tar.gz <sha256>
# Installs the package as a new release, switches the "current" link, restarts the app,
# checks its health and rolls back automatically if it does not come up.
set -euo pipefail

PKG="${1:?usage: remote-deploy.sh <package.tar.gz> <sha256>}"
EXPECTED_SHA="${2:?usage: remote-deploy.sh <package.tar.gz> <sha256>}"
BASE=/opt/tambola
KEEP=4                                  # releases kept for rollback
HEALTH_URL=http://127.0.0.1:5000/health

log() { echo "[deploy $(date +%H:%M:%S)] $*"; }

[ -f "$PKG" ] || { log "package not found: $PKG"; exit 1; }
ACTUAL_SHA=$(sha256sum "$PKG" | cut -d' ' -f1)
if [ "${ACTUAL_SHA,,}" != "${EXPECTED_SHA,,}" ]; then
  log "CHECKSUM MISMATCH (expected $EXPECTED_SHA, got $ACTUAL_SHA). Nothing changed."
  exit 1
fi
log "checksum ok"

REL="$BASE/releases/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$REL"
tar -xzf "$PKG" -C "$REL"
if [ ! -f "$REL/Tambola.Web.dll" ] || [ ! -d "$REL/wwwroot" ]; then
  log "package is incomplete. Nothing changed."
  rm -rf "$REL"
  exit 1
fi
chmod -R a+rX "$REL"

# safety copy of the database (the service runs as 'tambola'; a first deploy has no database yet, which is fine)
sudo -n /usr/bin/systemctl start tambola-backup.service || log "WARNING: backup did not run (ok on a first deploy)"

PREV=$(readlink -f "$BASE/current" 2>/dev/null || true)
ln -sfn "$REL" "$BASE/current"
log "switched to $REL (previous: ${PREV:-none})"
sudo -n /usr/bin/systemctl restart tambola

healthy=0
for _ in $(seq 1 30); do
  if curl -fsS --max-time 5 "$HEALTH_URL" 2>/dev/null | grep -q '"status":"ok"'; then healthy=1; break; fi
  sleep 2
done

if [ "$healthy" -ne 1 ]; then
  log "NEW RELEASE IS NOT HEALTHY. Rolling back."
  if [ -n "$PREV" ] && [ -d "$PREV" ]; then
    ln -sfn "$PREV" "$BASE/current"
    sudo -n /usr/bin/systemctl restart tambola
    sleep 5
    curl -fsS --max-time 5 "$HEALTH_URL" >/dev/null 2>&1 && log "rolled back to $PREV and healthy" || log "ROLLBACK ALSO UNHEALTHY: check 'journalctl -u tambola'"
  else
    log "no previous release to roll back to"
  fi
  exit 1
fi

log "healthy. Deployment complete."

# prune old releases (never the current or previous one)
CUR=$(readlink -f "$BASE/current")
ls -1dt "$BASE"/releases/*/ 2>/dev/null | tail -n +$((KEEP + 1)) | while read -r old; do
  old="${old%/}"
  [ "$old" = "$CUR" ] || [ "$old" = "$PREV" ] || { rm -rf "$old" && log "removed old release $old"; }
done
exit 0
