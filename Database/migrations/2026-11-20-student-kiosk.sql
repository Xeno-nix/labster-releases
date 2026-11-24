USE `clms`;
SELECT DATABASE();

CREATE TABLE student_kiosk_accounts (
    student_id VARCHAR(12) PRIMARY KEY,
    email VARCHAR(254) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NULL,
    code_hash BINARY(32) NULL,
    code_unit CHAR(36) NULL,
    code_expires DATETIME(6) NULL,
    FOREIGN KEY (student_id) REFERENCES students(student_id)
) ENGINE=InnoDB;

CREATE TABLE student_kiosk_sessions (
    unit_id CHAR(36) PRIMARY KEY,
    session_id CHAR(36) NOT NULL,
    student_id VARCHAR(12) NOT NULL,
    log_id INT NOT NULL,
    expires_at DATETIME(6) NOT NULL,
    FOREIGN KEY (unit_id) REFERENCES computer_units(computer_unit_id),
    FOREIGN KEY (student_id) REFERENCES student_kiosk_accounts(student_id),
    FOREIGN KEY (log_id) REFERENCES unit_utilization_logs(unit_utilization_log_id)
) ENGINE=InnoDB;
