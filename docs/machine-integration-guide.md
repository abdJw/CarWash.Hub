# CarWash Hub: machine integration guide

How a car wash machine sends its status to CarWash Hub: one HTTPS request per status update.

- **Endpoint:** `POST https://<host>/api/v1/machine-messages`
- **Required header:** `client-secret-key: <the shared key>`
- **Body:** JSON with two fields, `machineId` and `data`.

The owner gives you the base URL and the key. Every machine uses the **same** key. Keep it in the
firmware's protected configuration, not in source code you share.

Interactive documentation, with a "Try it out" button and ready-made examples, is at
`https://<host>/swagger` (and `https://<host>/scalar`) on the test server.

---

## 1. Quick example

```bash
curl -X POST https://<host>/api/v1/machine-messages \
  -H "client-secret-key: <key>" \
  -H "Content-Type: application/json" \
  -d '{"machineId":"CW-RUH-0001","data":{"status":"Idle","coins":12,"temperatureC":41.5}}'
```

Response, `201 Created`. The `Location` header points at the stored message:

```json
{
  "success": true,
  "message": "Machine message stored.",
  "data": {
    "messageId": 1842,
    "machineId": "CW-RUH-0001",
    "receivedAt": "2026-10-26T19:48:02.7198909+00:00"
  },
  "errors": {},
  "correlationId": "01a179c16db87e1aa8964a69c6d0e9e9"
}
```

**A 201 means the message is stored.** Your firmware only needs to check the status code.

---

## 2. The body

```json
{
  "machineId": "CW-RUH-0001",
  "data": { "...": "anything" }
}
```

### `machineId` (string, required)

- **Format:** 1 to 100 characters, using letters, digits, `.`, `_`, `:` and `-`, and starting with
  a letter or digit. For example `CW-RUH-0001` or `site3:bay_2`. Spaces, `/`, `#` and Arabic
  letters are refused.
- **Case:** ids are **not case-sensitive**, so `cw-ruh-0001` and `CW-RUH-0001` are the same
  machine. The spelling of the first message is kept.
- **No setup needed:** a new machine is registered automatically by its first message.
- **Type:** it must be a JSON string. `"machineId": 1234` is refused; send `"1234"`.

### `data` (any JSON, required)

The machine's status. There is no fixed schema yet; the owner will analyse the real messages and
agree a format with you later. Send whatever your firmware produces:

| You send | Stored as |
|---|---|
| An object: `"data": {"status":"Idle","coins":12}` | `{"status":"Idle","coins":12}`, exactly as sent |
| An array: `"data": [{"t":1},{"t":2}]` | `[{"t":1},{"t":2}]` |
| A string: `"data": "{\"status\":\"Idle\"}"` | `{"status":"Idle"}`, the string's content, unescaped |
| A string of text: `"data": "Idle,12,41.5"` | `Idle,12,41.5` |

`data` must not be missing, `null` or an empty string, and its stored text must be **64 KB or
less**. The whole request must be under 1 MB.

**Other rules:**
- **Content-Type** must be `application/json`. Send UTF-8.
- **Extra fields** next to `machineId` and `data` are ignored. Put everything you want stored
  *inside* `data`.

---

## 3. Responses and errors

Errors are JSON problem documents (`application/problem+json`). They carry:
- an `errors` object naming the field that is wrong;
- a `code`;
- a `correlationId`. Quote it when reporting a problem.

```json
{
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": { "machineId": [ "Machine id is required." ] },
  "code": "Error_ValidationFailed",
  "correlationId": "01a179c172f1767ca55656565f2c1a64"
}
```

| Status | `code` | Meaning | What to do |
|---|---|---|---|
| 201 | – | Stored | – |
| 400 | `Error_ValidationFailed` | `machineId` missing or invalid, or `data` missing, empty or over 64 KB (see `errors`) | Fix the field named in `errors` |
| 400 | `Validation_MalformedRequest` | The body is not valid JSON, or a field has the wrong type | Send valid JSON; `machineId` must be a string |
| 401 | `Auth_ClientSecretKeyMissing` | No `client-secret-key` header | Add the header |
| 401 | `Auth_ClientSecretKeyInvalid` | Wrong key | Check the key; do not retry in a loop |
| 413 | `Error_RequestTooLarge` | The request is over 1 MB | Send less per message |
| 415 | – | Content-Type is not `application/json` | Set `Content-Type: application/json` |
| 429 | `Error_TooManyRequests` | Too many messages from this IP address | Wait `Retry-After` seconds, then retry |
| 5xx | `Error_Unexpected` | Server problem | Retry later with backoff; report the `correlationId` |

**Retry policy:**
- **Retry on:** network errors, 429 (after `Retry-After`) and 5xx, with exponential backoff.
- **Don't retry on:** 400, 401, 413 and 415. These won't succeed until the request or
  configuration changes.
- **Duplicates:** a retry after a timeout may store the same status twice. That is harmless for
  status messages, and including your machine's own reading time inside `data` makes duplicates
  easy to spot later.

**Language:** send `X-Lang: ar` to receive the messages in Arabic. The `code` values never change.

---

## 4. Limits and transport

- **HTTPS only** in production. Use the `https://` URL; a redirect from `http://` is not something
  every embedded HTTP client follows.
- **Rate limit:** 600 messages per minute per IP address by default. Machines behind one internet
  connection share that budget, so tell the owner if a station needs more.
- **Clock:** the server records `receivedAt` itself, so a timestamp isn't required. If your
  machine has a reliable clock, including its own reading time in `data` helps the later analysis.

---

## 5. Checking what was stored

Reading messages back needs a different key: `client-read-key`. This key is never installed on a
machine. If the owner gives you one for testing:

```bash
# The latest message of a machine (payload = the stored data; payloadJson = the same, parsed, when it is JSON)
curl https://<host>/api/v1/machines/CW-RUH-0001/messages/latest -H "client-read-key: <read key>"

# Its history, newest first
curl "https://<host>/api/v1/machines/CW-RUH-0001/messages?pageNumber=1&pageSize=20" -H "client-read-key: <read key>"

# The message at the Location header of a 201
curl https://<host>/api/v1/machine-messages/1842 -H "client-read-key: <read key>"
```

A Postman collection with these examples is in `docs/postman/CarWash.Hub.postman_collection.json`.
Set its `baseUrl`, `clientSecretKey` and `clientReadKey` variables.
