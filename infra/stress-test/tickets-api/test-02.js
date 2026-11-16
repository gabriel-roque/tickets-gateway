import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  scenarios: {
    steady_load_1k_per_second: {
      executor: 'constant-arrival-rate',
      rate: 10000,
      timeUnit: '10s',
      duration: '10s',
      preAllocatedVUs: 10000,
    },
  },
};

export default function () {
  let url = `${__ENV.BASE_URL || 'http://localhost:5000'}/tickets`;
  let payload = JSON.stringify({
    event_id: __ENV.EVENT_ID,
    payment_method: 1,
  });

  let params = {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${__ENV.JWT_TOKEN}`,
    },
  };

  let res = http.post(url, payload, params);

  check(res, {
    'is status 201': (r) => r.status === 201,
    'is response time < 1s': (r) => r.timings.duration < 1000,
    'is response time < 2s': (r) => r.timings.duration < 2000,
    'is response time < 3s': (r) => r.timings.duration < 3000,
  });

  sleep(1);
}
