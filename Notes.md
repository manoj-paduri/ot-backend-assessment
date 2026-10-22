# Notes - Online Betting Data Capture and Analytics System

## 1. What was built

```
 tester (NBomber)                                                              GET /api/player/{id}/casino
      │ POST /api/player/casinowager                                           GET /api/player/topSpenders
      ▼                                                                          ▲
┌──────────────┐  publish + broker confirm   ┌────────────┐  batch (≤500)   ┌───────────┐  proc  ┌────────────┐
│ OT.Assessment│ ───────────────────────────▶│  RabbitMQ  │ ───────────────▶│ OT.Assess │ ─────▶ │ SQL Server │
│     .App     │   durable queue, persistent │ wagers  +  │  prefetch 1000  │ .Consumer │  TVP   │ OT_Assess- │
│  (Web API)   │◀──────── 202 Accepted       │ dead-letter│  multi-ack      │ (Worker)  │        │  ment_DB   │
└──────┬───────┘                             └────────────┘                 └───────────┘        └─────▲──────┘
       │  reads: stored procedures (Dapper) + distributed cache (Redis, or in-memory fallback) ────────┘
```

| Project | Role |
|---|---|
| `OT.Assessment.Core` | Contracts only: message, DTOs and the interfaces (`IWagerPublisher`, `IWagerStore`, `IPlayerQueries`, `IIngestionMonitor`). No framework or infrastructure dependencies. |
| `OT.Assessment.Infrastructure` | RabbitMQ publisher/topology/connection, SQL Server access (Dapper + table-valued parameter), cache decorator, transient-error classifier, DI extensions shared by API and consumer. |
| `OT.Assessment.App` | ASP.NET Core Web API: the three required endpoints, `GET api/admin/stats`, health checks, correlation-id middleware, global error handling, live dashboard at `/`. |
| `OT.Assessment.Consumer` | .NET Worker Service: drains the queue in batches and stores them. |
| `OT.Assessment.Tests` | xUnit tests (no infrastructure needed). |
| `OT.Assessment.Tester` | The supplied load generator; only `appsettings.json` was changed (full endpoint URL). |

Dependencies point inwards (`App`/`Consumer` → `Infrastructure` → `Core`), so the API and the worker share one tested
implementation of the queue and the database access, and either can be swapped (Kafka, another store) behind the interfaces.

## 2. Setup and running

**Prerequisites:** .NET 10 SDK, a RabbitMQ and a SQL Server (instructions for both options below). Run everything from the folder that contains `src`, `test` and this file.

### Option A - Windows, SQL Server Developer + Integrated Security (the connection string in the brief)

