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
