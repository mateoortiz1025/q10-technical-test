# OrderFlow — Gestión de Pedidos y Reserva Asíncrona de Inventario

Sistema distribuido *event-driven* para la creación de pedidos y reserva asíncrona de inventario en tiempo real.

## Stack Tecnológico

* **Backend API (`orders-api/`):** .NET 8 (Minimal APIs), Entity Framework Core, PostgreSQL, RabbitMQ (`RabbitMQ.Client`), SignalR.
* **Worker (`inventory-worker/`):** .NET 8 Worker Service, Entity Framework Core, PostgreSQL, RabbitMQ.
* **Frontend (`frontend/`):** React 18, Vite, `@microsoft/signalr`, Nginx (reverse proxy para `/api/` y `/hubs/`).
* **Infraestructura:** Docker Compose, PostgreSQL 16 (bases aisladas `orders_db` e `inventory_db`), RabbitMQ 3.13 Management.
* **Pruebas (`tests/`):** xUnit + SQLite en memoria.
* **Bonus (`k8s/`):** Manifiestos de Kubernetes (`ConfigMap`, `Deployment`, `Service`).

---

## Arranque Rápido

### 1. Levantar todo con Docker Compose
No requiere pasos previos (todas las variables tienen valores por defecto en `docker-compose.yml`; opcionalmente puedes copiar `.env.example` a `.env`):

```bash
docker compose up --build
```

Servicios disponibles:
* **Frontend (React):** http://localhost:3000
* **Orders API / Swagger (Dev):** http://localhost:5001/swagger
* **RabbitMQ Management:** http://localhost:15672 (`orderflow` / `orderflow_dev_pwd`)

### 2. SKUs iniciales para probar (Seed idempotente)
Al iniciar, `Inventory.Worker` carga automáticamente 4 productos en `inventory_db`:
* `LAPTOP-PRO-15` (stock inicial: `15`)
* `MOUSE-ERG-01` (stock inicial: `50`)
* `TECLADO-MEC-02` (stock inicial: `25`)
* `MONITOR-4K-27` (stock inicial: `3` — útil para probar rechazos por stock insuficiente)

### 3. Ejecutar las pruebas automatizadas
Con el SDK de .NET 8 instalado:

```bash
dotnet test tests/OrderFlow.Tests.csproj
```

---

## Arquitectura y Flujo de Eventos

```text
React (3000) ──POST /api/orders──▶ Orders.Api (5001) ──(OrderCreated)──▶ RabbitMQ (orderflow.events)
     ▲                                    │                                       │
     │                              [orders_db]                                   ▼
     │                                                                    Inventory.Worker
     └──── SignalR (/hubs/orders) ◀── Orders.Api ◀──(StockReserved / ─────┘
                                                     StockRejected)       [inventory_db]
```

1. **Creación (`Orders.Api`):** `POST /orders` valida (`clienteNombre` no vacío, `sku` no vacío, `cantidad` entre 1 y 100), abre una transacción en `orders_db`, guarda el pedido en estado `Pending` y publica `OrderCreatedEvent` en el exchange `orderflow.events` (routing key `order.created`).
2. **Reserva (`Inventory.Worker`):** consume la cola `inventory.orders-created`. Dentro de una transacción en `inventory_db`, verifica idempotencia en `processed_events` y ejecuta un descuento atómico (`WHERE Sku = @sku AND Disponible >= @qty`). Publica `StockReservedEvent` (`stock.reserved`) o `StockRejectedEvent` (`stock.rejected`).
3. **Confirmación y Tiempo Real (`Orders.Api`):** `OrderEventsConsumer` escucha la cola `orders.stock-responses`, actualiza el pedido a `Confirmed` o `Rejected` (solo si sigue en `Pending`) y notifica al frontend vía SignalR (`OrderStatusChanged`).

---

## Decisiones Técnicas y Manejo de Fallos

