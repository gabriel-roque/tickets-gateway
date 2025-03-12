import http from 'k6/http';
import { check, sleep } from 'k6';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

export let options = {
  scenarios: {
    stress_test: {
      executor: 'ramping-vus',
      stages: [
        { duration: '10s', target: 100 },
        { duration: '10s', target: 500 },
        { duration: '10s', target: 1000 },
      ],
    },
    steady_load: {
      executor: 'constant-arrival-rate',
      rate: 100,
      timeUnit: '1s',
      duration: '10s',
      preAllocatedVUs: 100,
    },
    steady_load_1k: {
      executor: 'constant-arrival-rate',
      rate: 1000,
      timeUnit: '1s',
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
