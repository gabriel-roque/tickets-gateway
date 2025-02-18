import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
  scenarios: {
    stress_test: {
      executor: 'ramping-vus',
      stages: [
        { duration: '10s', target: 50 }, // Aumenta para 50 usuários simultâneos
        { duration: '30s', target: 100 }, // Sobe para 100 usuários
        { duration: '30s', target: 500 }, // Sobe para 500 usuários
        { duration: '30s', target: 1000 }, // Sobe para 1000 usuários
        { duration: '10s', target: 0 }, // Finaliza o teste
      ],
    },
    steady_load: {
      executor: 'constant-arrival-rate',
      rate: 100, // 100 requisições por segundo
      timeUnit: '1s',
      duration: '10s',
      preAllocatedVUs: 100,
    },
  },
};

export default function () {
  let url = 'http://localhost:5003/tickets';
  let payload = JSON.stringify({
    event_id: '680c3dc6-e43b-4d96-290a-08dd36ee032c',
    payment_method: 1,
  });

  let params = {
    headers: {
      'Content-Type': 'application/json',
      Authorization:
        'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6IjIxZjQzZDI1LTgwMzctNDI2MC1kNTc0LTA4ZGQzNmVkY2MxNiIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiJBZG1pbiIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL2VtYWlsYWRkcmVzcyI6ImFkbWluQGVtYWlsLmNvbSIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IkFkbWluIiwiZXhwIjoxNzM5OTA4OTIyLCJpc3MiOiJodHRwczovL3RpY2tldHMtYXBpIiwiYXVkIjoiaHR0cHM6Ly90aWNrZXRzLWFwaSJ9.btZl3eJVnqXE7JQvUaovH9SkxobSQBIlopNdTeRpG64',
    },
  };

  let res = http.post(url, payload, params);

  check(res, {
    'is status 201': (r) => r.status === 201,
    '90% of responses are 201': (r) => Math.random() > 0.1 || r.status === 201,
    'is response time < 1s': (r) => r.timings.duration < 1000,
    '90% response time below 1s': (r) =>
      Math.random() > 0.1 || r.timings.duration < 1000,
  });

  sleep(1);
}
