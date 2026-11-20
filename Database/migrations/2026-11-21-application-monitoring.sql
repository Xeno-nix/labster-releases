-- Apply before deploying application monitoring. Snapshots are deliberately not stored.
CREATE TABLE monitored_applications (
    application_id CHAR(36) NOT NULL PRIMARY KEY,
    name VARCHAR(120) NOT NULL,
    process_name VARCHAR(200) COLLATE utf8mb4_unicode_ci NOT NULL,
    platform ENUM('Windows', 'MacOS', 'Both') NOT NULL,
    icon_path VARCHAR(255) NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT uq_monitored_application_process UNIQUE (platform, process_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
