# REST API

Base URL when running locally: `http://localhost:5029` (`dotnet run`) or `http://127.0.0.1:8080`
(`docker compose`). In the **Development** environment an interactive reference is served at
`/scalar/v1` (OpenAPI JSON at `/openapi/v1.json`); it is not available in Production. The
generated OpenAPI document currently has no response schemas, so this page is the reference for
response shapes.

All JSON properties are camelCase. Errors use RFC 7807 `ProblemDetails`
(`application/problem+json`).

## Authentication

One shared account, configured through `Auth__Username` / `Auth__PasswordHash`. Every endpoint
except `POST /api/auth/login` and `GET /api/health` requires `Authorization: Bearer <token>`.

```http
POST /api/auth/login
{ "username": "admin", "password": "..." }

200 { "token": "<jwt>", "expiresAt": "2026-10-13T09:30:00+00:00" }
401 ProblemDetails   (wrong username or password)
400 ProblemDetails   (missing fields)
```

The token is an HS256 JWT valid for `Auth:TokenLifetimeHours` (default 24). There is no refresh or
revocation. Missing, malformed, expired, or wrongly signed tokens return `401`.

## Endpoints

| Method | Route | Success | Notes |
|---|---|---|---|
| GET | `/api/health` | 200 / **503** | Public. `503` when PostgreSQL or MQTT is down. |
| GET | `/api/devices` | 200 | Devices registered automatically from telemetry. |
| GET | `/api/devices/{deviceId}/state` | 200 | `404` until the device has sent data. |
| GET | `/api/devices/{deviceId}/telemetry` | 200 | Query: `from`, `to` (ISO 8601), `limit` (default 100, max 1000, clamped). Newest first by `received_at`. |
| POST | `/api/devices/{deviceId}/relay` | **202** | Body `{ "state": true }`. Returns a command id; see lifecycle below. |
| GET | `/api/commands/{commandId}` | 200 | `commandId` must be a GUID; `404` if unknown. |
| GET | `/api/devices/{deviceId}/relay-commands` | 200 | Dashboard commands **and** device-initiated changes. `limit` default 20, max 100. |
| GET | `/api/rfid-cards` | 200 | Whitelist. |
| POST | `/api/rfid-cards` | 201 | Body `{ "uid": "04A1B2C3", "label": "optional" }`; `409` if it exists. |
| PATCH | `/api/rfid-cards/{uid}` | 204 | Body `{ "isActive": false, "label": "..." }` (both optional); `404` if unknown. |
| DELETE | `/api/rfid-cards/{uid}` | 204 | `404` if unknown. |
| GET | `/api/rfid-events` | 200 | Scan log including rejected cards. `limit` default 50, max 200. |
| GET | `/api/stream` | 200 | Server-Sent Events, see below. |

Validation: `deviceId` must match `^[A-Za-z0-9_-]{1,64}$`; RFID `uid` must match
`^[A-Za-z0-9]{1,64}$`; violations return `400`. Every whitelist change republishes the retained
`rfid/cards` message.

## Response shapes

```jsonc
// GET /api/devices/{id}/state  (also the payload of the "device-state" SSE event)
{ "deviceId": "terminal1", "status": "online", "voltageB": 221.4, "currentB": null,
  "powerB": null, "energyB": 2.5, "frequencyB": 50, "relayState": true,
  "lastSeen": "2026-10-13T08:30:09+00:00" }

// POST /api/devices/{id}/relay  -> 202, Location: /api/commands/{commandId}
{ "commandId": "<guid>", "deviceId": "terminal1", "requestedState": true, "status": "sent" }

// GET /api/commands/{commandId}
{ "commandId": "<guid>", "deviceId": "terminal1", "requestedState": true, "actualState": null,
  "status": "sent", "requestedAt": "...", "sentAt": "...", "acknowledgedAt": null, "errorMessage": null }

// GET /api/devices/{id}/telemetry
[ { "id": 1, "deviceId": "terminal1", "voltageB": 221.4, "currentB": null, "powerB": null,
    "energyB": 2.5, "frequencyB": 50, "deviceTimestamp": "...", "receivedAt": "..." } ]
```

`GET /api/devices/{id}/relay-commands` items have the same fields as the command status plus
`source`; `commandId` is a string because device-initiated rows use ids such as `rfid-<guid>`.

## Relay command lifecycle

`POST .../relay` returns **202 Accepted**, never "done". `202` means the backend accepted and
recorded the command request. It does **not** guarantee MQTT delivery, successful execution by the
device, or that the state the device later reports equals the requested one. `status` in the
response is `sent` when the publish succeeded and `failed` when it threw (the HTTP status is still
202 because the command row exists). The caller then polls `GET /api/commands/{id}` or listens to
`/api/stream` until the status is final.

| Status | Meaning | Set by |
|---|---|---|
| `pending` | Row created, nothing published yet | `SendCommandAsync` |
| `sent` | Published to the broker (not a delivery guarantee) | `MarkSent` |
| `executed` | The device acknowledged this command by sending `relay/state` with this `commandId`; `actualState` holds the state it reported | `ProcessRelayStateAsync` |
| `failed` | Publishing threw an exception | `MarkFailed` |
| `timeout` | Still `sent` after `Relay:CommandTimeoutSeconds` (default 30) | `RelayCommandTimeoutService` |

`executed` means "the device acknowledged and reported a state", not "the state equals the one
requested" and not "the physical relay contact was measured" — the backend only knows what the
firmware reports. Compare `actualState` with `requestedState`.

## Server-Sent Events (`GET /api/stream`)

Requires the bearer token, so use a client that can set headers (for example
`@microsoft/fetch-event-source`; the browser-native `EventSource` cannot). The server sends a
`: ping` comment every 15 seconds and `X-Accel-Buffering: no`.

| Event | Data |
|---|---|
| `device-state` | Same shape as `GET /api/devices/{id}/state`. Emitted after telemetry, relay state and status changes. |
| `rfid-scan` | `{ "deviceId", "uid", "recognized", "scannedAt": null, "receivedAt" }` |

`device-state` is emitted by the MQTT handlers (telemetry, relay state, status messages) but **not** by
the offline sweep, so a device that is only marked offline by the sweep produces no event. Events are
not replayed after a reconnect; clients should re-fetch state when the stream
reconnects. The broadcaster is in memory, so this works for a single instance only.
