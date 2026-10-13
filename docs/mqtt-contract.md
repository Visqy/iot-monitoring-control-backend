# MQTT contract

This document describes what the backend **subscribes to and publishes**, derived from the code in
`IotBackend/` (`MqttSubscriberService`, `MqttClientService`, `TelemetryService`,
`RelayCommandService`, `DeviceService`, `RfidService`). The device firmware is not part of this
repository; the "device" columns describe what the backend expects, not what any firmware is
guaranteed to send.

`{deviceId}` is the **first segment of the topic** (`terminal1/pzem` → `terminal1`). It is also the
primary key used in the database and in the REST API, so a device must publish under the same
prefix it listens on.

## Topics

| Direction | Topic | Retained | Handled by |
|---|---|---|---|
| device → backend | `{deviceId}/pzem` | no | `TelemetryService.ProcessTelemetryAsync` |
| device → backend | `{deviceId}/relay/state` | no | `RelayCommandService.ProcessRelayStateAsync` |
| device → backend | `{deviceId}/status` | device-defined (an LWT is typically retained) | `DeviceService.ProcessStatusMessageAsync` |
| device → backend | `{deviceId}/rfid` | no | `RfidService.ProcessScanAsync` |
| backend → device | `{deviceId}/relay/set` | no | published by `RelayCommandService.SendCommandAsync` |
| backend → devices | `rfid/cards` | **yes** | published by `RfidService` after every whitelist change |

Topic filters are configurable under the `Mqtt` configuration section; the values above are the
defaults in `appsettings.json`.

## Transport and delivery semantics

| Aspect | Behaviour in this backend |
|---|---|
| Connection | One `MqttClientService` (singleton). TLS is on by default (`Mqtt:UseTls`); disabling it is meant only for a local broker. |
| Session | Clean session. The backend does not rely on broker-side queued messages. |
| QoS (backend publishes) | **Not configured explicitly**, so MQTTnet defaults apply: **QoS 0** (at most once). An automated test (`MqttEndToEndTests`) asserts this for the backend's `relay/set` and `rfid/cards` publishes. |
| QoS (backend subscriptions, device publishes) | The backend does not set a subscription QoS (MQTTnet default; not asserted by tests). The QoS of messages published by the device depends on the firmware and is **not verified here**. |
| Reconnect | `MqttSubscriberService` polls `IsConnected` every `Mqtt:ReconnectDelaySeconds` (default 5 s), reconnects and **re-subscribes** to all topic filters. |
| Last Will | The backend sets none. The `{deviceId}/status` LWT is a device-side concern. |

Consequences of QoS 0 and a non-retained `relay/set` (broker behaviour for offline clients was
not tested, so these are expected rather than demonstrated):

- A command published while the device is offline or disconnected is not expected to be delivered later.
- The command then stays `sent` until the timeout sweep marks it `timeout`. There is no retry.
- A lost `relay/state` acknowledgement looks the same as a command the device never executed, until
  the next report arrives.

## Payloads

### `{deviceId}/pzem` — telemetry

```json
{
  "voltageB": 221.4,
  "currentB": null,
  "powerB": null,
  "energyB": 2.5,
  "freqB": 50.0,
  "timestamp": "2026-10-13T15:30:00+07:00"
}
```

- Property names are case-insensitive. Every measurement may be `null` (for example when the relay
  is off or the sensor read fails); the backend stores `null`.
- `timestamp` accepts ISO 8601 with offset, or the legacy `yyyy-MM-dd HH:mm:ss` which is
  interpreted as **UTC+7 (WIB)** and converted to UTC. Missing or unparsable values are stored as
  `NULL`; `received_at` (server time) is always stored and is used for ordering and filtering.
- Units are whatever the device reports; the backend does not convert or validate ranges.
- Invalid JSON is logged and dropped.

### `{deviceId}/relay/state` — relay state reported by the device

```json
{
  "commandId": "5eca31c7-0696-40ba-9659-4886bfe5bc12",
  "state": "ON",
  "source": "dashboard",
  "executedAt": "2026-10-13T15:30:05+07:00"
}
```

| Field | Meaning |
|---|---|
| `commandId` | GUID of the command being confirmed, or `null`/absent when the change was not caused by a dashboard command (RFID tap, boot). A value that is not a GUID makes the whole payload unparsable and it is ignored. |
| `state` | `"ON"` or `"OFF"` (case-insensitive). Anything else is ignored. |
| `source` | Free text such as `dashboard`, `rfid`, `boot`. Used for the audit row when `commandId` is `null`; defaults to `unknown`. |
| `executedAt` | Optional device time; normalised to UTC for `acknowledged_at`. |

A bare `ON` / `OFF` string (no JSON) is accepted as a legacy format. The backend treats `state` as
what the firmware reports; it does not verify the physical relay contact.

### `{deviceId}/status`

Plain text `online` or `offline` (case-insensitive, surrounding whitespace ignored). Other values
are ignored. `online` also refreshes `last_seen`. The device may publish these explicitly; an MQTT
Last Will, if the firmware configures one, lets the broker publish `offline` after an unexpected
disconnection. Independently of this topic, the inactivity sweep marks a device `offline` when no
relevant message has arrived within the configured threshold.

### `{deviceId}/rfid` — card scan

```json
{ "uid": "04A1B2C3", "recognized": true }
```

`uid` is trimmed and upper-cased. `recognized` is the **device's** decision against its local copy
of the whitelist; the backend only logs it (including rejected cards).

### `{deviceId}/relay/set` — command to the device (backend → device)

```json
{
  "commandId": "5eca31c7-0696-40ba-9659-4886bfe5bc12",
  "state": "ON",
  "source": "dashboard",
  "issuedAt": "2026-10-13T08:30:00+00:00"
}
```

### `rfid/cards` — whitelist (backend → all devices, retained)

A JSON array of the **active** UIDs only, replacing the previous list completely:

```json
["04A1B2C3", "04D5E6F7"]
```

If publishing fails after the database change, the failure is logged and the database remains the
source of truth; the retained message can then be stale until the next whitelist change.
