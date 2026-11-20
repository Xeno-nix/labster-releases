-- Apply after 2026-11-21-application-monitoring.sql. No process polling or scan history is introduced.
-- Apply once with a current schema selected. MySQL DDL commits implicitly.
ALTER TABLE monitored_applications ADD COLUMN flag_for_review BOOLEAN NOT NULL DEFAULT FALSE;

-- A single durable watermark per unit prevents stale/replayed scans from changing activity state.
ALTER TABLE computer_units
    ADD COLUMN last_activity_scan_at DATETIME(6) NULL,
    ADD COLUMN last_activity_scan_id CHAR(36) NULL;

-- Existing units inherit the database collation, while the application registry explicitly uses
-- utf8mb4. Match each referenced ID column rather than assuming both tables use the same collation.
SELECT CHARACTER_SET_NAME, COLLATION_NAME INTO @activity_unit_charset, @activity_unit_collation
FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'computer_units' AND COLUMN_NAME = 'computer_unit_id';
SELECT CHARACTER_SET_NAME, COLLATION_NAME INTO @activity_app_charset, @activity_app_collation
FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'monitored_applications' AND COLUMN_NAME = 'application_id';

SET @activity_ddl = CONCAT('CREATE TABLE abnormal_activities (
    activity_id CHAR(36) NOT NULL PRIMARY KEY,
    computer_unit_id CHAR(36) CHARACTER SET ', @activity_unit_charset, ' COLLATE ', @activity_unit_collation, ' NOT NULL,
    activity_type VARCHAR(40) NOT NULL,
    application_id CHAR(36) CHARACTER SET ', @activity_app_charset, ' COLLATE ', @activity_app_collation, ' NULL,
    details VARCHAR(255) NOT NULL,
    detected_at DATETIME(6) NOT NULL,
    ended_at DATETIME(6) NULL,
    active_application_id CHAR(36) CHARACTER SET ', @activity_app_charset, ' COLLATE ', @activity_app_collation, ' GENERATED ALWAYS AS
        (CASE WHEN ended_at IS NULL AND activity_type = ''FlaggedApplication'' THEN application_id ELSE NULL END) STORED,
    CONSTRAINT uq_active_flagged_application UNIQUE (computer_unit_id, activity_type, active_application_id),
    CONSTRAINT fk_abnormal_activity_unit FOREIGN KEY (computer_unit_id) REFERENCES computer_units(computer_unit_id),
    CONSTRAINT fk_abnormal_activity_application FOREIGN KEY (application_id) REFERENCES monitored_applications(application_id),
    INDEX ix_abnormal_activity_active (ended_at, detected_at, activity_id),
    INDEX ix_abnormal_activity_unit (computer_unit_id, detected_at, activity_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4');
PREPARE clms_activity_schema FROM @activity_ddl;
EXECUTE clms_activity_schema;
DEALLOCATE PREPARE clms_activity_schema;