* **Database-per-Service:** `Orders.Api` (`orders_db`) e `Inventory.Worker` (`inventory_db`) no comparten tablas ni se llaman por HTTP; el desacoplamiento es total vía eventos.
* **Resiliencia si RabbitMQ está caído al crear un pedido:** `POST /orders` envuelve el `SaveChangesAsync` en una transacción; si falla la publicación al broker, hace `Rollback` (evita pedidos huérfanos en `Pending`) y responde `503 Service Unavailable` con mensaje claro en la UI.
* **Idempotencia y concurrencia:** `Inventory.Worker` combina la tabla `processed_events` (`EventId` PK) con `ExecuteUpdateAsync` condicional en SQL, evitando doble descuento ante mensajes duplicados o *race conditions*.
* **Dead Letter Queues (DLQ) y Manual Ack:** ambos consumidores usan `autoAck: false`. Mensajes con JSON corrupto (`JsonException`) se envían a `*.dlq` (`requeue: false`) para no bloquear la cola; errores transitorios se reencolan (`requeue: true`).
* **SignalR con resincronización:** si el WebSocket se desconecta y reconecta, el frontend vuelve a consultar `GET /api/orders` para no perder transiciones ocurridas durante el corte.

---

## Trade-offs y Qué Haría Distinto con Más Tiempo

* **Transactional Outbox Pattern:** en lugar de publicar a RabbitMQ dentro de la transacción HTTP de `POST /orders` (y devolver `503` si el broker está caído), implementaría una tabla *Outbox* en `orders_db` con un *relay* en segundo plano para garantizar consistencia eventual incluso si RabbitMQ cae temporalmente.
* **Escalado horizontal de `Orders.Api` con SignalR:** si se despliegan múltiples réplicas de `Orders.Api` (como en `k8s/orders-api-deployment.yaml` con `replicas: 2`), agregaría un **Redis Backplane** para SignalR de modo que cualquier réplica pueda notificar a clientes conectados a otra instancia.
* **Consulta y gestión de inventario:** agregar endpoints (`GET /inventory`, `POST /inventory`, `PATCH /inventory/{sku}/stock`) o una proyección CQRS en `Orders.Api` para listar el stock disponible en tiempo real en el frontend, además de permitir crear nuevos productos y reponer stock sin depender del *seed* inicial.
* **Segmentación de red en Docker Compose:** separar los servicios en redes aisladas (`frontend-net` para `frontend` + `orders-api`, y `backend-net` con `internal: true` para `orders-api` + `inventory-worker` + `postgres` + `rabbitmq`), evitando que la capa web tenga visibilidad de red hacia la base de datos o el broker.
* **Migraciones formales:** reemplazar `EnsureCreatedAsync()` por migraciones versionadas de EF Core (`dotnet ef migrations`) y añadir tests E2E con Testcontainers (PostgreSQL + RabbitMQ reales).

---

## Bonus: Kubernetes (`k8s/`)

La carpeta `k8s/` incluye los manifiestos base para desplegar `orders-api`:
* `orders-api-configmap.yaml`: configuración no sensible (`ASPNETCORE_ENVIRONMENT`, `RabbitMq__*`).
* `orders-api-deployment.yaml`: 2 réplicas, `resources` (`requests`/`limits`), `readinessProbe` y `livenessProbe` sobre `/health`, y referencias a `Secret` para credenciales.
* `orders-api-service.yaml`: `Service` `ClusterIP` exponiendo el puerto `80` hacia `8080`.

**Cómo desplegaría el sistema completo en Kubernetes:**
* **`frontend`:** `Deployment` + `Service` (`ClusterIP`) expuestos hacia el exterior mediante un **`Ingress`** (NGINX con TLS) que enrute `/` al frontend y `/api` + `/hubs` hacia `orders-api` (con soporte de WebSockets para SignalR).
* **`inventory-worker`:** `Deployment` sin `Service` (solo tráfico saliente hacia Postgres y RabbitMQ), con su propio `ConfigMap` y `Secret`.
* **Datos y mensajería:** servicios administrados (RDS/Cloud SQL y CloudAMQP/Amazon MQ) o `StatefulSet` con `PersistentVolumeClaim`, protegidos con `NetworkPolicy` para que solo `orders-api` e `inventory-worker` tengan acceso.
* **Tiempo real multi-réplica:** agregar un **Redis Backplane** para SignalR al escalar `orders-api` a múltiples pods.
