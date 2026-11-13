# Runbooks

## Saga parada

1. Consulte `GET /api/v2/operations/{sagaId}`.
2. Verifique `step`, `attempts`, `lastError` e `leaseUntil`.
3. Confirme a saúde do participante indicado pelo step.
4. Um lease abandonado será retomado após expirar.
5. `ManualIntervention` exige correção da causa e `POST /internal/sagas/{sagaId}/retry` com a chave administrativa.

## Rebuild de projeções

1. Garanta capacidade no read store.
2. Pause o deployment `projection-worker` para manter um único dono do checkpoint.
3. Execute `kubectl create -f infra/k8s/replay-job.yaml`.
4. Aguarde o Job terminar e reinicie o projector.
5. Monitore checkpoint e última posição global.
6. Compare invariantes antes de liberar a Query API.

## Outbox acumulada

1. Verifique conectividade e autenticação do Kafka.
2. Inspecione `Attempts`, `LastError` e `LeaseUntil`.
3. Não remova mensagens não publicadas.
4. Após restaurar o broker, o dispatcher retoma leases expirados.
5. Mensagens excedendo dez tentativas recebem `DeadLetteredAt`; investigue antes de replay.

## Dead letter

Preserve payload e headers, corrija a causa e reenvie mantendo o `MessageId` original.

## Incidente de overselling

Bloqueie o inventory bucket, compare aggregate e projection e preserve os eventos. Divergência de projection pede rebuild; violação do stream pede eventos compensatórios, nunca edição do histórico.

## Chaos test

Execute `infra/stress-test/eventsphere/chaos.sh` durante a carga. Sagas retomam por lease, Outbox por mensagens pendentes e projections pelo checkpoint.

## Gate de stress

1. Execute `flash-sale.js` com 10.000 VUs, 50.000 chamadas e 45.000 chaves únicas.
2. Preserve o `runId` e `showId` impressos em `EVIDENCE`.
3. Execute `verify.sh`; aceite somente 5.000 Sagas completas, 40.000 compensadas, zero manual/failed, fonte igual à projeção, Outbox/DLQ/lag iguais a zero.
4. O total histórico de pagamentos/tickets pode superar 5.000 por compensações após autorização/emissão; os contadores ativos devem ser exatamente 5.000.
