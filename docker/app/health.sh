#!/bin/sh
set -eu
curl --fail --silent --max-time 3 http://127.0.0.1:8080/health >/dev/null
curl --fail --silent --max-time 3 http://127.0.0.1:8090/api/health >/dev/null
