# CarWash Hub: Phase 2 plan (dashboard and gas station portal)

Phase 1 stores every machine message verbatim and serves it back through the API. Phase 2 adds
the dashboard:
- the **owner (Super Admin)** sees every machine at every gas station;
- each **gas station user** sees only the current status of their own station's machines.

It also turns the raw messages into structured status, once the machine format is agreed with the
engineers.

None of this requires restructuring Phase 1. Each step below adds a project, an entity or a
registration next to what exists.

---

## 1. What Phase 1 already provides for Phase 2

| Phase 2 needs | Already in Phase 1 |
|---|---|
| Gas stations | `gas_stations` table and `GasStation` entity (Code, Name, NameAr, City, Address, IsActive, rowversion) |
| Machines assigned to stations | `machines.gas_station_id`, a nullable FK. Machines register themselves and wait unassigned. |
| Status history | `machine_messages`: every message, raw, append-only, indexed `(machine_id, received_at DESC, id DESC)` |
| "Station users see only their machines" | `IMachineAccessScopeProvider`. Every read in `MachineService` already filters by the scope it returns. |
| Users and roles in audit columns | `AuditableEntity.CreatedBy/ModifiedBy` and `ICurrentUserService.UserId`, null until sign-in exists |
| English and Arabic | `SharedResource` `.resx` files, `ILocalizationService`, `X-Lang` / culture providers, `NameAr` columns |
| Seeding the first admin | `SeedOptions` (`Seed` section) and `DbInitializer` |

---

## 2. New project: `CarWash.Hub.Web` (Razor Pages)

```text
src/CarWash.Hub.Web  →  Application, Infrastructure, Persistence
```

- **Hosting.** It is a separate host from the Api and uses the same database, so it deploys and
  scales independently. Machines keep talking only to the Api.
- **Composition.** Its `Program.cs` uses the same composition as the Api: `AddApplication()`,
  `AddInfrastructure()`, `AddPersistence()`, plus Razor Pages, cookie authentication and
  antiforgery.
- **Pages:**

```text
Pages/
  Account/   Login, Logout, AccessDenied
  Admin/     Dashboard, Stations (list/create/edit), Machines (list/assign), UnassignedMachines,
             Users (list/create/edit/deactivate)
  Station/   Dashboard (own machines, current status), MachineHistory
  Shared/    _Layout (lang/dir aware), _LanguageSwitcher, status badges
```

---

## 3. Users, roles and sign-in

- **New entity** `User : AuditableEntity<Guid>, IHasRowVersion` with:
  - UserName, Email, PasswordHash
  - Role: `SuperAdmin` or `StationUser`
  - `GasStationId?` (required for a StationUser)
  - IsActive and a SecurityStamp
- **Passwords** are hashed with `PasswordHasher<T>` from `Microsoft.Extensions.Identity.Core`
  (PBKDF2), as in cyberani_oms_api. There is no full ASP.NET Core Identity: two roles don't
  justify it.
- **Cookie authentication** is the Web project's default scheme. It issues claims
  `sub` (user id), `role` and `station_id`. The security stamp is checked periodically, so
  deactivating a user ends their sessions.
- **The client-key schemes are untouched.** The Api keeps them, and the Web project does not
  register them.
- **The first Super Admin** comes from `SeedOptions`: `Seed:AdminUserName`, `Seed:AdminEmail`, and
  `Seed:AdminPassword` from secrets. It is created by a reconciliation step in `DbInitializer`
  that inserts the admin only when it is missing.
- **`ICurrentUserService`** starts returning `UserId`, `Role` and `GasStationId` from the claims.
  The audit columns then fill in automatically.

---

## 4. Station isolation (enforced in Application, not in the UI)

- **The provider.** Register `UserMachineAccessScopeProvider` in the Web project in place of
  `AllStationsAccessScopeProvider`:
  - **SuperAdmin** gets `MachineAccessScope.AllStations`.
  - **StationUser** gets `MachineAccessScope.Station(user.GasStationId)`.
- **Where it is enforced.** `MachineListSpecification`, `MachineByCodeSpecification` and
  `MachineMessageByIdSpecification` already add `GasStationId == scope` when restricted. A station
  user therefore cannot read another station's machine even by typing its URL; the result is a
  404, not a 403, so other stations' machine ids are not revealed.
- **Tests:**
  - A StationUser of station A gets 404 for station B's machine and message.
  - The machine list contains only station A's machines.
  - A SuperAdmin sees both stations.

---

## 5. Owner workflows

1. **Gas stations:** create and edit (code, English/Arabic name, city, address), and deactivate.
   Deactivate rather than delete: `Restrict` FKs protect a station that still has machines.
2. **Unassigned machines inbox:** machines with `gas_station_id IS NULL`, which have registered
   themselves by sending messages. The owner assigns each to a station and optionally sets a
   display name.
   - Edits use the machine's rowversion, so concurrent admin edits get a clean 409.
   - Telemetry never touches the machine row, so incoming messages never cause that conflict.
   - The inbox is also the defence against spoofing. With one shared key, anyone holding it could
     invent a machine id, but an id appears on a station dashboard only after the owner assigns it.
3. **Users:** create StationUsers bound to a station and SuperAdmins, reset passwords, deactivate.

