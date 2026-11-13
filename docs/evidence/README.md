# Evidências de stress

Artefatos aprovados:

- `final-steady-50k-v2-summary.json`: 50.000 chamadas em 10 s, 5.000 req/s, p95 107,07 ms, zero erro de comando. SHA-256 `0c40a23f28cadc1426036a11e6bd7e672f4fbea4853013f50ad89db4219bcb5b`.
- `final-10k-parallel-v2-summary.json`: 10.000 VUs simultâneos, cinco chamadas por VU, 50.000 aceites em 8,0 s, p95 2.412,10 ms, zero erro. SHA-256 `d4b3475079269b0b98e6fd5762d4809c927fba48c0562c19853117f80731830a`.
- `final-10k-parallel-v2-invariants.json`: gate end-to-end aprovado após drain e replay, com 5.000 vendas, 40.000 compensações, zero oversell, Outbox/DLQ/lag em zero e projeção idêntica à origem. SHA-256 `b9d6fa619e3eda42363d9605b37624691345f386de4b6c7fe6c1aff9e7fcd36e`.

`attempts/` preserva, por transparência, execuções rejeitadas durante a auditoria. Elas não são usadas como evidência de aprovação.
