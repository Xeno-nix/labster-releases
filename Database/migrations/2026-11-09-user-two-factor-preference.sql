-- Apply once to an existing CLMS database before deploying the web build.
-- Existing/new accounts retain enabled email 2FA until the user confirms a preference change.
ALTER TABLE users ADD COLUMN two_factor_enabled BOOLEAN NOT NULL DEFAULT TRUE;
