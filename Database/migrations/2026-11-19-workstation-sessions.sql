-- Apply before deploying workstation session tracking. Existing utilization tables are unchanged.
CREATE TABLE workstation_sessions (
    session_id CHAR(36) NOT NULL PRIMARY KEY,
    computer_unit_id CHAR(36) NOT NULL,
    user_id CHAR(36) NOT NULL,
    started_at DATETIME(6) NOT NULL,
    ended_at DATETIME(6) NULL,
    active_unit_id CHAR(36) GENERATED ALWAYS AS
        (CASE WHEN ended_at IS NULL THEN computer_unit_id ELSE NULL END) STORED,
    CONSTRAINT uq_workstation_session_active_unit UNIQUE (active_unit_id),
    CONSTRAINT fk_workstation_session_unit FOREIGN KEY (computer_unit_id) REFERENCES computer_units(computer_unit_id),
    CONSTRAINT fk_workstation_session_user FOREIGN KEY (user_id) REFERENCES users(user_id),
    INDEX ix_workstation_session_history (started_at, session_id)
) ENGINE=InnoDB;
