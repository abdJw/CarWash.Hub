# CarWash Hub: ESP32 device contract

What the server does for the ESP32 + SIMCom A7670G firmware described in the device engineer's
*ESP32 Carwash API Integration Guide* (protocol baseline 2026-11-03). Use this page to check the
firmware against the server, or the server against the firmware.

| | |
|---|---|
| Events | `POST /api/v1/machine-messages` |
| Settings | `GET /api/v1/settings?machineId=AA:BB:CC:DD:EE:FF` |
| Header (both) | `client-secret-key: <the machine key>` |
| Transport | HTTPS, TLS 1.2 or 1.3; the device trusts the root CA (ISRG Root X1 for the current host) |

The earlier firmware format, `{"service":"soap","macAddress":"…","readAt":"…"}` in `data`, is still
accepted and processed as before. A payload is read as the event format when `data` is a JSON object
with a string `event`.

---

## 1. Events: `POST /api/v1/machine-messages`

```json
{
  "machineId": "AA:BB:CC:DD:EE:FF",
  "data": {
    "event": "payment_success",
    "device_mac": "AA:BB:CC:DD:EE:FF",
    "boot_id": "0123456789ABCDEF",
    "event_id": "AA:BB:CC:DD:EE:FF-0123456789ABCDEF-payment_success-1",
    "service": "water",
    "price_qirsh": 50,
    "paid_qirsh": 50,
    "duration_seconds": 180,
    "uptime_ms": 120000,
    "water_available": true
  }
}
```

Field names, event names and service codes are case-sensitive.

| Event | What the server records |
|---|---|
| `payment_success` | One sale of `service` (`water`, `soap`, `minute_water`, `vacuum`). Sales value = `price_qirsh` / 100 JOD; cash collected = `paid_qirsh` / 100 JOD, kept separately. Run time = `duration_seconds`. Also updates the water state. **The only event that is a sale.** |
| `device_boot` | Last start-up time and `boot_id`, the water state. Never touches the desired settings. |
| `water_status` | The water state (`water_available`) and a heartbeat. The latest message wins. |
| `settings_ack` | The machine's answer to a settings revision; see section 3. |
| anything else | Stored and marked *ignored*. |

**Answers.**

| Status | When |
|---|---|
| `201` | The message is stored durably (committed to the database). Also for a **repeat** of an `event_id` already stored for the machine: the original message is returned, `"duplicate": true`, and nothing new is stored or counted. Never `409`. |
| `400` | Only for an unusable envelope: `machineId` missing or invalid, `data` missing, or not JSON. |
| `401` | Key missing or wrong. |
| `413` / `415` | Body over 1 MB / `Content-Type` not `application/json`. |
| `429` | Over the per-address limit (600 per minute by default). `Retry-After` is whole seconds. |
| `5xx` | Server problem; retry. |

**Validation happens after the 201, never at the door.** The firmware stops posting after a 400, so
a payload with a bad price, a bad service code or a `device_mac` that is not the `machineId` is still
answered `201`. The message is then marked *failed* with the reason, and **no sale is recorded from
it**. Failed messages are counted on `GET /api/v1/processing/status` and listed on the machine's
page. A bad `duration_seconds`, `water_available` or `boot_id` is only dropped; the sale stands.

**Time.** `uptime_ms` is not a clock, so every event is timed by its arrival (`received_at`). A
payment delivered late after an outage lands on the day it arrived.

---

## 2. Settings: `GET /api/v1/settings?machineId=…`

```http
HTTP/1.1 200 OK
Content-Type: application/json
Content-Length: 291
Cache-Control: no-store

{"revision":1,"command":"update_settings","services":{"water":{"price_qirsh":50,"durations_seconds":[180,180,210]},"soap":{"price_qirsh":50,"durations_seconds":[60,90,90]},"minute_water":{"price_qirsh":25,"duration_seconds":60},"vacuum":{"price_qirsh":50,"durations_seconds":[180,180,210]}}}
```

