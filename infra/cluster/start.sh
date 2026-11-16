#!/usr/bin/env bash
set -euo pipefail

: "${EVENTSPHERE_DB_PASSWORD:?Set EVENTSPHERE_DB_PASSWORD}"
: "${EVENTSPHERE_API_KEY:?Set EVENTSPHERE_API_KEY}"
: "${EVENTSPHERE_JWT_SIGNING_KEY:?Set EVENTSPHERE_JWT_SIGNING_KEY (at least 32 characters)}"
EVENTSPHERE_ADMIN_API_KEY="${EVENTSPHERE_ADMIN_API_KEY:-${EVENTSPHERE_API_KEY}}"
EVENTSPHERE_DEPLOY_LEGACY="${EVENTSPHERE_DEPLOY_LEGACY:-false}"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd "${script_dir}/../.." && pwd)"

docker build -t eventsphere-api:1.0.0 -f "${repo_dir}/src/EventSphere.Api/Dockerfile" "${repo_dir}"
if [[ "${EVENTSPHERE_DEPLOY_LEGACY}" == "true" ]]; then
  docker build -t tickets-api:legacy "${repo_dir}/tickets-api"
  docker build -t gateway-api:legacy "${repo_dir}/gateway-api"
fi

kind create cluster --name eventsphere --config "${script_dir}/tickets-gateway-cluster.yaml"
kind load docker-image eventsphere-api:1.0.0 --name eventsphere
if [[ "${EVENTSPHERE_DEPLOY_LEGACY}" == "true" ]]; then
  kind load docker-image tickets-api:legacy gateway-api:legacy --name eventsphere
fi
kubectl create namespace eventsphere --dry-run=client -o yaml | kubectl apply -f -
kubectl -n eventsphere create secret generic eventsphere-secrets \
  --from-literal=DbPassword="${EVENTSPHERE_DB_PASSWORD}" \
  --from-literal=ConnectionStrings__WriteDb="Server=sqlserver,1433;Database=eventsphere-write;User Id=sa;Password=${EVENTSPHERE_DB_PASSWORD};TrustServerCertificate=true" \
  --from-literal=ConnectionStrings__ReadDb="Server=sqlserver,1433;Database=eventsphere-read;User Id=sa;Password=${EVENTSPHERE_DB_PASSWORD};TrustServerCertificate=true" \
  --from-literal=Security__ApiKey="${EVENTSPHERE_API_KEY}" \
  --from-literal=Security__AdminApiKey="${EVENTSPHERE_ADMIN_API_KEY}" \
  --from-literal=Security__JwtSigningKey="${EVENTSPHERE_JWT_SIGNING_KEY}" \
  --from-literal=Legacy__TicketDb="Server=sqlserver,1433;Database=tickets;User Id=sa;Password=${EVENTSPHERE_DB_PASSWORD};TrustServerCertificate=true" \
  --dry-run=client -o yaml | kubectl apply -f -
if [[ "${EVENTSPHERE_DEPLOY_LEGACY}" == "true" ]]; then
  kubectl apply -k "${repo_dir}/infra/k8s/overlays/legacy"
else
  kubectl apply -k "${repo_dir}/infra/k8s/base"
fi
kubectl -n eventsphere rollout status statefulset/sqlserver --timeout=5m
kubectl -n eventsphere rollout status statefulset/kafka --timeout=5m
kubectl -n eventsphere wait --for=condition=complete job/eventsphere-migration --timeout=5m
kubectl -n eventsphere rollout status deployment/command-api --timeout=5m
kubectl -n eventsphere rollout status deployment/query-api --timeout=5m

if [[ "${EVENTSPHERE_DEPLOY_LEGACY}" == "true" ]]; then
  kubectl -n eventsphere rollout status statefulset/legacy-postgres --timeout=5m
  kubectl -n eventsphere rollout status deployment/legacy-gateway-api --timeout=5m
  kubectl -n eventsphere rollout status deployment/legacy-tickets-api --timeout=5m
  kubectl -n eventsphere rollout status deployment/eventsphere-router --timeout=5m
  echo "Use: kubectl -n eventsphere port-forward svc/eventsphere-router 8080:80"
else
  echo "Use: kubectl -n eventsphere port-forward svc/command-api 8080:80"
fi

