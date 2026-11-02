# CarWash Hub API

Machine data ingestion for self-service car wash machines installed at gas stations.

**Phase 1 (this repository):** every machine pushes its status to one endpoint as JSON:
`{ "machineId": "...", "data": <any JSON> }`. What the machines put in `data` isn't fixed yet, so it
is stored as text exactly as received. Read-back endpoints serve each machine's latest status and
history.

**Phase 2 (in progress):** each message is parsed into service usage (soap, water, vacuum), and a
live Blazor dashboard in the same host shows the fleet to the owner and to each gas station. It
works on a phone and opens in Arabic by default. Refills are recorded per service, and each
machine shows what is left and when it will run out. Alerts reach every open dashboard live when
a service runs low or out or a machine goes quiet. Email and phone push follow.
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

**The dashboard** is at <http://localhost:5180/>.
- In Development, sign in as `admin` with the password `dev-only-Admin#2026` (`Seed:Admin*` in
  `appsettings.Development.json`). That account is created only while no Super Admin exists.
- For live data, run `scripts/Simulate-CarWash.ps1` in another terminal.

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
| `GET /api/v1/machines/{machineId}/usage?from&to` | `client-read-key` | Uses per service, in total and per UTC hour (default last 24 h, at most 31 days) |
| `GET /api/v1/processing/status` | `client-read-key` | Processing backlog (pending and failed messages) and the worker's state |
| `GET /api/v1/reports/finance?from&to&stationIds&services&machineId&grain&includeDetail` | `client-read-key` | Revenue: each use valued at its station's price that day. Totals and the period before, a timeline (`Auto`, `Day`, `Week`, `Month`) and breakdowns by station, service and machine. `from`/`to` are business days in Asia/Amman, at most 366 days apart; repeat `stationIds` and `services` for several. Amounts in JOD |
| `GET /health`, `GET /health/ready` | none | Liveness, and readiness (which also checks the database) |

**Dashboard routes** (signed-in user; the cookie never opens an API endpoint):

