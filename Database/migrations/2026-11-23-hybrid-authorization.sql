-- Apply once after the monitoring migrations, before deploying the new server.
-- Existing accounts keep their role; no account is automatically promoted.
ALTER TABLE users MODIFY role_type ENUM('admin','faculty','student','superadmin') NULL;

CREATE TABLE authorization_roles (
    role_id INT NOT NULL PRIMARY KEY,
    name VARCHAR(20) NOT NULL UNIQUE,
    is_protected BOOLEAN NOT NULL DEFAULT FALSE
) ENGINE=InnoDB;
INSERT INTO authorization_roles VALUES (0,'Admin',FALSE),(1,'Faculty',FALSE),(2,'SuperAdmin',TRUE);

CREATE TABLE role_privileges (
    role_id INT NOT NULL,
    privilege VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PRIMARY KEY(role_id,privilege),
    CONSTRAINT fk_role_privilege_role FOREIGN KEY(role_id) REFERENCES authorization_roles(role_id)
) ENGINE=InnoDB;

-- Match the existing users ID collation rather than changing any identity or historical tables.
SELECT CHARACTER_SET_NAME,COLLATION_NAME INTO @auth_user_charset,@auth_user_collation
FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users' AND COLUMN_NAME='user_id';
SET @auth_override_ddl=CONCAT('CREATE TABLE user_privilege_overrides (
    user_id CHAR(36) CHARACTER SET ',@auth_user_charset,' COLLATE ',@auth_user_collation,' NOT NULL,
    privilege VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    is_granted BOOLEAN NOT NULL,
    PRIMARY KEY(user_id,privilege),
    CONSTRAINT fk_user_privilege_user FOREIGN KEY(user_id) REFERENCES users(user_id)
) ENGINE=InnoDB');
PREPARE clms_auth_schema FROM @auth_override_ddl;
EXECUTE clms_auth_schema;
DEALLOCATE PREPARE clms_auth_schema;

CREATE TABLE authorization_audit (
    audit_id CHAR(36) NOT NULL PRIMARY KEY,
    actor_id CHAR(36) NULL,
    target_type VARCHAR(20) NOT NULL,
    target_key VARCHAR(100) NOT NULL,
    operation VARCHAR(80) NOT NULL,
    privilege VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    previous_value VARCHAR(100) NULL,
    new_value VARCHAR(100) NULL,
    is_allowed BOOLEAN NOT NULL,
    occurred_at DATETIME(6) NOT NULL,
    INDEX ix_authorization_audit_time(occurred_at,audit_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Seed current shared module access. Subsequent startup never reseeds these editable defaults.
INSERT INTO role_privileges(role_id,privilege) VALUES
(1,'dashboard.view'),(1,'laboratories.view'),(1,'units.view'),(1,'units.monitor'),(1,'monitoring.view'),
(1,'inventory.view'),(1,'inventory.manage'),(1,'courses.view'),(1,'courses.manage'),
(1,'terms.view'),(1,'terms.manage'),(1,'schedules.view'),(1,'schedules.manage'),
(1,'bookings.view'),(1,'bookings.manage'),(1,'classlists.view'),(1,'classlists.manage'),
(1,'logbook.view'),(1,'logbook.export');
INSERT INTO role_privileges(role_id,privilege) SELECT 0,privilege FROM role_privileges WHERE role_id=1;
INSERT INTO role_privileges(role_id,privilege) VALUES
(0,'users.view'),(0,'users.create'),(0,'users.edit'),(0,'users.delete'),(0,'users.assign_roles'),
(0,'units.manage'),(0,'monitoring.configure'),(0,'monitoring.lab_screens'),(0,'monitoring.sessions.view'),
(0,'backups.view'),(0,'backups.create');
-- SuperAdmin has automatic catalog access and intentionally has no role_privileges rows.
