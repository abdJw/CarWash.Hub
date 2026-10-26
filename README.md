# CarWash Hub API

Machine data ingestion for self-service car wash machines installed at gas stations.

**Phase 1 (this repository):** every machine pushes its status to one endpoint as JSON:
`{ "machineId": "...", "data": <any JSON> }`. What the machines put in `data` isn't fixed yet, so it
is stored as text exactly as received. Read-back endpoints serve each machine's latest status and
history.

**Phase 2 (planned):** a Razor Pages dashboard for the owner and a portal for each gas station.
See [docs/phase-2-plan.md](docs/phase-2-plan.md).

Machine engineers should read [docs/machine-integration-guide.md](docs/machine-integration-guide.md);
this README is for developers working on the API.

---

## Quick start

### Prerequisites

- .NET SDK 10.0.401 or later, with a complete .NET 10 runtime: `dotnet --list-runtimes` must show
  `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` at the same 10.0.x patch level.
- SQL Server 2019 or later. The database collation `Arabic_100_CI_AS_SC_UTF8` needs 2019+.

### Run

```bash
dotnet run --project src/CarWash.Hub.Api --launch-profile http
```

Development uses `Server=localhost;Database=carwash_hub_dev;Trusted_Connection=True`. Pending
migrations are applied automatically. The development keys come from
`appsettings.Development.json`:

| Header | Development value |
|---|---|
| `client-secret-key` | `dev-only-ingest-key-0123456789abcdef` |
| `client-read-key` | `dev-only-read-key-0123456789abcdefgh` |

Any key starting with `dev-only-` is refused outside the Development environment.

Once it is running:
- Swagger UI is at <http://localhost:5180/swagger>. Click **Authorize**, paste both keys, and try
  the examples.
- Scalar is at <http://localhost:5180/scalar>.
- `src/CarWash.Hub.Api/CarWash.Hub.http` has ready-made requests for the VS/Rider HTTP client.

```bash
curl -X POST http://localhost:5180/api/v1/machine-messages \
  -H "client-secret-key: dev-only-ingest-key-0123456789abcdef" \
  -H "Content-Type: application/json" \
  -d '{"machineId":"CW-RUH-0001","data":{"status":"Idle","coins":12}}'

curl http://localhost:5180/api/v1/machines/CW-RUH-0001/messages/latest \
  -H "client-read-key: dev-only-read-key-0123456789abcdefgh"
```

### Docker Compose

```bash
cp .env.example .env      # set MSSQL_SA_PASSWORD, CLIENT_SECRET_KEY, CLIENT_READ_KEY
docker compose up --build # API on http://localhost:8080, Swagger at /swagger (Staging)
```

---

## Endpoints (v1)

| Method and path | Header required | Purpose |
|---|---|---|
| `POST /api/v1/machine-messages` | `client-secret-key` | Store a message: JSON `{ machineId, data }`. 201 + `Location` |
| `GET /api/v1/machine-messages/{id}` | `client-read-key` | One stored message: `payload` (the stored data) and `payloadJson` when it is JSON |
| `GET /api/v1/machines` | `client-read-key` | Known machines, with first- and last-seen times (search, sort, page) |
| `GET /api/v1/machines/{machineId}` | `client-read-key` | One machine |
| `GET /api/v1/machines/{machineId}/messages/latest` | `client-read-key` | The machine's current status |
| `GET /api/v1/machines/{machineId}/messages?from&to&pageNumber&pageSize` | `client-read-key` | History, newest first |
| `GET /health`, `GET /health/ready` | none | Liveness, and readiness (which also checks the database) |

**Response shapes:**
- Success: an `ApiResponse<T>` envelope, `{ success, message, data, errors, correlationId }`.
- Failure: an RFC 7807 problem document with a stable `code` (for example
  `Ingestion_MachineIdMissing`) and the `correlationId`.
- Messages are in English, or Arabic with `X-Lang: ar`.

---

## Configuration

Every secret comes from user-secrets in development, or from environment variables elsewhere.
Nothing secret is committed.

| Section | Setting | Default | Notes |
|---|---|---|---|
| `ConnectionStrings` | `Default` | (dev only) | `ConnectionStrings__Default` |
| `ClientKeys` | `IngestKeys[0..1]` `{ Id, Key }` | — | The key every machine sends. 1–2 entries (2 during a rotation). Required. |
| `ClientKeys` | `ReadKeys[0..1]` `{ Id, Key }` | — | The key for the read endpoints. Must differ from every ingest key. Required. |
| `RateLimiting` | `Enabled`, `IngestPermitLimit`, `IngestWindowSeconds` | true, 600, 60 | Per client IP, because every machine shares one key. |
| `ReverseProxy` | `Enabled`, `KnownProxies`, `KnownNetworks` | off | Turn on behind a proxy, so client IPs and HTTPS are seen correctly. |
| `ApiDocumentation` | `Enabled`, `AllowedNetworks` | on except Production | Swagger / Scalar. In Production, enable it and restrict it to the engineers' network. |
| `Seed` | `ApplyMigrations` | false (true in Development) | In production, prefer the idempotent script (below). |
| `RequestLogging` | `Enabled`, `SlowRequestThresholdMs` | true, 2000 | One line per request. Headers and bodies are never logged. |

