FROM golang:1.23.4-alpine AS builder

RUN apk add --no-cache git
WORKDIR /app
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN go build -o gateway-api ./cmd/api

FROM alpine:latest

RUN adduser -D -g '' appuser
WORKDIR /app
COPY --from=builder /app/gateway-api .
COPY .env .env
USER appuser
EXPOSE 3000

ENTRYPOINT ["./gateway-api"]