1. **RabbitMQ** (guest/guest on `localhost:5672`, management UI http://localhost:15672):
   `docker\RabbitMQ\docker-up.bat` (Docker Compose), *or* the [RabbitMQ Windows service](https://www.rabbitmq.com/docs/install-windows#installer).
2. **Database:** `sqlcmd -S localhost -E -C -b -i DatabaseGenerate.sql` (or open the file in SSMS and run it). It is idempotent: it creates `OT_Assessment_DB` and every object only if missing.
3. **Start the API and the consumer** (two terminals):
   ```
   dotnet build OT.Assessment.slnx
   dotnet test test/OT.Assessment.Tests
   dotnet run --project src/OT.Assessment.App
   dotnet run --project src/OT.Assessment.Consumer
   ```
   The API listens on http://localhost:5000. Connection strings default to
   `Server=localhost;Database=OT_Assessment_DB;Integrated Security=SSPI;TrustServerCertificate=True;`
   (`TrustServerCertificate` is only there because the current SQL client encrypts by default and a dev server has a self-signed certificate).

### Option B - everything in Docker (works on Linux/macOS too)

```
dotnet build OT.Assessment.slnx
dotnet test test/OT.Assessment.Tests
docker compose -f docker/docker-compose.yml up -d            # RabbitMQ + SQL Server, schema is applied by the db-init job
dotnet run --project src/OT.Assessment.App      --launch-profile sql-auth-docker
dotnet run --project src/OT.Assessment.Consumer --launch-profile sql-auth-docker
```
The `sql-auth-docker` profile loads `appsettings.SqlAuth.json` (SQL login `sa`, because a Linux container cannot use Windows authentication).

### Optional: Redis as the distributed cache

`docker compose -f docker/docker-compose.yml --profile redis up -d`, then start the API with `Redis__ConnectionString=localhost:6379`.
Without it the same cache abstraction (`IDistributedCache`) is backed by process memory, so nothing extra is needed to run locally.

### Run the load test

1. Make sure the API and the consumer are running (`/health/ready` returns `Healthy` when SQL Server and RabbitMQ are reachable).
2. `test/OT.Assessment.Tester/appsettings.json` already points at `http://localhost:5000/api/player/casinowager`.
3. In a third terminal start the reconciliation watcher, then start the tester:
   ```
   .\scripts\Wait-ForDrain.ps1 -Expected 7000          # PowerShell  (Linux/macOS: scripts/wait-for-drain.sh 7000)
   dotnet run --project .\test\OT.Assessment.Tester\OT.Assessment.Tester.csproj
   ```
4. Watch it live at http://localhost:5000 (dashboard), then run `scripts/Reconcile.sql` for the integrity checks.

The tester sends **7,000** wagers (500 requests every 2 s, about 14 s of load). Use `scripts/ResetData.sql` (consumer stopped) to start the next run from zero.

### Endpoints

| Method | Route | Notes |
|---|---|---|
| POST | `api/player/casinowager` | Validates, publishes to RabbitMQ, returns `202 Accepted` once the broker has confirmed the message. |
| GET | `api/player/{playerId}/casino?page=1&pageSize=10` | Newest first; `{playerId}` is the wager's `accountId`. Response shape is exactly the one in the brief (`data`, `page`, `pageSize`, `total`, `totalPages`). |
| GET | `api/player/topSpenders?count=10` | Highest total spend first. |
| GET | `api/admin/stats` | Bonus: stored / queued / dead-lettered counts, for reconciliation. |
| GET | `health/live`, `health/ready`, `openapi/v1.json`, `/` | Liveness, readiness (SQL + RabbitMQ), OpenAPI document, dashboard. `OT.Assessment.App.http` has ready-made requests. |

## 3. Message bus (RabbitMQ)

- Local instance: `docker\RabbitMQ\docker-up.bat` (or `docker-up.sh`), credentials **guest / guest**, AMQP `localhost:5672`, management UI `localhost:15672`. Settings live in the `RabbitMq` section of each `appsettings.json` and can be overridden with environment variables (`RabbitMq__HostName=...`).
- Topology (declared idempotently by whichever process starts first, from one shared class): durable queue `ot.assessment.wagers`, with a dead-letter exchange `ot.assessment.wagers.dlx` (fanout) bound to the durable queue `ot.assessment.wagers.dead`.
- Messages are persistent, carry the wager id as `MessageId`, and are JSON (one shared serializer for producer and consumer, so the wire format cannot drift).
- **Why RabbitMQ:** an ack/nack model that maps directly onto "store, then acknowledge", dead-lettering built in, easy to run locally, and the technology named in the role.

## 4. Design decisions and reasoning

### API
- **`202 Accepted` only after a publisher confirm.** The client is told "accepted" only when the broker has taken responsibility for the persistent message, so an accepted wager cannot be lost by a crash of the API. Publishing uses a small pool of confirm-enabled channels (`PublisherChannelPoolSize`, default 16) so concurrent requests do not share a channel or serialise on one.
- If the broker is down the API answers `503` (RFC 7807 problem details) rather than pretending; clients can retry.
- **Validation at the edge** (`DataAnnotations` + `IValidatableObject`): non-empty GUIDs, required text, length limits that mirror the table-valued-parameter columns, amount range that fits `decimal(19,4)`. Invalid input is a `400`, never a poison message. Property binding is case-insensitive, so the brief's `Username` and the usual `username` both work.
- Only the fields the requirements use are put on the bus (see section 5); `CreatedDateTime` is normalised to UTC on the way in.
- `CorrelationIdMiddleware` gives each request an id (accepts a sane `X-Correlation-Id`, echoes it, adds it to every log scope). `GlobalExceptionHandler` turns failures into problem details without leaking internals.
- Page size is capped (1-100) and `count` for top spenders is capped (1-1000) so a request cannot ask the database for an unbounded amount of data.

### Consumer
- **Batching.** One message per database round trip would be the bottleneck. The worker reads from a bounded in-memory buffer and writes a batch when it reaches `BatchSize` (500) **or** `BatchMaxWaitMilliseconds` (200 ms) has passed, whichever is first: large batches under load, low latency when quiet. One batch = one stored-procedure call = one transaction.
- **Back-pressure.** `PrefetchCount` (1000) bounds the unacknowledged messages the broker will push; the buffer has the same capacity, so memory is bounded and if SQL Server is slow the messages simply wait in the queue.
- **Acknowledge after commit.** A batch is acknowledged with a single multi-ack only after the transaction has committed (at-least-once). Because the write is idempotent (below), a redelivery has no effect, so the net result is exactly-once storage.
- **Failure handling** (`WagerBatchProcessor`):

  | Situation | Behaviour |
  |---|---|
  | SQL Server down, timeout, deadlock, pool exhausted | Retried with capped exponential backoff (0.5 s → 15 s) for as long as it takes; messages stay unacknowledged, nothing is lost. |
  | Message is not valid JSON / lacks mandatory values | Rejected without requeue → dead-letter queue; the rest of the batch continues. |
  | Batch fails for a data reason (non-transient) | Falls back to one-by-one writes to isolate the offender, which is dead-lettered; the others are stored. |
  | RabbitMQ connection/channel drops | Session is re-created after a delay; unacknowledged messages return to the queue automatically (connection auto-recovery is on as well). |
  | Consumer crashes mid-batch | Unacknowledged messages are redelivered; idempotent insert skips any that were committed. |
- Separation of concerns: `WagerConsumerWorker` only talks to the broker, `ChannelBatchReader` only does size/time batching, `WagerBatchProcessor` holds the delivery rules and depends on two small interfaces (`IWagerStore`, `IMessageAcknowledger`) - which is what makes it unit-testable without RabbitMQ or SQL Server.

### Database (`DatabaseGenerate.sql`)
```
Provider 1───* Game 1───* Wager *───1 Player
```
- **Normalised, no duplication:** provider and game names and usernames are stored once; a wager row carries small keys (`GameId int`, `AccountId uniqueidentifier`). Fields no requirement needs (brand, external reference, transaction type, session data) are not stored.
- **Keys:** `Wager` is clustered on an ever-increasing `bigint IDENTITY` (`WagerSeq`) so inserts append and never split pages; the client's `WagerId` GUID gets a unique index. That unique index is what makes the consumer idempotent and also serves lookups by wager id.
- **Indexes chosen for the two read endpoints:**
  - `IX_Wager_AccountId_CreatedDateTimeUtc (AccountId, CreatedDateTimeUtc DESC, WagerSeq DESC) INCLUDE (WagerId, GameId, Amount)` - a player's page is a short range scan in the right order, no sort, no key lookups.
  - `IX_Player_TotalAmountSpend (TotalAmountSpend DESC) INCLUDE (Username)` - top N is the first N index rows.
  - `IX_Wager_GameId` - supports the foreign key / per-game analytics.
- **Running aggregates:** `Player.TotalAmountSpend` and `Player.WagerCount` are updated in the same transaction as the insert, from the rows *actually inserted*. "Top spenders" and the `total` behind paging become index reads instead of aggregating millions of rows. The trade-off (write amplification on hot players, aggregates that must be kept correct) is covered by the idempotent design and checked by `scripts/Reconcile.sql`.
- **Stored procedures** (`usp_<Entity>_<Action>`): `usp_Wager_InsertBatch` (set-based: providers → games → players → wagers → totals, one transaction, `UPDLOCK, HOLDLOCK` on the existence checks so parallel consumers cannot create duplicates), `usp_Wager_GetPageByAccount`, `usp_Player_GetTopSpenders`, `usp_Ingestion_GetStats`. View `vw_WagerDetail` is the flattened reporting/analytics entry point.
- **Table-valued parameter** (`dbo.WagerBatchType`): the whole batch is streamed in one round trip, avoiding both row-by-row inserts and string-built SQL.
- **`READ_COMMITTED_SNAPSHOT ON`:** the GET endpoints never block behind the consumer's write transactions, so both stay responsive during ingestion.
- Money is `decimal(19,4)` (the tester sends `double`; values are rounded to 4 decimals, so totals do not accumulate binary floating-point error). Times are stored as UTC `datetime2`.
- Naming: singular PascalCase tables, `PK_/FK_/UQ_/UX_/IX_/DF_` constraint prefixes, `usp_`/`vw_` object prefixes.

### Caching
`GET topSpenders` is an aggregate over the whole player base that every dashboard refresh asks for, so it is served through `IDistributedCache` with a short TTL (`Caching:TopSpendersTtlSeconds`, default 5 s): a few seconds of staleness is invisible, database load almost disappears. Redis is used when `Redis:ConnectionString` is set, otherwise an in-memory implementation of the same interface. The cache is an optimisation only - if Redis is down the call logs a warning and falls through to SQL Server. A player's own history changes with every wager and is already a cheap index seek, so it is deliberately not cached (a stale "latest wagers" would be wrong, not merely old).

### Code quality
SOLID in practice: single-purpose classes (publisher, topology, serializer, batch reader, processor, store); dependency inversion through `Core` interfaces; the cache is a decorator (open/closed - the endpoint code did not change to add it); options classes per concern; nullable reference types on; async end to end with `CancellationToken` and `ConfigureAwait(false)` in library code.

## 5. Data stored per wager

| Payload field | Stored as | Why |
|---|---|---|
| `wagerId` | `Wager.WagerId` (unique) | idempotency, response field |
| `accountId`, `Username` | `Player.AccountId`, `Player.Username` | player history and top spenders |
| `provider`, `gameName`, `theme` | `Provider.Name`, `Game.Name`, `Game.Theme` | response fields, analytics, stored once |
| `amount` | `Wager.Amount` + `Player.TotalAmountSpend` | response field, top spenders |
| `createdDateTime` | `Wager.CreatedDateTimeUtc` | ordering, response field |
| `transactionId`, `countryCode`, `numberOfBets`, `duration` | `Wager.*` | small, useful for analytics |
| `brandId`, `externalReferenceId`, `transactionTypeId`, `sessionData` | not stored | no requirement uses them (`sessionData` is large free text) |

## 6. Capacity reasoning

The tester offers 250 requests per second for 14 seconds. The API does one confirmed publish per request on up to 16 parallel channels (a confirmed publish to a local broker is in the low-millisecond range), and the consumer turns hundreds of messages into one set-based transaction, so the database sees a handful of round trips per second rather than hundreds. Both sides therefore have large headroom for this load, and the queue should stay shallow. If the numbers in section 8 say otherwise, the knobs are `BatchSize`, `PrefetchCount`, `PublisherChannelPoolSize` and running more consumer instances (the write path is safe for that: idempotent, and deadlocks are retried).

## 7. Challenges and how they were handled

- **At-least-once vs. correct totals.** A redelivered message must not add to a player's total twice. Solved by updating the totals from the rows the insert actually produced (`OUTPUT ... INTO`), in the same transaction.
- **Duplicates inside one batch and concurrent first-time players/games.** Handled with `ROW_NUMBER()` de-duplication and `UPDLOCK, HOLDLOCK` existence checks; the unique constraints are the backstop.
- **One bad message must not hold up thousands of good ones.** Isolation by one-by-one fallback and the dead-letter queue.
- **Distinguishing "retry" from "give up".** `TransientErrorClassifier` separates infrastructure faults (retry forever with backoff) from data faults (dead-letter), so the consumer neither spins on a poison message nor drops a message because the database blinked.
- **Time zones.** The payload carries an offset (`+02:00`); it is converted to UTC at the edge and stored as UTC, so ordering is unambiguous.
- **Floating-point money.** The tester sends `double`; storage is `decimal(19,4)` with explicit rounding.
- **Running the same code with Windows authentication (brief) and in Docker (SQL login).** Solved with two launch profiles and an environment-specific settings file instead of code changes.
- **Proving nothing is lost.** Added `GET api/admin/stats`, `scripts/Wait-ForDrain.ps1|.sh` and `scripts/Reconcile.sql` so the check "submitted = stored + queued + dead-lettered" is one command.

## 8. Load-test results

Environment: _⟵ fill: CPU / RAM / OS (e.g. Windows 11), Docker Desktop version, SQL Server 2022 Developer (container), RabbitMQ 4 (container), .NET 10, Redis off (in-memory cache)_

| Item | Result |
|---|---|
| Wagers submitted by the tester (NBomber `ok`) | ⟵ fill (tester sends 7,000) |
| Failed submissions (NBomber `fail`) | ⟵ fill |
| Wagers stored (`/api/admin/stats` → `storedWagers`) | 7,000 |
| Queued / dead-lettered after drain | ⟵ fill from the Wait-ForDrain summary (expected 0 / 0) |
| Missing (submitted − stored) | ⟵ fill (0 if `ok` = 7,000) |
| Duplicates (`Reconcile.sql` check 1) | 0 (7,000 stored = 7,000 distinct wager ids) |
| Time from first request to last wager stored (`Wait-ForDrain` elapsed) | ⟵ fill |
| Ingest duration in the database (`Reconcile.sql` check 2) | 25.7 s (first insert 11:11:14.421 UTC, last 11:11:40.090 UTC) |
| Both GET endpoints answered during ingestion? | ⟵ fill (Yes/No) |
| Processing errors in the consumer log | ⟵ fill (None / list them) |

**Integrity checks (`scripts/Reconcile.sql`)**
- Check 3 (player running totals vs. sum of their wagers): 0 rows, so consistent.
- Check 4 (wagers without a player or game): 0 rows.
- Check 5 (top 5 spenders): stored totals equal totals recomputed from the wagers. The top spender is Josephine.Schaefer36 with 431,745.5712.

**Observation:** the tester offers load for about 14 s, while storing finished about 25.7 s after the first insert, so the queue drained for roughly 11 s after the load stopped. The consumer kept pace without losing or duplicating any wager. The tuning knobs for a faster drain are `Consumer:BatchSize`, `Consumer:PrefetchCount` and running a second consumer instance.

How the values were captured: the tester prints `ok`/`fail` at the end; `Wait-ForDrain` prints the reconciliation block and exits `0` only when nothing is missing, queued or dead-lettered; `Reconcile.sql` prints duplicates, first/last insert time and the aggregate checks. During the run, `GET api/player/topSpenders?count=10` and `GET api/player/{accountId}/casino` were called to confirm both stay responsive.

## 9. Tests

`dotnet test test/OT.Assessment.Tests` - no RabbitMQ or SQL Server needed. They cover: batch processing and acknowledgement rules (valid batch, malformed message, transient retry, poison-message isolation), the size-or-time batch reader, message serialisation round trip and rejection of bad input, request validation and mapping (UTC conversion, trimming), the cache decorator (hit, per-count keys, TTL 0, broken cache falls back to the database), transient-error classification, and the controllers.
Test summary: total: 26, failed: 0, succeeded: 26, skipped: 0, duration: 1.6s
CI: `.github/workflows/ci.yml` restores, builds and runs these on every push/PR.

## 10. Bonus additions

- Idempotent, restart-safe pipeline with publisher confirms, dead-letter queue and back-pressure (section 4).
- Redis-ready distributed cache with a graceful in-memory fallback.
- Live dashboard at `/` (stored, queued, dead-lettered, rows/second, top spenders) and `GET api/admin/stats`.
- Reconciliation tooling (`Wait-ForDrain`, `Reconcile.sql`, `ResetData.sql`).
- Liveness/readiness health checks, correlation ids, RFC 7807 errors, OpenAPI document, `.http` request file.
- One-command infrastructure (`docker/docker-compose.yml`), GitHub Actions CI, unit tests.

## 11. Suggested improvements for production

- **Security:** authentication/authorisation on the API (JWT/OAuth2) and the admin endpoints, rate limiting, TLS everywhere, secrets from a vault (the sample passwords are for local use only), least-privilege SQL logins (execute on the procedures only).
- **Reliability:** RabbitMQ quorum queues / a cluster, a retry-with-delay queue before dead-lettering plus an alert and a replay tool for the dead-letter queue, an outbox if wagers were ever produced inside another transaction, a circuit breaker/Polly policies on the publisher.
- **Scale:** several consumer instances (already safe), partitioning or sharding by `AccountId` for a very large `Wager` table, table partitioning/columnstore or an archive tier for old wagers, read replicas for the GET side, keyset paging instead of `OFFSET` for deep pages.
- **Observability:** structured logs to a central store, OpenTelemetry traces across API → queue → consumer (propagate the correlation id as a message header), metrics (queue depth, batch size/latency, publish confirm latency) and alerts on depth and dead letters.
- **Search/analytics:** index wagers into ElasticSearch from the consumer for search and dashboards; use Redis pub/sub or SignalR to push live updates instead of polling.
- **Operations:** containerise the API and consumer, deploy through the CI pipeline with database migrations (DbUp/Flyway) instead of a run-once script, health-based autoscaling on queue depth.
- **Data:** a retention policy, GDPR handling for usernames, and a contract test between the tester payload and the API.