The API **refuses to start** if the client keys are missing, shorter than 32 characters, contain
placeholders, are shared between the two lists, or are `dev-only-` keys outside Development. The
error names the setting to fix.

### Setting the keys

```powershell
# Generate a key (PowerShell)
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_')

# Development: user-secrets (overrides appsettings.Development.json)
dotnet user-secrets --project src/CarWash.Hub.Api set "ClientKeys:IngestKeys:0:Id"  "k1"
dotnet user-secrets --project src/CarWash.Hub.Api set "ClientKeys:IngestKeys:0:Key" "<generated>"
```

```bash
# Elsewhere: environment variables
ClientKeys__IngestKeys__0__Id=k1
ClientKeys__IngestKeys__0__Key=<generated>
ClientKeys__ReadKeys__0__Id=r1
ClientKeys__ReadKeys__0__Key=<another generated key>
```

### Rotating the shared machine key (no downtime)

1. Add the new key as the second entry: `ClientKeys__IngestKeys__1__Id=k2` and
   `ClientKeys__IngestKeys__1__Key=<new>`. Both keys are now accepted.
2. Update the machines one by one.
3. Watch `client_key_id` on new rows in `machine_messages` (or `clientKeyId` in the read
   endpoints) until no message arrives with `k1` any more.
4. Remove the old entry. A changed key takes effect without a restart wherever the configuration
   source supports reload.

---

## Architecture

### Layers and the dependency rule

```text
src/
  CarWash.Hub.Domain          →  (nothing)
  CarWash.Hub.Application     →  Domain
  CarWash.Hub.Infrastructure  →  Application
  CarWash.Hub.Persistence     →  Application
  CarWash.Hub.Api             →  Application, Infrastructure, Persistence
  (Phase 2) CarWash.Hub.Web   →  Application, Infrastructure, Persistence
tests/
  CarWash.Hub.UnitTests
  CarWash.Hub.IntegrationTests
```

Inner layers never reference outer ones. `CarWash.Hub.Domain.csproj` has no references at all, so
the compiler enforces the innermost layer. `DependencyRuleTests` checks the rest, including that
Application references neither EF Core nor ASP.NET Core. The Api references Persistence only so
`Program.cs` can call `AddPersistence()`.

| Layer | Holds |
|---|---|
| Domain | Entities (`GasStation`, `Machine`, `MachineMessage`), the base entity types and claim/scope constants |
| Application | Ingestion (the `{ machineId, data }` request, its validator, the stored-payload rule), services, DTOs, validators, specifications, the repository and unit-of-work abstractions, and the `.resx` resources (English and Arabic) |
| Infrastructure | Client-key authentication, localization (`X-Lang`), the current-user accessor, the clock, the machine access scope |
| Persistence | `ApplicationDbContext`, entity configurations, migrations, repositories, unit of work, the audit-stamp interceptor, startup migration |
| Api | Controllers, middleware, filters, rate limiting, Swagger/Scalar (with request examples), Serilog |

The conventions follow `cyberani_oms_api`:
- Services return DTOs and throw `AppException` subclasses, which the exception middleware maps to
  problem documents.
- A result filter wraps successful responses in `ApiResponse<T>`.
- Data access goes through specifications and a generic repository; no `IQueryable` leaves
  Persistence.
- Every message is a `ResourceKeys` constant with English and Arabic text.

### Key decisions

- **A simple contract.** The body is JSON with two fields: `machineId` (a string) and `data` (any
  JSON). The request is validated by `IngestMessageRequestValidator`:
  - `machineId` is required, 1–100 characters of `[A-Za-z0-9._:-]`, and matched case-insensitively;
  - `data` is required, not null or blank, and at most 64 KB once stored.
- **Raw storage.** `machine_messages.payload` is `nvarchar(max)` holding `data` as text:
  - a JSON string is stored as its own text, unescaped;
  - anything else is stored as its raw JSON, byte for byte.

  `payload_size_bytes`, `received_at`, `client_key_id`, `source_ip`, `user_agent` and
  `correlation_id` are stored beside it. Phase 2 can re-parse the whole history whenever the parser
  changes. The read endpoints also return `payloadJson` when the payload is a JSON object or array.
- **Auto-registration.** A machine is created by its first message, race-free: the unique index
  decides which of two concurrent inserts wins. It has no gas station until Phase 2 assigns one.
