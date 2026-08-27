import http from 'k6/http';
import { check } from 'k6';

const baseUrl = __ENV.BASE_URL || 'http://127.0.0.1:8080';
const apiKey = __ENV.BACKLINKSTUDIO_API_KEY;
const projectId = __ENV.BACKLINKSTUDIO_PROJECT_ID;
const rate = Number(__ENV.LOAD_RATE || 10);
const duration = __ENV.LOAD_DURATION || '20s';

if (!apiKey || !projectId) {
  throw new Error('BACKLINKSTUDIO_API_KEY and BACKLINKSTUDIO_PROJECT_ID are required.');
}

export const options = {
  discardResponseBodies: true,
  scenarios: {
    bounded_reads: {
      executor: 'constant-arrival-rate',
      rate,
      timeUnit: '1s',
      duration,
      preAllocatedVUs: Math.max(40, rate * 4),
      maxVUs: Math.max(40, rate * 4),
    },
  },
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<750', 'p(99)<1500'],
    dropped_iterations: ['count==0'],
  },
};

const headers = { Authorization: `Bearer ${apiKey}` };

export default function () {
  const health = http.get(`${baseUrl}/health/ready`);
  check(health, { 'readiness is healthy': (response) => response.status === 200 });

  const projects = http.get(`${baseUrl}/api/v1/projects?limit=20`, { headers });
  check(projects, { 'projects list succeeds': (response) => response.status === 200 });

  const jobs = http.get(`${baseUrl}/api/v1/jobs?projectId=${projectId}&limit=20`, { headers });
  check(jobs, { 'jobs list succeeds': (response) => response.status === 200 });
}