**Open question for the owner:** when a machine moves to another station, does its earlier history
stay with the old station?
- **If yes:** add a nullable `gas_station_id` to `machine_messages` and stamp it at ingestion from
  the moment machines are assigned. Old rows stay null and belong to "the machine's station at the
  time".
- **If no:** nothing to do; history follows the machine.

---

## 6. From raw messages to structured status

Once the engineers' format is agreed, the steps are:

1. **Parsing state on the raw table.** Add nullable `processing_status` and `processed_at` to
   `machine_messages`. This is a metadata-only change on SQL Server.
2. **`MachineStatusLog` entity.** Typed fields decided from the real data:
   - machine id, reported time, state (`Idle`, `Washing`, `Drying`, `OutOfService`, `Error`, …)
   - program, coins/revenue counters, error code, temperatures, and so on;
   - a back-reference to the source `machine_messages.id`, for traceability.
3. **`IMachineMessageParser`.** One implementation per known format or firmware version, chosen by
   the stored `User-Agent` or by a version field the engineers add inside `data`. It is pure
   Application-layer logic, unit-tested on real stored payloads exported from Phase 1.
4. **`MessageProcessingWorker`.** A `BackgroundService` in its own host or in the Web host:
   - reads `Pending` messages in batches, using the `ExecuteInTransactionAsync` unit of work;
   - writes `MachineStatusLog` rows and upserts **`machine_status_snapshots`**, one row per machine
     holding the current state and last-seen time, with no rowversion, so frequent writes never
     conflict with admin edits;
   - marks each message `Processed`, or `Failed` with the reason.
5. **Re-parsing.** Because the raw history is kept, a parser fix or a new field means resetting
   `processing_status` and running the worker again. No data is ever lost to an early guess.

The dashboards read `machine_status_snapshots` (current status) and `machine_status_logs`
(history and charts), and fall back to the raw latest message for machines whose format is not
parsed yet.

---

## 7. Dashboard behaviour

- **Online/offline.** A machine is online when its last-seen time is within a configurable
  threshold (`Dashboard:OfflineAfterMinutes`, for example 10). Station and fleet tiles count
  online, offline and in-error machines.
- **Owner dashboard:** every station with its machine counts by state, a list of machines in
  error or offline, and drill-down into a station and then a machine's history.
- **Station dashboard:** that station's machines with current state, last seen and current
  program, plus each machine's history.
- **Refresh.** Page refresh first. SignalR live updates are an optional later step: the worker
  publishes a status change and the dashboard updates the machine tile.

---

## 8. Localization and RTL

- **Language selection.**
  - The cookie culture provider is first, then `X-Lang`, then `Accept-Language`.
  - A language switcher in the layout writes the culture cookie.
  - Supported cultures stay en-US and ar-SA.
- **Strings.** `IViewLocalizer` / `IStringLocalizer<SharedResource>` serve page text. New keys go
  into the same `.resx` pair and `ResourceKeys`, so the completeness tests cover the dashboard too.
- **RTL.** `_Layout` sets `<html lang="@culture" dir="@(isArabic ? "rtl" : "ltr")">` and loads
  `bootstrap.rtl.min.css` instead of `bootstrap.min.css` for Arabic. Use logical spacing classes
  (`ms-*`/`me-*`) so layouts mirror correctly.
- **Data.** Station names show through `ILocalizationService.Localize(Name, NameAr)`. Dates use
  the culture's formatting, and times are shown in Asia/Riyadh while stored in UTC.

---

## 9. Retention and housekeeping

- **Purge job.** Raw messages are kept indefinitely in Phase 1. Phase 2 adds a configurable
  retention, `Retention:RawMessageDays`, applied by a nightly job that deletes in batches
  (`DELETE TOP (5000) … WHERE received_at < @cutoff AND processing_status = 'Processed'`), so the
  log and locks stay small. Structured `machine_status_logs` can be kept longer than raw messages.
- **Sizing.** Plan storage as machines × messages per day × average size × 2 (nvarchar). For
  example, 200 machines × 1,440 messages/day × 0.5 KB × 2 ≈ 290 MB/day.

---

## 10. Security follow-ups (optional)

- **Machine identity.** If the shared machine key becomes a concern (any holder can post as any
  machine), the options are:
  - per-machine keys, as a new `machine_keys` table and a third authentication scheme;
  - or HMAC request signing.

  The unassigned inbox already keeps spoofed ids off station dashboards in the meantime.
- **Web hardening.** Antiforgery on every form, a lockout after failed sign-ins, HTTPS-only secure
  cookies, a CSP for the Razor pages, and audit-logging of admin actions. cyberani_oms_api's
  `AuditLogInterceptor` can be ported; it must run *after* the existing `AuditableEntityInterceptor`.

---

## 11. Suggested order of work

1. `User` entity, `UserConfiguration`, migration, Super Admin seeding.
2. `CarWash.Hub.Web` host: cookie auth, login/logout, layout with language switcher and RTL.
3. `UserMachineAccessScopeProvider`, and the station-isolation tests.
4. Stations CRUD, the unassigned machines inbox and assignment, users CRUD.
5. Owner and station dashboards on raw latest messages and derived last-seen.
6. Once the format is known: `MachineStatusLog`, parser, worker, snapshots; switch the dashboards
   to structured status.
7. Retention job, then optional SignalR and security follow-ups.
