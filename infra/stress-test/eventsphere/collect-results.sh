#!/usr/bin/env bash
set -euo pipefail

namespace="${NAMESPACE:-eventsphere}"
test_run="${TEST_RUN:-eventsphere-flash-sale}"
output_dir="${OUTPUT_DIR:-artifacts/stress-evidence/${test_run}}"
mkdir -p "${output_dir}"

mapfile -t pods < <(kubectl -n "${namespace}" get pods -l "k6_cr=${test_run},runner=true" -o name)
[[ "${#pods[@]}" -gt 0 ]] || { echo "No k6 runner pods found." >&2; exit 1; }

for pod in "${pods[@]}"; do
  name="${pod#pod/}"
  kubectl -n "${namespace}" logs "${pod}" >"${output_dir}/${name}.log"
  grep '^K6_SUMMARY ' "${output_dir}/${name}.log" | sed 's/^K6_SUMMARY //' >"${output_dir}/${name}.json"
done

jq -s '{
  runners: length,
  iterations: ([.[].metrics.iterations.values.count] | add),
  accepted: ([.[].metrics.orders_accepted.values.count] | add),
  failedRate: (([.[] | .metrics.http_req_failed.values.fails] | add) / ([.[] | .metrics.http_req_failed.values | (.passes + .fails)] | add)),
  dropped: ([.[] | .metrics.dropped_iterations.values.count // 0] | add)
}' "${output_dir}"/*.json | tee "${output_dir}/aggregate.json"

jq -e '.runners == 10 and .iterations == 50000 and .accepted == 50000 and .dropped == 0 and .failedRate < 0.01' \
  "${output_dir}/aggregate.json" >/dev/null
