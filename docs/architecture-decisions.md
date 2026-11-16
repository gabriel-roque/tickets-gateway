# Decisões arquiteturais

## ADR-001 — Migração incremental

Status: aceita. EventSphere é uma vertical nova. O overlay `infra/k8s/overlays/legacy` executa os serviços antigos e novos lado a lado, com roteamento Strangler por caminho; o backfill validado e idempotente permite migração por evento e canário, sem troca big-bang nem dual-write entre bancos.

## ADR-002 — Event Store e Kafka

Status: aceita. O Event Store SQL append-only é a fonte da verdade de Orders, Inventory e Tickets. Kafka transporta eventos de integração. Cada append usa `expectedVersion`, metadata e Outbox na mesma transação.

## ADR-003 — Entrega at-least-once

Status: aceita. Não prometemos exactly-once distribuído. Outbox impede perda entre banco e broker; Inbox, chaves únicas e handlers idempotentes tornam redelivery segura.

## ADR-004 — Saga orquestrada

Status: aceita. A compra possui ordem explícita e compensações. Estado, versão, lease, tentativas e erro são persistidos. Participantes usam `SagaId`/`OrderId` como chaves idempotentes.

## ADR-005 — Concorrência

Status: aceita. Estoque geral usa optimistic concurrency por stream. A chave lógica é `eventId + sectorId + bucketId`; buckets evitam um hot stream global. Assentos numerados usam transação curta, índice único, proprietário autenticado e TTL calculado no servidor.

## ADR-006 — Separação de deploy

Status: aceita. A mesma imagem executa papéis por `EventSphere__Role`: command, order-consumer, query, saga, projection, integration, outbox, migration ou replay. Isso permite escalabilidade independente.

## ADR-007 — Segurança

Status: aceita. Nenhuma credencial é versionada. Kubernetes referencia `eventsphere-secrets`; ambientes locais usam variáveis obrigatórias. Imagens são versionadas e o container executa sem root.

## ADR-008 — Ingresso assíncrono de comandos

Status: aceita. Em produção, a Command API persiste a intenção de compra em um tópico Kafka de 24 partições e responde `202` somente após confirmação do broker. `OrderId`, `ReservationId` e `SagaId` derivam deterministicamente da chave idempotente com escopo de cliente. O fingerprint da requisição rejeita reutilização da mesma chave com payload diferente. Consumers escaláveis gravam Event Store, idempotência e Saga transacionalmente. O modo direto permanece disponível para testes isolados.

## ADR-009 — Projeção global ordenada

Status: aceita. Um único projector possui o checkpoint global e persiste lotes de 100 eventos em uma transação, limitando o pico de memória do SQL no laboratório. O worker não escala horizontalmente; disponibilidade vem do restart e recuperação pelo checkpoint. Replay usa o papel exclusivo `replay` com o worker normal pausado.

Como o SQL Server aloca valores `IDENTITY` antes do commit, appends concorrentes poderiam tornar `N+1` visível antes de `N` e fazer o checkpoint ignorar o commit atrasado. O Event Store usa um `sp_getapplock` transacional e exclusivo somente na seção de append/commit, garantindo que `GlobalPosition` e ordem de visibilidade sejam equivalentes. O ingresso continua paralelo e durável no Kafka; a serialização fica restrita à fronteira da fonte de verdade.