- **No settings** (unknown machine, unassigned, or its station not fully configured yet):
  `200 {"revision":0}`. Never 404 or 204. Polling never registers a machine.
- **The body is the document itself** - no envelope - minified, ASCII, in this field order, with an
  exact `Content-Length`, no compression and no chunked encoding. It is about 300 bytes at most, well
  under the firmware's 768 / 640 byte limits.
- **Where the values come from.** The **prices** are the station's prices in the dashboard, as in
  effect today (Asia/Amman business date). The **run times** are the station's, set in the same
  dialog. Every machine at a station gets the same values.
- **Revisions.** The server recomputes every machine's snapshot when prices, run times, a machine's
  station or a station change, and at least every minute (`DeviceSettings:ReconcileIntervalSeconds`),
  which is what makes a dated price reach the machines shortly after midnight. Changed values get the
  next revision; equal values never do; a revision is never reused for different values (it is also
  issued above any revision a machine has reported).
- **Never a partial snapshot.** If a station is missing a price or run time for any of the four
  services, or a price is not one the machine accepts, its machines keep being sent the last complete
  snapshot, and the dashboard shows the settings as *not sent* with the reason.
- **Limits enforced when they are entered:** a machine service's price must be 0.05-9.95 JOD in
  steps of 0.05 (so `price_qirsh` is 5-995, divisible by 5); a run time 1-5999 seconds; three run
  times, or one for `minute_water`.
- **Polling budget:** a separate per-address limit of 1200 polls per minute by default
  (`RateLimiting:SettingsPermitLimit`), so polling never uses up the budget payments are posted from.

---

## 3. Acknowledgements and the dashboard

```json
{ "event": "settings_ack", "revision": 1, "status": "saved", "...": "common fields" }
```

| `status` | Effect |
|---|---|
| `saved`, `unchanged` | The revision is now **applied**, if it is one the server issued and newer than the applied one. |
| `busy` | Shown as *machine busy*; the snapshot stays available and the machine retries. |
| `storage_error`, `invalid_settings`, `invalid_command`, `invalid_json`, unknown | Shown as *refused by the machine*; never applied. |

A dashboard save is **pending** until the matching acknowledgement arrives. An older reply arriving
late never lowers the applied revision. Acknowledgements are deduplicated by `event_id` like every
event, never by revision. The desired snapshot is kept after it is applied, so a machine that
reboots and polls again gets the same revision.

The machine page shows the revision sent and confirmed, the last reply, the values, the last
start-up and the last report. Machine cards show the water state and a settings chip.

---

## 4. Alerts

| Alert | When |
|---|---|
| Offline | A machine on this firmware has sent nothing for 3 minutes (`Alerts:OfflineAfterMinutes`), outside its station's quiet hours. Closes on its next message. |
| No activity | For this firmware, measured in **sales**: no sale for the machine's threshold (default 6 h), while it is online. Not raised while the machine is offline. |
| No water | `water_available` has been false for 1 minute (`Alerts:WaterUnavailableAfterMinutes`), the machine is online, outside quiet hours. Critical. Closes when it reports water again. |

Machines on the earlier firmware keep the earlier no-activity rule: no message of any kind.

---

## 5. Security notes

- **The machine id is an identifier, not a credential.** Every machine shares one key, so any holder
  of it can read any machine's settings (prices and run times - nothing secret) and could post events
  for any machine. A new machine id appears only in the admin inbox until a Super Admin assigns it,
  and a forged `payment_success` for an assigned machine would count as a sale. Per-device keys would
  close this; they need per-device firmware provisioning.
- The dashboard's `client-read-key` is refused by both device endpoints, and the machine key opens
  no dashboard or read endpoint.
- The current host's certificate is a 90-day Let's Encrypt certificate, so the firmware must trust
  the **root** (ISRG Root X1), not the leaf. Moving the site to another domain may change the CA.
