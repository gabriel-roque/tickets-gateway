#!/usr/bin/env bash
set -euo pipefail

namespace="${NAMESPACE:-eventsphere}"
test_run="${TEST_RUN:-eventsphere-flash-sale}"
: "${INTERNAL_BASE_URL:?Set INTERNAL_BASE_URL (use a port-forward for local execution)}"
: "${ADMIN_API_KEY:?Set ADMIN_API_KEY}"

echo "Waiting for the distributed load window..."
deadline=$((SECONDS + 300))
until kubectl -n "${namespace}" get pods -l "k6_cr=${test_run},runner=true" -o json \
  | jq -e '[.items[] | select(.status.phase == "Running")] | length > 0' >/dev/null; do
  (( SECONDS < deadline )) || { echo "k6 runners did not start" >&2; exit 1; }
  sleep 2
done

curl --fail --silent -X POST \
  -H "X-Api-Key: ${ADMIN_API_KEY}" -H 'Content-Type: application/json' \
  -d '{"seconds":60}' "${INTERNAL_BASE_URL}/internal/faults/payment" >/dev/null

kubectl -n "${namespace}" delete pod -l role=saga --wait=false
kubectl -n "${namespace}" delete pod -l role=projection --wait=false
kubectl -n "${namespace}" delete pod -l role=outbox --wait=false

kubectl -n "${namespace}" rollout status deployment/saga-worker --timeout=3m
kubectl -n "${namespace}" rollout status deployment/projection-worker --timeout=3m
kubectl -n "${namespace}" rollout status deployment/outbox-worker --timeout=3m
kubectl -n "${namespace}" wait --for=condition=TestRunPassed "testrun/${test_run}" --timeout=15m

echo "Fault window injected and all durable workers recovered. Run verify.sh after drain."
