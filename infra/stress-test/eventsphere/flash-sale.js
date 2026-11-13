import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';
import { Trend, Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const JWT_TOKEN = __ENV.JWT_TOKEN || '';
const RUN_ID = __ENV.RUN_ID || 'local-volume';
const RUN_SEED = Number(__ENV.RUN_SEED || 261112);
const CAPACITY = Number(__ENV.CAPACITY || 5000);
const BUCKETS = Number(__ENV.BUCKETS || 100);
const TOTAL = Number(__ENV.ITERATIONS || 50000);
const UNIQUE = Number(__ENV.UNIQUE_ORDERS || 45000);
const DURATION = __ENV.DURATION || '10s';
const VUS = Number(__ENV.VUS || 10000);
const PROFILE = __ENV.PROFILE || 'parallel';
const STEADY = PROFILE === 'steady';
const COMMAND_DURATION = new Trend('command_duration', true);
const ACCEPTED = new Counter('orders_accepted');

export const options = {
  scenarios: {
    volume_50k: STEADY ? {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.RATE || 5000),
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: Number(__ENV.PRE_ALLOCATED_VUS || 2000),
      maxVUs: VUS,
    } : {
        executor: 'per-vu-iterations',
        vus: VUS,
        iterations: TOTAL / VUS,
        maxDuration: DURATION,
        gracefulStop: '0s',
      },
  },
  thresholds: {
    // Steady-state and simultaneous-burst SLOs are deliberately separate.
    command_duration: STEADY ? ['p(95)<300', 'p(99)<1000'] : ['p(95)<3000', 'p(99)<6000'],
    'http_req_failed{endpoint:command}': ['rate<0.01'],
    checks: ['rate>0.99'],
    orders_accepted: STEADY ? [`count>=${TOTAL - 10}`] : [`count==${TOTAL}`],
  },
};

function guidFromNumber(value) {
  const suffix = Math.abs(value).toString(16).padStart(12, '0').slice(-12);
  return `00000000-0000-4000-8000-${suffix}`;
}

function headers(extra = {}) {
  return { 'Content-Type': 'application/json', Authorization: `Bearer ${JWT_TOKEN}`, ...extra };
}

export function setup() {
  if (!JWT_TOKEN) throw new Error('JWT_TOKEN is required');
  if (!STEADY && TOTAL % VUS !== 0) throw new Error('ITERATIONS must be divisible by VUS');
  if (CAPACITY % BUCKETS !== 0) throw new Error('CAPACITY must be divisible by BUCKETS');
  const showId = guidFromNumber((RUN_SEED * 1000) + 999);
  const inventoryIds = [];
  for (let index = 0; index < BUCKETS; index += 1) {
    const inventoryId = guidFromNumber((RUN_SEED * 1000) + index);
    inventoryIds.push(inventoryId);
    const response = http.post(`${BASE_URL}/api/v2/inventories`, JSON.stringify({
      inventoryId,
      showId,
      sectorId: 'floor',
      bucketId: `${RUN_ID}-${index}`,
      capacity: CAPACITY / BUCKETS,
      unitPrice: 199.90,
      currency: 'BRL',
    }), { headers: headers(), tags: { endpoint: 'setup' } });
    if (!check(response, { 'inventory ready': (r) => r.status === 201 || r.status === 409 }))
      throw new Error(`inventory setup failed: ${response.status} ${response.body}`);
  }
  return { runId: RUN_ID, showId, inventoryIds };
}

export default function (data) {
  const iteration = exec.scenario.iterationInTest;
  const logicalOrder = iteration < UNIQUE ? iteration : iteration - UNIQUE;
  const inventoryId = data.inventoryIds[logicalOrder % data.inventoryIds.length];
  const response = http.post(`${BASE_URL}/api/v2/orders`, JSON.stringify({
    customerId: guidFromNumber(logicalOrder + 1),
    inventoryId,
    quantity: 1,
  }), {
    headers: headers({
      'Idempotency-Key': `${data.runId}-${logicalOrder}`,
      'X-Correlation-Id': guidFromNumber(logicalOrder + 1000000),
    }),
    tags: { endpoint: 'command' },
    timeout: '20s',
  });
  COMMAND_DURATION.add(response.timings.duration);
  if (response.status === 202) ACCEPTED.add(1);
  check(response, { 'command accepted': (r) => r.status === 202 });
}

export function teardown(data) {
  console.log(`EVIDENCE ${JSON.stringify({ runId: data.runId, showId: data.showId, capacity: CAPACITY, unique: UNIQUE, total: TOTAL })}`);
}

export function handleSummary(data) {
  const outputs = { stdout: 'K6 summary captured.\n' };
  if (__ENV.SUMMARY_PATH) outputs[__ENV.SUMMARY_PATH] = JSON.stringify(data, null, 2);
  return outputs;
}
