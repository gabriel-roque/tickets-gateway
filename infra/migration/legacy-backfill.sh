#!/usr/bin/env bash
set -euo pipefail

: "${LEGACY_EXPORT:?Set LEGACY_EXPORT to the JSON export path}"
: "${BASE_URL:?Set BASE_URL to the EventSphere command endpoint}"
: "${JWT_TOKEN:?Set JWT_TOKEN to an EventSphere admin token}"

DRY_RUN="${DRY_RUN:-false}"
MAPPING_PATH="${MAPPING_PATH:-artifacts/legacy-mapping.ndjson}"

command -v jq >/dev/null || { echo "jq is required" >&2; exit 1; }
command -v curl >/dev/null || { echo "curl is required" >&2; exit 1; }
jq -e '(.events | type == "array") and (.tickets | type == "array")' "${LEGACY_EXPORT}" >/dev/null

violations="$(jq -r '
  [.events[] as $event |
    ([.tickets[] | select((.status == 3 or .status == "Approved") and .eventId == $event.id)] | length) as $sold |
    select(($event.totalTickets | tonumber) < $sold) |
    "event=\($event.id) capacity=\($event.totalTickets) approved=\($sold)"] | .[]
' "${LEGACY_EXPORT}")"
if [[ -n "${violations}" ]]; then
  echo "Legacy export has approved tickets above declared capacity:" >&2
  echo "${violations}" >&2
  exit 1
fi

request() {
  local method="$1" path="$2" body="$3" idempotency_key="${4:-}"
  if [[ "${DRY_RUN}" == "true" ]]; then
    jq -cn --arg method "${method}" --arg path "${path}" --argjson body "${body}" \
      '{dryRun: true, method: $method, path: $path, body: $body}'
    return
  fi

  local response_file status
  response_file="$(mktemp)"
  trap 'rm -f "${response_file}"' RETURN
  local -a headers=(-H "Authorization: Bearer ${JWT_TOKEN}" -H "Content-Type: application/json")
  if [[ -n "${idempotency_key}" ]]; then headers+=(-H "Idempotency-Key: ${idempotency_key}"); fi
  status="$(curl --silent --show-error --output "${response_file}" --write-out '%{http_code}' \
    -X "${method}" "${headers[@]}" --data "${body}" "${BASE_URL}${path}")"
  if [[ "${status}" != "201" && "${status}" != "202" && "${status}" != "409" ]]; then
    echo "Migration request failed: ${method} ${path} status=${status}" >&2
    jq . "${response_file}" >&2 2>/dev/null || true
    exit 1
  fi
  jq -c --arg status "${status}" '. + {httpStatus: ($status | tonumber)}' "${response_file}"
}

if [[ "${DRY_RUN}" != "true" ]]; then
  mkdir -p "$(dirname "${MAPPING_PATH}")"
  : >"${MAPPING_PATH}"
fi

while IFS= read -r event; do
  event_id="$(jq -r '.id' <<<"${event}")"
  body="$(jq -cn --arg id "${event_id}" \
    --arg bucket "legacy-${event_id}" \
    --argjson capacity "$(jq '.totalTickets | tonumber' <<<"${event}")" \
    --argjson price "$(jq '.priceTicket | tonumber' <<<"${event}")" \
    '{inventoryId:$id,showId:$id,sectorId:"legacy",bucketId:$bucket,capacity:$capacity,unitPrice:$price,currency:"BRL"}')"
  request POST /api/v2/inventories "${body}" >/dev/null
done < <(jq -c '.events[]' "${LEGACY_EXPORT}")

while IFS= read -r ticket; do
  ticket_id="$(jq -r '.id' <<<"${ticket}")"
  event_id="$(jq -r '.eventId' <<<"${ticket}")"
  owner_id="$(jq -r '.ownerId' <<<"${ticket}")"
  body="$(jq -cn --arg customer "${owner_id}" --arg inventory "${event_id}" \
    '{customerId:$customer,inventoryId:$inventory,quantity:1}')"
  result="$(request POST /api/v2/orders "${body}" "legacy-ticket-${ticket_id}")"
  mapping="$(jq -cn --arg legacyTicketId "${ticket_id}" --arg legacyEventId "${event_id}" \
    --argjson result "${result}" '{legacyTicketId:$legacyTicketId,legacyEventId:$legacyEventId,result:$result}')"
  if [[ "${DRY_RUN}" == "true" ]]; then echo "${mapping}"; else echo "${mapping}" >>"${MAPPING_PATH}"; fi
done < <(jq -c '.tickets[] | select(.status == 3 or .status == "Approved")' "${LEGACY_EXPORT}")

if [[ "${DRY_RUN}" == "true" ]]; then
  echo "Legacy migration dry-run completed." >&2
else
  echo "Legacy migration submitted. Mapping: ${MAPPING_PATH}"
fi
