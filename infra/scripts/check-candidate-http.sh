#!/usr/bin/env bash
set -euo pipefail

base_url="${1:?Pass the candidate base URL as the first argument}"
base_url="${base_url%/}"

landing="$(curl --silent --show-error --max-time 15 --output /dev/null --write-out '%{http_code}|%{content_type}' "$base_url/")"
case "$landing" in
  200\|text/html*) ;;
  *) echo "Candidate landing page must return 200 text/html; received $landing." >&2; exit 1 ;;
esac

platform_status="$(curl --silent --show-error --max-time 15 --output /dev/null --write-out '%{http_code}' "$base_url/api/platform/session")"
if [[ "$platform_status" != 401 ]]; then
  echo "Unauthenticated platform session must return 401; received $platform_status." >&2
  exit 1
fi

echo 'Candidate landing page and protected API checks passed.'
