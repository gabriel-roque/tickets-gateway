import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  scenarios: {
    stress_test: {
      executor: 'ramping-vus',
      stages: [
        { duration: '10s', target: 50 },
        { duration: '30s', target: 100 },
        { duration: '10s', target: 0 },
        { duration: '60s', target: 200 },
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
  let url = 'http://localhost:5000/tickets';
  let payload = JSON.stringify({
    event_id: 'e24bc3ee-c06e-49fb-f543-08dd5db6c96a',
    payment_method: 1,
  });

  let params = {
    headers: {
      'Content-Type': 'application/json',
      Authorization:
        'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6ImY2MjgzOTliLTVmNWUtNDRhMC04YzBjLTA4ZGQ1ZGI2YmU0NSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiJBZG1pbiIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6ImFkbWluQGVtYWlsLmNvbSIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IkFkbWluIiwiZXhwIjoxNzQxOTgwOTU0LCJpc3MiOiJodHRwczovL3RpY2tldHMtYXBpIiwiYXVkIjoiaHR0cHM6Ly90aWNrZXRzLWFwaSJ9.6m2RvNG4lH5BKolWDCOgMPv0luO6w5oHjPQQMTgN7Xk',
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
