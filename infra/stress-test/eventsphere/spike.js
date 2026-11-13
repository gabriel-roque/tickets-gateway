import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const JWT_TOKEN = __ENV.JWT_TOKEN || '';
const INVENTORY_IDS = (__ENV.INVENTORY_IDS || '').split(',').filter(Boolean);
const RUN_ID = __ENV.RUN_ID || 'local-spike';

export const options = {
  scenarios: {
    spike_10k_parallel: {
      executor: 'per-vu-iterations',
      vus: Number(__ENV.VUS || 10000),
      iterations: 1,
      maxDuration: '2m',
    },
  },
  thresholds: {
    'http_req_failed{endpoint:spike}': ['rate<0.01'],
    http_req_duration: ['p(95)<300', 'p(99)<1000'],
    checks: ['rate>0.99'],
  },
};

function guid(value) {
  const suffix = Math.abs(value).toString(16).padStart(12, '0').slice(-12);
  return `00000000-0000-4000-8000-${suffix}`;
}

export default function () {
  if (INVENTORY_IDS.length === 0) throw new Error('INVENTORY_IDS is required');
  if (!JWT_TOKEN) throw new Error('JWT_TOKEN is required');
  const id = exec.vu.idInTest;
  const response = http.post(`${BASE_URL}/api/v2/orders`, JSON.stringify({
    customerId: guid(id),
    inventoryId: INVENTORY_IDS[id % INVENTORY_IDS.length],
    quantity: 1,
  }), {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${JWT_TOKEN}`,
      'Idempotency-Key': `${RUN_ID}-spike-${id}`,
    },
    tags: { endpoint: 'spike' },
    timeout: '20s',
  });
  check(response, { 'spike accepted': (r) => r.status === 202 });
}

export function handleSummary(data) {
  const outputs = { stdout: 'K6 summary captured.\n' };
  if (__ENV.SUMMARY_PATH) outputs[__ENV.SUMMARY_PATH] = JSON.stringify(data, null, 2);
  return outputs;
}
