# Relatório de validação

Data: 2026-11-12. Ambiente: Docker Compose local com limites explícitos, SQL Server 2022, Redpanda com 24 partições, três Command APIs no gate de entrada, quatro consumers no replay e quatro Saga workers no drain. O SQL ficou limitado a 1,5 CPU/3 GiB, com 2.304 MiB de limite interno; APIs usam 0,35 CPU/320 MiB e workers 0,25 CPU/320 MiB.

## Gates de entrada

| Perfil | Volume | Resultado | Latência |
|---|---:|---:|---:|
| Steady state | 5.000 req/s por 10 s | 50.000 `202`, zero erro | p95 107,07 ms |
| Burst paralelo | 10.000 VUs, cinco chamadas por VU | 50.000 `202`, zero erro | p95 2.412,10 ms |

O lote paralelo contém 45.000 compras únicas e 5.000 replays idempotentes. Os SLOs são separados por perfil: p95 menor que 300 ms no steady state e menor que 3 s no burst simultâneo.

## Gate de consistência após drain e replay

Run `final-audit-10k-parallel-v2`, show `00000000-0000-4000-8000-00000f9063e7`:

| Invariante | Resultado |
|---|---:|
| Compras únicas materializadas | 45.000 |
| Capacidade / confirmados / reservados / disponíveis | 5.000 / 5.000 / 0 / 0 |
| Sagas completas / compensadas | 5.000 / 40.000 |
| Sagas running/compensating/manual/failed | 0 |
| Pagamentos históricos / ativos | 5.000 / 5.000 |
| Tickets históricos / ativos | 5.000 / 5.000 |
| Outbox pendente / dead letters | 0 / 0 |
| Event Store / checkpoint / lag | 145.448 / 145.448 / 0 |
| Projeção após replay | idêntica à origem |

O replay completo foi executado como job singleton com batch de 100, 0,25 CPU e 320 MiB. Durante a validação, um desvio incremental de três unidades revelou a possibilidade de posições `IDENTITY` ficarem visíveis fora da ordem de commit. O Event Store passou a serializar apenas a seção de append/commit com `sp_getapplock`; o rebuild posterior convergiu exatamente com a origem.

## Gates de qualidade

- `dotnet test tests/EventSphere.UnitTests`: 29/29 e `dotnet test tests/TicketsApi.UnitTests`: 1/1. Cobertura inclui regressão SQL para commits concorrentes, migration real do schema anterior e replay idempotente, ownership/TTL, autorização concorrente de transferência, notificações sequenciais, refund prematuro, janelas de crash e configuração/reuso do handler via `IHttpClientFactory`.
- `go test ./...`: sucesso, incluindo idempotência concorrente, propagação de falha e upgrade do schema antigo com deduplicação/índice único no gateway legado.
- Auditoria NuGet transitiva: nenhuma dependência vulnerável.
- Compose, Kustomize, scripts shell e `git diff --check`: válidos.
- Kubernetes base foi validado por renderização de 1.189 linhas e o overlay Strangler legado por 1.630 linhas com Kustomize. O backfill legado passou pelo dry-run e a configuração Nginx do front door passou em `nginx -t`; não houve `apply` nem benchmark em cluster neste laboratório limitado.

As execuções intermediárias rejeitadas foram preservadas em `docs/evidence/attempts/` e não compõem o gate aprovado.
