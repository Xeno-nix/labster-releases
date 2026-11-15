-- Apply once to MySQL 8+ before deploying. Back up first; do not run against production automatically.
-- Resolve existing duplicate (provider,provider_sub) pairs before adding the unique index.
CREATE TABLE IF NOT EXISTS external_identities (
    id CHAR(36) NOT NULL PRIMARY KEY,
    user_id CHAR(36) NOT NULL,
    provider VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    provider_sub VARCHAR(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL
) ENGINE=InnoDB;
CREATE UNIQUE INDEX uq_external_provider_subject ON external_identities(provider, provider_sub);
CREATE UNIQUE INDEX uq_external_provider_user ON external_identities(provider, user_id);

CREATE TABLE onboarding_mutex (id INT NOT NULL PRIMARY KEY) ENGINE=InnoDB;
INSERT INTO onboarding_mutex VALUES (1);

CREATE TABLE google_link_targets (
    user_id CHAR(36) NOT NULL PRIMARY KEY,
    email VARCHAR(254) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    linked_at DATETIME(6) NULL,
    UNIQUE KEY uq_google_target_email (email)
) ENGINE=InnoDB;

CREATE TABLE google_link_invitations (
    id CHAR(36) NOT NULL PRIMARY KEY,
    user_id CHAR(36) NOT NULL,
    email VARCHAR(254) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    token_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    approved_by CHAR(36) NOT NULL,
    created_at DATETIME(6) NOT NULL,
    expires_at DATETIME(6) NOT NULL,
    consumed_at DATETIME(6) NULL,
    revoked_at DATETIME(6) NULL,
    UNIQUE KEY uq_google_invitation_hash (token_hash),
    KEY ix_google_invitation_user (user_id, created_at)
) ENGINE=InnoDB;

CREATE TABLE email_outbox (
    id CHAR(36) NOT NULL PRIMARY KEY,
    recipient VARCHAR(254) NOT NULL,
    subject VARCHAR(255) NOT NULL,
    payload MEDIUMTEXT NULL COMMENT 'Data Protection encrypted HTML; cleared after delivery',
    invitation_id CHAR(36) NULL,
    status VARCHAR(16) NOT NULL DEFAULT 'pending',
    attempts INT NOT NULL DEFAULT 0,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    next_attempt_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    sent_at DATETIME(6) NULL,  
    last_error VARCHAR(100) NULL,
    KEY ix_email_delivery (status, next_attempt_at),
    UNIQUE KEY uq_email_invitation (invitation_id)
);
