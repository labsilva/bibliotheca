#!/bin/bash
set -Eeuo pipefail
node /opt/bibliotheca/app/configure.mjs
export Authentication__Issuer="${PUBLIC_URL%/}/"
export Authentication__FrontendOrigin="${PUBLIC_URL%/}"
nginx -t
# Refuse a combined deployment where Chromium can read backend secrets.
for path in /app/appsettings.json /app/cert /app/data /run/secrets/oidc_signing_password /run/secrets/oidc_encryption_password; do
  if runuser -u pwuser -- test -r "$path"; then
    echo "Unsafe permissions: browser user can read $path. See docs/deployment.md." >&2
    exit 1
  fi
done
pids=()
stop() {
  trap - TERM INT EXIT
  for pid in "${pids[@]}"; do kill -TERM -- "-$pid" 2>/dev/null || true; done
  for attempt in {1..20}; do
    running=false
    for pid in "${pids[@]}"; do kill -0 -- "-$pid" 2>/dev/null && running=true; done
    if ! $running; then break; fi
    sleep 1
  done
  for pid in "${pids[@]}"; do kill -KILL -- "-$pid" 2>/dev/null || true; done
  for pid in "${pids[@]}"; do wait "$pid" 2>/dev/null || true; done
}
trap 'stop; exit 0' TERM INT
trap stop EXIT
setsid runuser -u pwuser -- env -i HOME=/home/pwuser PATH="$PATH" NODE_ENV=production PLAYWRIGHT_BROWSERS_PATH="${PLAYWRIGHT_BROWSERS_PATH:-/ms-playwright}" PLAYWRIGHT_HOST=127.0.0.1 xvfb-run -a --server-args='-screen 0 1280x800x24 -nolisten tcp' node /opt/bibliotheca-browser/server.mjs &
pids+=("$!")
ready=false
for attempt in {1..45}; do
  if curl --fail --silent --max-time 1 http://127.0.0.1:8080/health >/dev/null; then ready=true; break; fi
  kill -0 "${pids[0]}" 2>/dev/null || exit 1
  sleep 1
done
if ! $ready; then echo 'Playwright did not become ready within 45 seconds.' >&2; exit 1; fi
setsid runuser -u app -- /app/Bibliotheca.Backend &
pids+=("$!")
setsid nginx -g 'daemon off;' &
pids+=("$!")
# Any exited service ends the container; Docker's restart policy restarts the whole app.
wait -n "${pids[@]}" || true
exit 1
