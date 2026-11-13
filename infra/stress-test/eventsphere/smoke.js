import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const API_KEY = __ENV.API_KEY || '';
const INVENTORY_ID = __ENV.INVENTORY_ID;
const RUN_ID = __ENV.RUN_ID || 'smoke';

export const options = {
  vus: 20,
  iterations: 200,
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500'],
  },
};

export default function () {
  const suffix = (__VU * 100000 + __ITER).toString(16).padStart(12, '0').slice(-12);
  const response = http.post(`${BASE_URL}/api/v2/orders`, JSON.stringify({
    customerId: `00000000-0000-4000-8000-${suffix}`,
    inventoryId: INVENTORY_ID,
    quantity: 1,
  }), {
    headers: {
      'Content-Type': 'application/json',
      'X-Api-Key': API_KEY,
      'Idempotency-Key': `${RUN_ID}-${__VU}-${__ITER}`,
    },
  });
  check(response, { accepted: (r) => r.status === 202 });
  sleep(0.01);
}
