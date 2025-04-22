#!/bin/bash

TOPICS=("ticket-order" "payment-ticket")

PARTITIONS=2
RETENTION_MS=43200000

# URL do endpoint
URL="http://localhost:8080/api/clusters/gateway-kafka/topics"

HEADERS=(
  -H "Accept: */*"
  -H "Content-Type: application/json"
  -H "Origin: http://localhost:8080"
)

for TOPIC in "${TOPICS[@]}"; do
  echo "Criando tópico: $TOPIC"

  JSON_DATA="{\"name\":\"$TOPIC\",\"partitions\":$PARTITIONS,\"configs\":{\"cleanup.policy\":\"delete\",\"retention.ms\":\"$RETENTION_MS\"}}"

  RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$URL" "${HEADERS[@]}" --data-raw "$JSON_DATA")

  if [[ "$RESPONSE" -eq 200 || "$RESPONSE" -eq 201 ]]; then
    echo "Tópico '$TOPIC' criado com sucesso!"
  else
    echo "Erro ao criar o tópico '$TOPIC'. Código HTTP: $RESPONSE"
  fi
done
