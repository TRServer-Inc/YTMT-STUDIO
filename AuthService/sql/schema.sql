CREATE TABLE IF NOT EXISTS auth_sessions (
    session_id UUID PRIMARY KEY,
    account_id TEXT NOT NULL,
    username TEXT NOT NULL,
    session_version INTEGER NOT NULL,
    refresh_token_hash CHAR(64) NOT NULL UNIQUE,
    access_token_hash CHAR(64) NOT NULL UNIQUE,
    access_expires_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_used_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_auth_sessions_account_active
    ON auth_sessions (account_id, expires_at)
    WHERE revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_auth_sessions_expiry
    ON auth_sessions (expires_at);

CREATE TABLE IF NOT EXISTS auth_events (
    event_id BIGSERIAL PRIMARY KEY,
    account_id TEXT NULL,
    event_type TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_auth_events_created_at
    ON auth_events (created_at DESC);