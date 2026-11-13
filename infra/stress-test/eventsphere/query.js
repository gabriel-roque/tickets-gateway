import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.QUERY_URL || 'http://localhost:8080';
const ORDER_IDS = (__ENV.ORDER_IDS || '').split(',').filter(Boolean);

export const options = {
  scenarios: {
    reads: { executor: 'constant-arrival-rate', rate: 5000, timeUnit: '1s', duration: '30s', preAllocatedVUs: 2000, maxVUs: 5000 },
  },
  thresholds: { http_req_duration: ['p(99)<200'], http_req_failed: ['rate<0.01'], checks: ['rate>0.99'] },
};

export default function () {
  if (ORDER_IDS.length === 0) throw new Error('ORDER_IDS is required');
  const response = http.get(`${BASE_URL}/api/v2/orders/${ORDER_IDS[Math.floor(Math.random() * ORDER_IDS.length)]}`);
  check(response, { 'query found': (r) => r.status === 200 });
}

export function handleSummary(data) {
  return { stdout: `K6_SUMMARY ${JSON.stringify(data)}\n` };
}