| Route | Purpose |
|---|---|
| `/` | Overview: machines that need a refill or will run out within 24 hours, a "Needs attention" list, today's uses against yesterday, silent machines, uses by service over 24 h / 7 d / 30 d |
| `/machines`, `/machines/{id}` | Machine cards with a level tag per service and a Refill button (search, "Needs refill" filter, sort by urgency); one machine's levels, refill history (with void), usage, latest uses and raw messages |
| `/m/{id}/refill` | The phone page a machine's QR sticker opens: record a refill in a few taps |
| `/alerts` | The alert centre: open alerts or the whole history, by kind (refill / no activity); refill or acknowledge in place. The bell in the app bar shows the open, unacknowledged count on every page |
| `/reports/finance` | Finance: revenue, uses, average per day and unpriced uses against the period before; revenue or uses over time by service, per day, week or month; breakdowns by station, service and machine; the station's current prices. Filters for the period (presets or a custom range), stations (Super Admins), services and machine, kept in the address so a report can be bookmarked. A station user sees only their own station |
| `/reports/finance/export?format=xlsx\|csv&…` | The same report as a download: an Excel workbook (summary, timeline, breakdowns and one row per day, station, machine and service) or a CSV of those rows. Same filters and scope as the page |
| `/account/login`, `/account/logout` | Sign-in and sign-out. Static pages with antiforgery; sign-in is rate-limited and locks an account after repeated failures |
| `/account/culture?culture=en-US\|ar-SA` | Language switch (cookie) |
| `/account/password` | Change your own password; required after an admin sets a temporary one |
| `/admin` and `/admin/inbox`, `/admin/stations`, `/admin/users`, `/admin/services` | Super Admins only: name and assign new machines, stations and each station's service prices, users (temporary password, reset, deactivate), service catalog |
| `/admin/sticker/{id}` | Super Admins only: a printable sticker with the machine's name and a QR code to its refill page |

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
| `Seed` | `SeedData` | true | Inserts missing reference data (the soap, water and vacuum services). Never changes an existing row. |
| `BackgroundWorkers` | `Enabled` | true | Runs the message processing worker in this process. Off in the integration tests. |
| `Processing` | `BatchSize`, `SweepIntervalSeconds`, `MaxPastSkew`, `MaxFutureSkew`, `RetryMinDelay` | 200, 30, 7 days, 10 min, 2 s | How messages become usage; see "Message processing" below. |
| `RequestLogging` | `Enabled`, `SlowRequestThresholdMs` | true, 2000 | One line per request. Headers and bodies are never logged. |
| `Dashboard` | `TimeZone`, `DefaultCulture`, `PublicBaseUrl`, `FirstDayOfWeek` | Asia/Amman, ar-SA, null, Saturday | "Today" and every time on screen are in this zone. Startup fails if the zone is unknown. `PublicBaseUrl` is the address QR stickers point at; set it to the live site so a sticker printed from another address still works. `FirstDayOfWeek` starts the finance report's weeks. |
| `DashboardAuth` | `ExpireHours`, `RememberMeDays`, `RevalidationMinutes`, `MaxFailedAttempts`, `LockoutMinutes` | 12, 14, 5, 5, 15 | Session lifetimes, the session re-check interval, and the lockout. |
| `Alerts` | `NoActivityAfterHours`, `BusyWithinMinutes` | 6, 15 | Machines send only when used: silence for this long (outside the station's quiet hours) means "silent" and raises a no-activity alert; a use this recent means "in use". A machine can override the hours in its settings. |
| `Alerts` | `CheckIntervalSeconds`, `StartupGraceMinutes` | 60, 15 | How often the worker re-checks every machine's alerts, and how long after it starts before it may call a machine silent. |
| `Seed` | `AdminUserName`, `AdminEmail`, `AdminDisplayName`, `AdminPassword` | — | The first Super Admin, created only while none exists. Set the password from a secret for the first start, then remove it. |

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
  CarWash.Hub.Web             →  Application, Infrastructure        (Blazor dashboard, a Razor class library)
  CarWash.Hub.Api             →  Application, Infrastructure, Persistence, Web
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

### Message processing

Each ESP32 message is one use of one service:
`{"service":"soap","macAddress":"B0:CB:D8:D0:64:2C","readAt":"2026-10-31T13:03:45Z"}`.

- **Ingest is unchanged.** The message is stored with `processing_status = Pending`, the 201 goes
  back, and an in-process signal wakes `MachineStateWorker`. The worker also sweeps every
  `SweepIntervalSeconds`, so a missed signal costs only latency.
- **The worker** (`MachineStateProcessor`, one transaction per batch):
  - parses the batch with `Esp32ServiceEventParser`;
  - writes one `service_usages` row per use and increments `service_usage_hourly`;
  - updates `machine_states`, which hold last seen, last use and total uses;
  - marks each message `Processed`, `Ignored` (no `service`, or not JSON) or `Failed`, with the
    reason in `processing_error`.
- **Single writer.** A SQL Server application lock (`sp_getapplock`) keeps the worker to one
  process, even during an IIS overlapped recycle. Counters are therefore plain increments, with no
  race.
- **Time.** `readAt` is trusted unless it is more than `MaxPastSkew` in the past or `MaxFutureSkew`
  in the future. An unset clock reporting 1970 is replaced by the receive time.
- **Retries are counted once.** A re-sent event has the same service and `readAt` and arrives at
  least `RetryMinDelay` later. Two real uses in the same second arrive together and are both
  counted, because under-counting would bring a refill alert too late.
- **Unknown services** (`"service":"wax"`) are added to `service_definitions` as unconfigured, so
  their usage is recorded from the first message.
- **Backfill and reprocessing.** Messages stored before processing existed were migrated as
  `Pending` and are processed on the first start. Resetting rows to `Pending` re-parses them.
- `scripts/Simulate-CarWash.ps1` posts the same events, with options for rate, clock skew and
  delayed retries.

### Refills, levels and forecasts

- **A refill** is one transaction on one machine that covers one or more services, each with a
  capacity in **uses** ("soap: 300 uses until the next refill"). It can be backdated up to 30 days,
  and carries optional notes and who recorded it.
- **The form** pre-selects the services that are low, empty or used after empty, and pre-fills each
  capacity from that service's last refill, else the catalog's default capacity. It opens from a
  machine card, the machine page, or the machine's QR sticker on a phone.
- **Levels** per machine and service live in `machine_service_states`:
  remaining = capacity − uses since the refill.
  - Ok, then **Low** at or below the service's low threshold (default 20%), **Empty** at zero, and
    **Used after empty** below zero. A service that was never refilled shows "No refill recorded".
  - A use whose `readAt` is before the refill belongs to the previous fill, so a delayed message
    never empties a fresh refill.
- **Only the worker writes levels.** Recording or voiding a refill (or changing the catalog) saves
  the fact together with a `state_rebuild_requests` row and wakes the worker. The worker then
  recomputes that machine from `service_usages`, so a count never drifts and the UI never races the
  worker.
- **Voiding** is a soft delete with a reason; the previous refill becomes current again. Super Admins
  can void any refill; a station user can void their own within 24 hours.
- **Forecast.** "Runs out in ~6 h" walks a 28-day hour-of-week usage profile forward from now, so a
  quiet night is not counted like a busy afternoon. With under a week of history it uses an
  hour-of-day profile. It needs at least 5 uses of history and looks at most 30 days ahead. The
  worker refreshes forecasts every 15 minutes.

### Alerts

- **What raises one.**
  - A service gets one level alert at a time, the most urgent that applies:
    - **Runs out soon:** still above its threshold, but forecast to run out within 24 hours;
    - **Low:** at or below the service's low threshold;
    - **Empty:** zero uses left;
    - **Used after empty:** still being used after running out.
  - **No activity:** no message for `NoActivityAfterHours`, not counting the station's quiet hours.
- **How severe.** Empty and used-after-empty are critical; the others are warnings. Each shows with
  its icon and word, never colour alone.
- **Who raises them.** Only the processing worker, under its lease, by comparing what should be
  open with what is.
  - It checks after every batch, for the machines just touched, including right after a refill is
    recorded.
  - It checks once a minute for every machine: forecasts come due and machines go quiet with time,
    not with a message.
  - Because the check works from current state, running it twice or after a missed run gives the
    same result.
- **Which machines.** Only machines assigned to an active station and in service. Unassigning a
  machine or taking it out of service closes its alerts.
- **How one closes.**
  - A more urgent alert replaces it ("Escalated").
  - Recording a refill closes it ("Refilled"). This includes a backdated refill, but not a void.
  - A no-activity alert closes on the machine's next message.
- **Who is told.** A toast and a chime in every open tab of every user who can see the machine.
  - Toasts come on a first alert, when it turns critical, and always for used after empty and no
    activity.
  - "Runs out soon" turning into "Low" is not news, so it doesn't toast again.
  - More than three at once become one "N new alerts" toast.
  - The chime plays once per alert per device, after the page has been clicked once (a browser
    rule), and can be turned off from the bell.
- **Acknowledge.** Means "I'm handling it". It clears the bell's count but leaves the alert open
  until the condition clears.
- **Quiet hours** are set per station, 23:00–07:00 by default (local time); a station can be "Open
  24 hours". Silence then doesn't count, so a machine last used before closing isn't silent
  overnight.
- **Live.**
  - Alerts reach the dashboard over its existing SignalR (Blazor Server) connection; there is no
    separate hub.
  - The event carries no content: each tab reloads its alerts through its own user's station
    scope, so no station sees another's.
- **Hosting requirement.** The no-activity check runs only while the site is running. Shared IIS
  unloads an idle site after about 20 minutes, which happens exactly when machines are quiet.
  - Keep it awake with an external uptime ping on `/health` every 5 minutes (for example
    UptimeRobot, free), or with `startMode=AlwaysRunning`.

### Prices and the finance report

- **Prices.** One per station and service, in Jordanian dinars to the fils, set by Super Admins
  (Administration → Stations → Prices). Station users see their station's prices but can't change
  them.
  - A price applies from a business day (Asia/Amman) on. The price on a day is the newest one that
    had started by then, so a new price never changes what earlier days earned.
  - A past start day revalues the uses since then: that is how history is priced the first time,
    or a mistake corrected. Setting a price for a day that already has one replaces it.
  - A price entered by mistake is removed (a soft delete that keeps who set and who removed it),
    and the one before it applies again.
- **Unpriced uses.** A use on a day its station had no price for the service is counted as
  unpriced and shown on its own, never silently as zero. A price of 0 means free.
- **Which station earned it.** The worker stamps each use with the station its machine stands at
  when the use is processed. Moving a machine later moves only its future uses; the money already
  earned stays with the station that earned it.
  - The migration stamped the uses before it with each machine's station at that moment.
  - Uses made while a machine was unassigned belong to no station. A whole-fleet report counts them
    in a note, with a link to assign machines.
- **The rollup.** `service_usage_daily` holds uses per business day, station, machine and service.
  The worker keeps it in the same transaction as the uses.
  - Reports read it, never the raw uses, and value it in memory at the day's prices.
  - It has no soft-delete filter: a deleted machine or station keeps the revenue it earned.
  - **Keep it forever.** It is a few rows per machine per day, and any future retention job must
    not prune it, or past revenue disappears.
- **Who sees what.** The same scope as every read: a station user gets their own station on the
  page and in the export, whatever the address asks for. The read API key sees every station.
- **The comparison period** is as long as the part of the range that has happened: "this month"
  on the 2nd is compared with the two days before it, not with a whole month.

### The dashboard (Blazor Server in the API host)

- **One site.** `CarWash.Hub.Web` is a Razor class library that the host composes with
  `AddDashboard()` and `MapDashboard()`. The shared hosting gives one site, and machines keep
  `/api/*`.
- **Two kinds of caller, kept apart.**
  - The dashboard cookie (`DashboardCookie`) is the default scheme.
  - The API policies name their client-key schemes, so a cookie never satisfies them.
  - The fallback policy still demands a key, so every dashboard endpoint states its own
    authorization: static assets are anonymous, while pages and `/_blazor` require a signed-in user.
- **One DI scope per operation.** Components call services only through
  `DashboardOperationRunner`.
  - It opens a scope per call and copies the circuit's user into `CurrentPrincipalHolder`, so the
    station scope and audit columns see that user.
  - A scoped `DbContext` therefore never lives as long as a circuit.
  - `DependencyRuleTests` enforces this.
- **Live.**
  - After each committed batch, the processing worker publishes a `MachineActivityEvent`.
  - `DashboardEventBus` gives each open tab a bounded queue of its own.
  - Pages coalesce events into one reload every 1.5 s, and pulse the cards of machines just used.
- **Sessions.**
  - The cookie is re-checked against the stored user every 5 minutes, and an open circuit every
    5 minutes. A deactivated user or a changed security stamp ends the session.
  - Data Protection keys live in `data_protection_keys`, so an IIS recycle signs nobody out.
- **Language.** Arabic (`ar-SA`, Gregorian calendar) right-to-left is the dashboard default; the
  API still defaults to English.
- **Administration.**
  - Each admin service checks for a Super Admin itself, beyond the pages' `[Authorize]`.
  - Edits carry the row version they started from, so a stale edit is a 409, never a silent
    overwrite.
  - Changing a user's role, station, active flag or password issues a new security stamp, which
    ends their open sessions. An admin cannot change their own access.
  - A machine becomes visible to a station only when assigned from the New machines inbox. This is
    also the defence against an invented machine id.
- **Dialogs** (refill, void, the admin forms) render inside the themed, mirrored root. That gives
  them the theme's colours in light and dark, and the page's reading direction in Arabic.
- **Charts.**
  - Hand-built HTML and CSS: no chart library and no inline script.
  - Service colours come from a palette validated for colour-vision deficiency, and are stepped
    separately for light and dark.
  - Every chart has a legend and a table view.

### Middleware ordering

1. **Forwarded headers** (when `ReverseProxy:Enabled`), before anything reads the client address.
2. **Correlation id**, so every log line, stored message and error carries it.
3. **Request logging**, which wraps everything, so the logged status and duration are what the
   client saw.
4. **Request localization**, *above* the exception handler. The culture flows through an
   `AsyncLocal`, so below the handler every error body would be in English.
5. **Exception handling**: anything thrown below becomes a localized problem document. Directly
   inside it, dashboard paths get an error *page* instead (`UseDashboardErrorHandling`).
6. **Security headers**, written on `OnStarting`, so error responses carry them too.
   - API and health paths get `default-src 'none'`, and the documentation keeps its own policy.
   - Dashboard paths allow scripts only from this site (none inline) plus the Blazor WebSocket,
     and their HTML is `no-store`.
7. **HTTPS redirection.**
8. **Swagger / Scalar**, *before* routing and authorization: the fallback policy would otherwise
   demand a key for the documentation itself.
9. **Routing.**
10. **Authentication.**
11. **Rate limiter**, which refuses a flood before any key is hashed.
12. **Authorization**: the `Ingest`, `Read` and dashboard policies, plus the fallback.
13. **Antiforgery**, for the dashboard's form posts.
14. **Log enrichment** with the key id.
15. **Endpoints**: controllers, health checks, then `MapDashboard()`.

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
- **`QUOTED_IDENTIFIER`.** `station_service_prices` has a filtered unique index (one live price per
  station, service and day). Anything that creates it or writes to the table must run with
  `QUOTED_IDENTIFIER ON`. That is the default for the app, SSMS and Azure Data Studio, but not for
  `sqlcmd`: pass `-I` when running the migration script or ad-hoc SQL with it.
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
  - completeness of the English and Arabic resources;
  - the level and forecast maths, and the alert rules: which alert a level calls for, severity, who
    is told, refill detection, and silence across quiet hours.
- **Integration tests** run the real pipeline (`WebApplicationFactory`) against real SQL Server.
  They use the server in `CARWASH_TEST_CONNECTION` if set, otherwise a Testcontainers
  `mssql/server:2022` container (this needs Docker). With neither, they are **skipped**, not
  passed.

  ```powershell
  $env:CARWASH_TEST_CONNECTION = "Server=localhost;Database=carwash_hub_test;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True"
  ```
