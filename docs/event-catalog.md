# Catálogo de eventos

| Evento | Stream | Produtor | Consumidores principais |
|---|---|---|---|
| `ShowInventoryRegistered` | `show-{id}` | Inventory | Cancellation Guard |
| `ShowCancelled` | `show-{id}` | Event Cancellation | Inventory/Saga Guard |
| `InventoryCreated` | `inventory-{id}` | Inventory | Projection |
| `InventoryReserved` | `inventory-{id}` | Purchase Saga | Projection, Analytics |
| `InventoryReleased` | `inventory-{id}` | Compensation | Projection |
| `InventoryConfirmed` | `inventory-{id}` | Purchase Saga | Projection, Settlement |
| `InventoryReturned` | `inventory-{id}` | Refund/Compensation | Projection |
| `InventoryReservationExpired` | `inventory-{id}` | Expiry Worker | Projection, Operations |
| `InventorySalesClosed` | `inventory-{id}` | Event Cancellation | Projection |
| `OrderCreated` | `order-{id}` | Command API | Saga, Projection |
| `OrderStepCompleted` | `order-{id}` | Saga | Projection, Operations |
| `OrderCompleted` | `order-{id}` | Saga | Projection, Notification |
| `OrderCancelled` | `order-{id}` | Compensation | Projection |
| `RefundRequested` | `order-{id}` | Refund Saga | Projection |
| `OrderRefunded` | `order-{id}` | Refund Saga | Projection, Settlement |
| `TicketIssued` | `ticket-{id}` | Ticketing | Projection, Check-in |
| `TicketTransferred` | `ticket-{id}` | Ticketing | Projection |
| `TicketCheckedIn` | `ticket-{id}` | Check-in | Projection |
| `TicketInvalidated` | `ticket-{id}` | Refund/Compensation | Projection, Check-in |

Todos os envelopes carregam schema version, timestamp, correlation, causation, actor e trace ID. Tipos desconhecidos interrompem o projector para exigir um upcaster explícito.

Comandos de compra usam o tópico `eventsphere.commands.orders`; eventos de integração usam `eventsphere.events`. Ambos têm 24 partições no ambiente de referência. Poison messages seguem para tópicos `.dlq` após retries limitados.
