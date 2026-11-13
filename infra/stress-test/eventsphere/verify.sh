#!/usr/bin/env bash
set -euo pipefail

: "${BASE_URL:?Set BASE_URL to the command API (cluster-internal or port-forward)}"
: "${ADMIN_API_KEY:?Set ADMIN_API_KEY}"
: "${RUN_ID:?Set RUN_ID}"
: "${SHOW_ID:?Set SHOW_ID}"

deadline=$((SECONDS + ${DRAIN_TIMEOUT_SECONDS:-900}))
while (( SECONDS < deadline )); do
  response="$(curl --fail --silent \
    -H "X-Api-Key: ${ADMIN_API_KEY}" \
    "${BASE_URL}/internal/audit/runs/${RUN_ID}?showId=${SHOW_ID}")"

  running="$(jq '[.sagaCounts[] | select(.status == "Running" or .status == "Compensating")] | map(.count) | add // 0' <<<"${response}")"
  if [[ "${running}" == "0" && "$(jq -r '.pendingOutbox' <<<"${response}")" == "0" && "$(jq -r '.projectionLag' <<<"${response}")" == "0" ]]; then
    break
  fi
  sleep 5
done

jq . <<<"${response}"
if [[ -n "${EVIDENCE_PATH:-}" ]]; then
  jq . <<<"${response}" >"${EVIDENCE_PATH}"
fi
jq -e '
  .submitted == 45000 and
  .source.capacity == 5000 and .source.confirmed == 5000 and .source.reserved == 0 and .source.available == 0 and
  .projection.capacity == .source.capacity and .projection.confirmed == .source.confirmed and
  .projection.reserved == .source.reserved and .projection.available == .source.available and
  ([.sagaCounts[] | select(.status == "Completed") | .count] | add // 0) == 5000 and
  ([.sagaCounts[] | select(.status == "Compensated") | .count] | add // 0) == 40000 and
  ([.sagaCounts[] | select(.status == "Running" or .status == "Compensating" or .status == "ManualIntervention" or .status == "Failed") | .count] | add // 0) == 0 and
  .payments >= 5000 and .activePayments == 5000 and .tickets >= 5000 and .activeTickets == 5000 and
  .pendingOutbox == 0 and .deadLetters == 0 and .projectionLag == 0
' <<<"${response}" >/dev/null

echo "Stress invariants verified for run ${RUN_ID}."
