# Migração Strangler do legado

O overlay `infra/k8s/overlays/legacy` executa EventSphere e os dois serviços legados lado a lado. O router mantém `/events` e `/tickets` no `tickets-api`, expõe o gateway antigo em `/legacy/gateway/` e envia `/api/v2/*` e `/health/*` para EventSphere. A retirada do legado acontece por rota, sem dual-write entre bancos.

## Subir o canário local em Kind

```bash
export EVENTSPHERE_DB_PASSWORD='local-secret'
export EVENTSPHERE_API_KEY='local-api-key'
export EVENTSPHERE_ADMIN_API_KEY='local-admin-key'
export EVENTSPHERE_JWT_SIGNING_KEY='at-least-32-random-characters'
EVENTSPHERE_DEPLOY_LEGACY=true bash infra/cluster/start.sh
```

O `tickets-api` executa as migrations EF em um init container. O PostgreSQL legado recebe `gateway-api/sql/script-01.sql` por init script; no startup, o gateway também migra volumes antigos, elimina duplicatas de forma determinística e cria o índice único de `external_id` antes de aceitar tráfego. Todos os workloads possuem requests/limits e uma única réplica no laboratório.

## Exportar e validar

Produza um JSON sem credenciais no formato abaixo. `totalTickets` deve ser a capacidade total original, não apenas o saldo. Somente tickets `Approved`/status `3` são importados como vendas.

```json
{
  "events": [{"id":"GUID","totalTickets":100,"priceTicket":199.90}],
  "tickets": [{"id":"GUID","eventId":"GUID","ownerId":"GUID","status":3}]
}
```

O backfill falha antes de escrever se aprovados excederem a capacidade declarada. Primeiro execute o dry-run:

```bash
LEGACY_EXPORT=/secure/legacy-export.json \
BASE_URL=http://localhost:8080 \
JWT_TOKEN="$JWT_TOKEN" \
DRY_RUN=true \
bash infra/migration/legacy-backfill.sh
```

Depois execute de fato. Inventários preservam o ID do evento legado; cada venda usa `legacy-ticket-{ticketId}` como chave idempotente. Reexecuções são seguras e o arquivo NDJSON liga IDs legados às novas operações.

```bash
LEGACY_EXPORT=/secure/legacy-export.json \
BASE_URL=http://localhost:8080 \
JWT_TOKEN="$JWT_TOKEN" \
MAPPING_PATH=artifacts/legacy-mapping.ndjson \
bash infra/migration/legacy-backfill.sh
```

Após o drain, use `verify.sh` e compare capacidade, pagamentos e tickets ativos com o export. O rollback de tráfego é somente a troca das rotas do router; o Event Store não é apagado. O arquivo de export e o mapping contêm dados pessoais e não devem ser versionados.
