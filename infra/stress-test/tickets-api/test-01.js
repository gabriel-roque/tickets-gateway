import http from 'k6/http';
import { check, sleep } from 'k6';

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
