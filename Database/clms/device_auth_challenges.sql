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
