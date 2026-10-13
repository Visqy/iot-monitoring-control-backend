-- 001_initial_schema.sql
-- Initial schema for IotBackend. Idempotent (CREATE TABLE IF NOT EXISTS), so DatabaseInitializer
-- can safely run it on every startup.
--
-- NOTE: this script does NOT migrate. If a table already exists with an older shape, the
-- CREATE TABLE IF NOT EXISTS statements are silently skipped and new columns will not appear.
-- For this proof of concept the database is simply dropped and recreated when the schema
-- changes. Introduce numbered migrations (002_*.sql, ...) before keeping data you care about.
-- There are intentionally no foreign keys: telemetry is stored before the device row exists
-- (devices are auto-registered from incoming telemetry).

-- Device master data
CREATE TABLE IF NOT EXISTS devices (
    device_id  VARCHAR PRIMARY KEY,
    name       TEXT,
    location   TEXT,
    status     TEXT NOT NULL DEFAULT 'unknown',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Raw telemetry history, one row per incoming MQTT message
CREATE TABLE IF NOT EXISTS telemetry (
    id        BIGSERIAL PRIMARY KEY,
    device_id VARCHAR NOT NULL,
    topic     TEXT NOT NULL,

    voltage_b   DOUBLE PRECISION,
    current_b   DOUBLE PRECISION,
    power_b     DOUBLE PRECISION,
    energy_b    DOUBLE PRECISION,
    frequency_b DOUBLE PRECISION,

    device_timestamp TIMESTAMPTZ,
    received_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    raw_payload JSONB NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_telemetry_device_received
ON telemetry (device_id, received_at DESC);

-- One row per device with its latest known state. It merges three sources (telemetry,
-- relay/state, status/LWT), so it is more than a performance cache of the telemetry table.
CREATE TABLE IF NOT EXISTS device_current_state (
    device_id VARCHAR PRIMARY KEY,

    status      TEXT NOT NULL DEFAULT 'unknown',
    voltage_b   DOUBLE PRECISION,
    current_b   DOUBLE PRECISION,
    power_b     DOUBLE PRECISION,
    energy_b    DOUBLE PRECISION,
    frequency_b DOUBLE PRECISION,
    relay_state BOOLEAN,

    last_seen  TIMESTAMPTZ,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Every relay change. Dashboard commands follow the full lifecycle
-- (pending -> sent -> executed/failed/timeout); device-initiated changes (RFID, boot) are
-- inserted directly as 'executed'. command_id is VARCHAR rather than UUID: dashboard commands
-- hold a standard UUID, device-initiated rows hold a synthetic "{source}-{guid}" id.
CREATE TABLE IF NOT EXISTS relay_commands (
    command_id VARCHAR PRIMARY KEY,
    device_id  VARCHAR NOT NULL,

    requested_state BOOLEAN NOT NULL,
    actual_state    BOOLEAN,

    source TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',

    requested_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at         TIMESTAMPTZ,
    acknowledged_at TIMESTAMPTZ,

    error_message TEXT,
    raw_payload   JSONB
);

CREATE INDEX IF NOT EXISTS idx_relay_commands_device_requested
ON relay_commands (device_id, requested_at DESC);

-- Global RFID whitelist managed from the dashboard. Cards are toggles: presence plus
-- is_active is the only thing that decides whether a scan is accepted.
CREATE TABLE IF NOT EXISTS rfid_cards (
    uid VARCHAR PRIMARY KEY,

    label     TEXT,
    is_active BOOLEAN NOT NULL DEFAULT true,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Append-only log of every card scan, including rejected ones. A rejected card never
-- changes the relay, so it never reaches relay_commands; this table is its only trace.
CREATE TABLE IF NOT EXISTS rfid_events (
    id        BIGSERIAL PRIMARY KEY,
    device_id VARCHAR NOT NULL,
    uid       TEXT NOT NULL,

    recognized BOOLEAN NOT NULL,

    scanned_at  TIMESTAMPTZ,
    received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    raw_payload JSONB NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_rfid_events_received
ON rfid_events (received_at DESC);
