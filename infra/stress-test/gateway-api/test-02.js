import http from 'k6/http';
import { check, sleep } from 'k6';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

export let options = {
  scenarios: {
    steady_load_1k_per_second: {
      executor: 'constant-arrival-rate',
      rate: 10000,
      timeUnit: '10s',
      duration: '10s',
      preAllocatedVUs: 1000,
    },
  },
};

export default function () {
  let url = 'http://localhost:3000/transaction-pix';
  let payload = JSON.stringify({
    name: 'Ticket do Evento XPTO',
    value: 3244,
    external_id: uuidv4(),
  });

  let params = { headers: { 'x-api-key': 'master_api_key' } };

  let res = http.post(url, payload, params);

  check(res, {
    'is status 201': (r) => r.status === 201,
    'is response time < 1s': (r) => r.timings.duration < 1000,
    'is response time < 2s': (r) => r.timings.duration < 2000,
    'is response time < 3s': (r) => r.timings.duration < 3000,
  });

  sleep(1);
}
