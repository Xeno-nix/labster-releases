-- Apply once to the current Unit Registry schema during a coordinated server/service upgrade.
-- MySQL DDL commits implicitly. Back up the schema before applying; do not run against a legacy pre-registry database.
CREATE TABLE device_credentials (
    credential_id CHAR(36) NOT NULL PRIMARY KEY,
    computer_unit_id CHAR(36) NOT NULL,
    credential_generation INT NOT NULL,
    public_key VARCHAR(256) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    key_fingerprint CHAR(43) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    algorithm VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL,
    activated_at DATETIME(6) NULL,
    revoked_at DATETIME(6) NULL,
    replaces_id CHAR(36) NULL,
    replaced_by_id CHAR(36) NULL,
    active_unit_id CHAR(36) GENERATED ALWAYS AS
        (CASE WHEN activated_at IS NOT NULL AND revoked_at IS NULL THEN computer_unit_id ELSE NULL END) STORED,
    CONSTRAINT uq_device_key_fingerprint UNIQUE (key_fingerprint),
    CONSTRAINT uq_device_active_unit UNIQUE (active_unit_id),
    CONSTRAINT fk_device_credential_unit FOREIGN KEY (computer_unit_id) REFERENCES computer_units(computer_unit_id),
    CONSTRAINT fk_device_replaces FOREIGN KEY (replaces_id) REFERENCES device_credentials(credential_id),
    CONSTRAINT fk_device_replaced_by FOREIGN KEY (replaced_by_id) REFERENCES device_credentials(credential_id),
    CONSTRAINT ck_device_algorithm CHECK (algorithm = 'ES256'),
    INDEX ix_device_unit_generation (computer_unit_id, credential_generation)
) ENGINE=InnoDB;

CREATE TABLE device_auth_challenges (
    challenge_id CHAR(36) NOT NULL PRIMARY KEY,
    computer_unit_id CHAR(36) NOT NULL,
    credential_id CHAR(36) NOT NULL,
    context_id CHAR(36) NOT NULL,
    replacement_credential_id CHAR(36) NULL,
    purpose VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    audience VARCHAR(200) NOT NULL,
    nonce CHAR(43) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at DATETIME(6) NOT NULL,
    expires_at DATETIME(6) NOT NULL,
    consumed_at DATETIME(6) NULL,
    verified_at DATETIME(6) NULL,
    CONSTRAINT fk_challenge_unit FOREIGN KEY (computer_unit_id) REFERENCES computer_units(computer_unit_id),
    CONSTRAINT fk_challenge_credential FOREIGN KEY (credential_id) REFERENCES device_credentials(credential_id),
    CONSTRAINT fk_challenge_replacement FOREIGN KEY (replacement_credential_id) REFERENCES device_credentials(credential_id),
    INDEX ix_challenge_expiration (expires_at),
    INDEX ix_challenge_outstanding (computer_unit_id, consumed_at, expires_at)
) ENGINE=InnoDB;

ALTER TABLE unit_registrations
    ADD public_key VARCHAR(256) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD key_fingerprint CHAR(43) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD device_credential_id CHAR(36) NULL,
    ADD CONSTRAINT uq_registration_key UNIQUE (key_fingerprint),
    ADD CONSTRAINT fk_registration_credential FOREIGN KEY (device_credential_id) REFERENCES device_credentials(credential_id);

UPDATE unit_registrations SET status='Expired',last_modified_at=UTC_TIMESTAMP(6)
WHERE status='Pending' AND public_key IS NULL;
UPDATE agent_refresh_credentials SET revoked_at=COALESCE(revoked_at,UTC_TIMESTAMP(6));
-- Existing registered assignments must be soft-deleted in Unit Registry and registered once with new keys.
-- Legacy refresh history and human refresh_tokens remain intact.
