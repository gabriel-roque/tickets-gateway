#!/usr/bin/env bash
set -euo pipefail

: "${JWT_SIGNING_KEY:?JWT_SIGNING_KEY is required}"
SUBJECT="${SUBJECT:-00000000-0000-4000-8000-000000000001}"
ROLE="${ROLE:-admin}"
NOW="$(date +%s)"
EXP="$((NOW + 3600))"

b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
HEADER="$(printf '%s' '{"alg":"HS256","typ":"JWT"}' | b64url)"
PAYLOAD="$(printf '{"sub":"%s","role":"%s","iss":"eventsphere","aud":"eventsphere-api","iat":%s,"exp":%s}' \
  "$SUBJECT" "$ROLE" "$NOW" "$EXP" | b64url)"
SIGNATURE="$(printf '%s' "$HEADER.$PAYLOAD" | openssl dgst -sha256 -mac HMAC \
  -macopt "key:$JWT_SIGNING_KEY" -binary | b64url)"
printf '%s.%s.%s\n' "$HEADER" "$PAYLOAD" "$SIGNATURE"