- **Last-seen is derived**, from the `(machine_id, received_at DESC, id DESC)` index, not stored
  on `machines`. Storing it would bump the rowversion on every message and make every dashboard
  edit fail with 409.
- **Two shared keys from configuration.** `client-secret-key` (ingest only) sits on every machine.
  `client-read-key` is never installed on a machine. Keys are compared as SHA-256 digests in
  constant time and are never logged or stored. A fallback authorization policy protects anything
  not explicitly anonymous, unknown paths included.
- **Rate limit per client IP**, because a per-key limit would throttle the whole fleet as one.

### Fixed relative to cyberani_oms_api

The cyberani code these parts were copied from has five defects, fixed here:
1. `ApiResponseWrapperFilter` replaced `CreatedAtActionResult`, so every 201 lost its `Location`
   header. The fix wraps the result in place.
2. `ExceptionHandlingMiddleware` had no case for `BadHttpRequestException`, so a request-size 413
   became a 500. The fix maps it.
3. `UnitOfWork.BeginTransactionAsync` fails under `EnableRetryOnFailure`. It is replaced by
   `ExecuteInTransactionAsync`, which runs inside the execution strategy.
4. The log redaction list missed dashed header names such as `X-Api-Key`. The fix is an explicit
   `SensitiveHeaders` list.
5. The localized model-binding 400 was returned as a `BadRequestObjectResult`, whose
   `application/problem+json` content type `[Produces("application/json")]` silently rewrote. The fix
   returns a `JsonResult`.

### Middleware ordering

1. **Forwarded headers** (when `ReverseProxy:Enabled`), before anything reads the client address.
2. **Correlation id**, so every log line, stored message and error carries it.
3. **Request logging**, which wraps everything, so the logged status and duration are what the
   client saw.
4. **Request localization**, *above* the exception handler. The culture flows through an
   `AsyncLocal`, so below the handler every error body would be in English.
5. **Exception handling**: anything thrown below becomes a localized problem document.
6. **Security headers**, written on `OnStarting`, so error responses carry them too.
7. **HTTPS redirection.**
8. **Swagger / Scalar**, *before* routing and authorization: the fallback policy would otherwise
   demand a key for the documentation itself.
9. **Routing.**
10. **Authentication.**
11. **Rate limiter**, which refuses a flood before any key is hashed.
12. **Authorization**: the `Ingest` and `Read` policies plus the fallback.
13. **Log enrichment** with the key id.
14. **Endpoints.**

---

## Database and migrations

```bash
# Add a migration (Api is the startup project; no design-time factory)
dotnet ef migrations add <Name> --project src/CarWash.Hub.Persistence --startup-project src/CarWash.Hub.Api --output-dir Migrations

# Production: produce a reviewed, idempotent script instead of migrating on startup
dotnet ef migrations script --idempotent --project src/CarWash.Hub.Persistence --startup-project src/CarWash.Hub.Api -o migrate.sql
```

`dotnet-ef` 10.0.12 is pinned in `dotnet-tools.json` (`dotnet tool restore`).

- **Collation.** When the migration creates the database, it creates it with collation
  `Arabic_100_CI_AS_SC_UTF8`. If a DBA creates the database instead, they must use that collation:
  it is what makes machine ids case-insensitive and sorts Arabic station names correctly.
- **Naming.** Tables and columns are snake_case (`machine_messages.received_at`), so hand-written
  analysis SQL over the raw payloads uses one style:

  ```sql
  SELECT TOP 100 JSON_VALUE(payload, '$.status') AS status, COUNT(*) AS n
  FROM machine_messages WHERE ISJSON(payload) = 1
  GROUP BY JSON_VALUE(payload, '$.status') ORDER BY n DESC;
  ```

---

## Testing

```bash
dotnet build CarWash.Hub.sln                               # warnings are errors
dotnet test --project tests/CarWash.Hub.UnitTests
dotnet test --project tests/CarWash.Hub.IntegrationTests   # needs a database, see below
```

- **Unit tests** need no database and no HTTP server. They cover:
  - the `{ machineId, data }` validator (id format, required fields, the 64 KB limit);
  - the stored-payload rule (string data unescaped, other JSON raw);
  - the client-key handler and its options validator;
  - the dependency rule;
  - completeness of the English and Arabic resources.
- **Integration tests** run the real pipeline (`WebApplicationFactory`) against real SQL Server.
  They use the server in `CARWASH_TEST_CONNECTION` if set, otherwise a Testcontainers
  `mssql/server:2022` container (this needs Docker). With neither, they are **skipped**, not
  passed.

  ```powershell
  $env:CARWASH_TEST_CONNECTION = "Server=localhost;Database=carwash_hub_test;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True"
  ```
