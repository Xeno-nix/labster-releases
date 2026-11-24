-- CLMS table schema documentation
-- Consolidated through 2026-11-24; fresh-database definitions, not an upgrade migration.
-- Select the intended database before using this listing.
-- Referenced character columns require matching character sets and collations.
-- Seed data, views, routines, triggers, and separate query indexes are not included.

-- users
create table users
(
    user_id                char(36)                             not null
        primary key,
    first_name             varchar(50)                          null,
    last_name              varchar(50)                          null,
    middle_name            varchar(50)                          null,
    suffix                 varchar(5)                           null,
    email                  varchar(100)                         null,
    password_hash          varchar(255)                         null,
    role_type              enum ('admin', 'faculty', 'student', 'superadmin') null,
    deleted_at             datetime                             null,
    reset_token            varchar(64)                          null,
    reset_token_expiration datetime                             null,
    created_at             datetime   default CURRENT_TIMESTAMP null,
    last_modified_at       datetime   default CURRENT_TIMESTAMP null,
    password_changed       tinyint(1) default 0                 not null,
    failed_login_attempts   int default 0                        not null,
    lockout_end            datetime                             null,
    two_factor_enabled     boolean default true                 not null,
    constraint email
        unique (email)
);

-- laboratories
create table laboratories
(
    lab_id   int auto_increment
        primary key,
    lab_name varchar(100) null
);

-- academic_terms
create table academic_terms
(
    academic_term_id int auto_increment
        primary key,
    academic_year    varchar(20)                        null,
    semester         enum ('First', 'Second', 'Summer') null,
    start_date       date                               null,
    end_date         date                               null
);

-- courses
create table courses
(
    course_id   int auto_increment
        primary key,
    course_code varchar(20)  null,
    course_name varchar(100) null,
    constraint course_code
        unique (course_code)
);

-- students
create table students
(
    student_id  varchar(12) not null
        primary key,
    first_name  varchar(50) null,
    last_name   varchar(50) null,
    middle_name varchar(50) null,
    suffix      varchar(5)  null
);

-- guests
create table guests
(
    guest_id       int auto_increment
        primary key,
    guest_name     varchar(150) not null,
    organization   varchar(150) null,
    contact_number varchar(30)  null,
    purpose        varchar(255) null
);

-- computer_units
create table computer_units
(
    computer_unit_id      char(36)                                 not null
        primary key,
    unit_name             varchar(100)                             not null,
    unit_number           tinyint unsigned                         not null,
    laboratory_id         int                                      not null,
    deleted_at            datetime                                 null,
    created_at            datetime(6) default CURRENT_TIMESTAMP(6) not null,
    last_modified_at      datetime(6) default CURRENT_TIMESTAMP(6) not null,
    hostname              varchar(255)                             null,
    operating_system      varchar(255)                             null,
    agent_version         varchar(64)                              null,
    is_enabled            tinyint(1)  default 1                    not null,
    credential_generation int         default 0                    not null,
    registered_at         datetime(6)                              null,
    last_seen_at          datetime(6)                              null,
    serial_number         varchar(100)                             null,
    last_activity_scan_at DATETIME(6) NULL,
    last_activity_scan_id CHAR(36) NULL,
    constraint uq_computer_units_lab_number
        unique (laboratory_id, unit_number),
    constraint computer_units_ibfk_1
        foreign key (laboratory_id) references laboratories (lab_id)
);

-- equipment
create table equipment
(
    equipment_id      char(36)                                                                   not null
        primary key,
    equipment_no      varchar(50)                                                                not null,
    serial_number     varchar(100)                                                               null,
    equipment_name    varchar(150)                                                               not null,
    brand             varchar(100)                                                               null,
    model_number      varchar(100)                                                               null,
    equipment_type    varchar(50)                                                                not null,
    status            enum ('available', 'in_use', 'maintenance', 'damaged') default 'available' not null,
    needs_pm          tinyint(1)                                             default 0           not null,
    needs_calibration tinyint(1)                                             default 0           not null,
    remarks           varchar(255)                                                               null,
    deleted_at        datetime                                                                   null,
    constraint equipment_no
        unique (equipment_no)
);

-- supplies
create table supplies
(
    supply_id int auto_increment
        primary key,
    name      varchar(100)                                                                       not null,
    category  varchar(50)                                                                        not null,
    unit      varchar(30)                                                                        not null,
    status    enum ('fully_stocked', 'in_stock', 'low_stock', 'out_of_stock') default 'in_stock' not null,
    remarks   varchar(255)                                                                       null
);

-- external_identities
create table external_identities
(
    id           char(36)     not null
        primary key,
    user_id      char(36)     null,
    provider     varchar(50)  not null,
    provider_sub varchar(255) not null,
    constraint provider_sub
        unique (provider_sub),
    constraint uq_external_provider_subject
        unique (provider, provider_sub),
    constraint uq_external_provider_user
        unique (provider, user_id),
    constraint fk_external_identities_users
        foreign key (user_id) references users (user_id)
            on delete cascade
);

-- refresh_tokens
create table refresh_tokens
(
    id         char(36)                                 not null
        primary key,
    user_id    char(36)                                 not null,
    token_hash varchar(255)                             not null,
    created_at datetime(6) default CURRENT_TIMESTAMP(6) not null,
    expires_at datetime(6)                              not null,
    revoked_at datetime(6)                              null,
    constraint uq_refresh_tokens_token_hash
        unique (token_hash),
    constraint fk_refresh_tokens_user
        foreign key (user_id) references users (user_id)
            on delete cascade
);

-- class_schedule
create table class_schedule
(
    class_schedule_id int auto_increment
        primary key,
    course_id         int         not null,
    instructor_id     char(36)    not null,
    lab_id            int         not null,
    day_of_week       varchar(10) null,
    time_start        time        not null,
    time_end          time        not null,
    term_id           int         not null,
    year_level        tinyint unsigned null,
    section           varchar(10) null,
    deleted_at        datetime    null,
    constraint class_schedule_ibfk_1
        foreign key (course_id) references courses (course_id),
    constraint class_schedule_ibfk_2
        foreign key (instructor_id) references users (user_id),
    constraint class_schedule_ibfk_3
        foreign key (lab_id) references laboratories (lab_id),
    constraint class_schedule_ibfk_4
        foreign key (term_id) references academic_terms (academic_term_id)
);

-- student_enrollment
create table student_enrollment
(
    enrollment_id     int auto_increment
        primary key,
    student_id        varchar(12) not null,
    class_schedule_id int         not null,
    constraint student_id
        unique (student_id, class_schedule_id),
    constraint student_enrollment_ibfk_1
        foreign key (student_id) references students (student_id),
    constraint student_enrollment_ibfk_2
        foreign key (class_schedule_id) references class_schedule (class_schedule_id)
);

-- bookings
create table bookings
(
    booking_id     int auto_increment
        primary key,
    lab_id         int          not null,
    requestor_name varchar(150) not null,
    email          varchar(100) null,
    purpose        varchar(255) null,
    booking_date   date         not null,
    start_time     time         not null,
    end_time       time         not null,
    deleted_at     datetime     null,
    constraint bookings_ibfk_1
        foreign key (lab_id) references laboratories (lab_id)
);

-- attendance_logs
create table attendance_logs
(
    utilization_log_id int auto_increment
        primary key,
    laboratory_id      int          null,
    student_id         varchar(12)  null,
    guest_id           int          null,
    purpose_subject    varchar(255) null,
    course varchar(255) null,
    faculty_in_charge  varchar(50)  null,
    log_date           date         null,
    time_in            datetime     null,
    time_out           datetime     null,
    constraint attendance_logs_laboratory_fk
        foreign key (laboratory_id) references laboratories (lab_id),
    constraint attendance_logs_ibfk_1
        foreign key (student_id) references students (student_id),
    constraint attendance_logs_ibfk_2
        foreign key (guest_id) references guests (guest_id),
    constraint attendance_logs_ibfk_3
        foreign key (faculty_in_charge) references users (user_id)
);

-- unit_utilization_logs
create table unit_utilization_logs
(
    unit_utilization_log_id int auto_increment
        primary key,
    computer_unit_id        char(36)    not null,
    student_id              varchar(12) null,
    guest_id                int         null,
    log_date                date        null,
    time_in                 datetime    null,
    time_out                datetime    null,
    constraint unit_utilization_logs_ibfk_1
        foreign key (computer_unit_id) references computer_units (computer_unit_id),
    constraint unit_utilization_logs_ibfk_2
        foreign key (student_id) references students (student_id),
    constraint unit_utilization_logs_ibfk_3
        foreign key (guest_id) references guests (guest_id)
);

-- unit_availability
create table unit_availability
(
    unit_availability_id int auto_increment
        primary key,
    computer_unit_id     char(36)             not null,
    is_online            tinyint(1) default 1 null,
    last_heartbeat       datetime             null,
    ip_address           varchar(45)          null,
    constraint uq_unit_availability_unit
        unique (computer_unit_id),
    constraint unit_availability_ibfk_1
        foreign key (computer_unit_id) references computer_units (computer_unit_id)
);

-- unit_activity
create table unit_activity
(
    activity_id      int auto_increment
        primary key,
    computer_unit_id char(36)             not null,
    student_id       varchar(12)          null,
    guest_id         int                  null,
    activity_type    varchar(50)          not null,
    description      varchar(255)         null,
    is_resolved      tinyint(1) default 0 null,
    detected_at      datetime             null,
    resolved_by      varchar(50)          null,
    resolved_at      datetime             null,
    constraint unit_activity_ibfk_1
        foreign key (computer_unit_id) references computer_units (computer_unit_id),
    constraint unit_activity_ibfk_2
        foreign key (student_id) references students (student_id),
    constraint unit_activity_ibfk_3
        foreign key (guest_id) references guests (guest_id),
    constraint unit_activity_ibfk_4
        foreign key (resolved_by) references users (user_id)
);

-- screen_cast_sessions
create table screen_cast_sessions
(
    cast_session_id int auto_increment
        primary key,
    initiated_by    char(36)                           not null,
    status          enum ('active', 'ended', 'failed') not null,
    started_at      datetime                           null,
    ended_at        datetime                           null,
    session_token   varchar(255)                       null,
    constraint screen_cast_sessions_ibfk_1
        foreign key (initiated_by) references users (user_id)
);

-- screen_cast_connections
create table screen_cast_connections
(
    screen_cast_connection_id int auto_increment
        primary key,
    cast_session_id           int                                not null,
    computer_unit_id          char(36)                           not null,
    status                    enum ('connected', 'disconnected') not null,
    started_at                datetime                           null,
    ended_at                  datetime                           null,
    constraint cast_session_id
        unique (cast_session_id, computer_unit_id),
    constraint screen_cast_connections_ibfk_1
        foreign key (cast_session_id) references screen_cast_sessions (cast_session_id),
    constraint screen_cast_connections_ibfk_2
        foreign key (computer_unit_id) references computer_units (computer_unit_id)
);

-- agent_refresh_credentials
create table agent_refresh_credentials
(
    credential_id         char(36)                   not null
        primary key,
    computer_unit_id      char(36)                   not null,
    credential_generation int                        not null,
    family_id             char(36)                   not null,
    token_hash            char(44) collate ascii_bin not null,
    created_at            datetime(6)                not null,
    expires_at            datetime(6)                not null,
    revoked_at            datetime(6)                null,
    replaced_by_id        char(36)                   null,
    constraint uq_agent_refresh_hash
        unique (token_hash),
    constraint fk_agent_refresh_successor
        foreign key (replaced_by_id) references agent_refresh_credentials (credential_id),
    constraint fk_agent_refresh_unit
        foreign key (computer_unit_id) references computer_units (computer_unit_id)
);

-- device_credentials
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
    CONSTRAINT ck_device_algorithm CHECK (algorithm = 'ES256')
) ENGINE=InnoDB;

-- device_auth_challenges
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
    CONSTRAINT fk_challenge_replacement FOREIGN KEY (replacement_credential_id) REFERENCES device_credentials(credential_id)
) ENGINE=InnoDB;

-- unit_registrations
create table unit_registrations
(
    registration_id       char(36)                                                              not null
        primary key,
    laboratory_id         int                                                                   not null,
    unit_number           tinyint unsigned                                                      not null,
    registration_code     char(8) collate ascii_bin                                             not null,
    secret_hash           char(44) collate ascii_bin                                            not null,
    public_key VARCHAR(256) CHARACTER SET ascii COLLATE ascii_bin NULL,
    key_fingerprint CHAR(43) CHARACTER SET ascii COLLATE ascii_bin NULL,
    device_credential_id CHAR(36) NULL,
    hostname              varchar(255)                                                          not null,
    operating_system      varchar(255)                                                          not null,
    agent_version         varchar(64)                                                           not null,
    status                enum ('Pending', 'Approved', 'Rejected', 'Expired') default 'Pending' not null,
    created_at            datetime(6)                                                           not null,
    last_modified_at      datetime(6)                                                           not null,
    expires_at            datetime(6)                                                           not null,
    decided_at            datetime(6)                                                           null,
    decided_by            char(36)                                                              null,
    computer_unit_id      char(36)                                                              null,
    credential_generation int                                                                   null,
    claimed_at            datetime(6)                                                           null,
    pending_laboratory_id int as ((case when (`status` = _utf8mb4'Pending') then `laboratory_id` else NULL end)) stored,
    pending_unit_number   tinyint unsigned as ((case when (`status` = _utf8mb4'Pending') then `unit_number` else NULL end)) stored,
    constraint uq_registration_key unique (key_fingerprint),
    constraint fk_registration_credential foreign key (device_credential_id) references device_credentials(credential_id),
    constraint uq_pending_assignment
        unique (pending_laboratory_id, pending_unit_number),
    constraint uq_registration_code
        unique (registration_code),
    constraint fk_registration_actor
        foreign key (decided_by) references users (user_id),
    constraint fk_registration_lab
        foreign key (laboratory_id) references laboratories (lab_id),
    constraint fk_registration_unit
        foreign key (computer_unit_id) references computer_units (computer_unit_id)
);

-- equipment_assignments
create table equipment_assignments
(
    assignment_id    char(36)                                                          not null
        primary key,
    equipment_id     char(36)                                                          not null,
    computer_unit_id char(36)                                                          null,
    laboratory_id    int                                                               null,
    status           enum ('active', 'ended') default 'active'                         not null,
    end_reason       enum ('broken', 'replaced', 'moved', 'returned', 'lost', 'other') null,
    remarks          varchar(255)                                                      null,
    assigned_at      datetime                 default CURRENT_TIMESTAMP                not null,
    ended_at         datetime                                                          null,
    constraint equipment_assignments_ibfk_1
        foreign key (equipment_id) references equipment (equipment_id),
    constraint equipment_assignments_ibfk_2
        foreign key (computer_unit_id) references computer_units (computer_unit_id),
    constraint equipment_assignments_ibfk_3
        foreign key (laboratory_id) references laboratories (lab_id),
    constraint chk_assignment_target
        check (((`computer_unit_id` is not null) and (`laboratory_id` is null)) or
               ((`computer_unit_id` is null) and (`laboratory_id` is not null)))
);

-- workstation_sessions
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
    CONSTRAINT fk_workstation_session_user FOREIGN KEY (user_id) REFERENCES users(user_id)
) ENGINE=InnoDB;

-- student_kiosk_accounts
CREATE TABLE student_kiosk_accounts (
    student_id VARCHAR(12) PRIMARY KEY,
    email VARCHAR(254) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NULL,
    code_hash BINARY(32) NULL,
    code_unit CHAR(36) NULL,
    code_expires DATETIME(6) NULL,
    FOREIGN KEY (student_id) REFERENCES students(student_id)
) ENGINE=InnoDB;

-- student_kiosk_sessions
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

-- monitored_applications
CREATE TABLE monitored_applications (
    application_id CHAR(36) NOT NULL PRIMARY KEY,
    name VARCHAR(120) NOT NULL,
    process_name VARCHAR(200) COLLATE utf8mb4_unicode_ci NOT NULL,
    platform ENUM('Windows', 'MacOS', 'Both') NOT NULL,
    icon_path VARCHAR(255) NULL,
    flag_for_review BOOLEAN NOT NULL DEFAULT FALSE,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    CONSTRAINT uq_monitored_application_process UNIQUE (platform, process_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- abnormal_activities
CREATE TABLE abnormal_activities (
    activity_id CHAR(36) NOT NULL PRIMARY KEY,
    computer_unit_id CHAR(36) NOT NULL,
    activity_type VARCHAR(40) NOT NULL,
    application_id CHAR(36) NULL,
    details VARCHAR(255) NOT NULL,
    detected_at DATETIME(6) NOT NULL,
    ended_at DATETIME(6) NULL,
    active_application_id CHAR(36) GENERATED ALWAYS AS
        (CASE WHEN ended_at IS NULL AND activity_type = 'FlaggedApplication' THEN application_id ELSE NULL END) STORED,
    CONSTRAINT uq_active_flagged_application UNIQUE (computer_unit_id, activity_type, active_application_id),
    CONSTRAINT fk_abnormal_activity_unit FOREIGN KEY (computer_unit_id) REFERENCES computer_units(computer_unit_id),
    CONSTRAINT fk_abnormal_activity_application FOREIGN KEY (application_id) REFERENCES monitored_applications(application_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- authorization_roles
CREATE TABLE authorization_roles (
    role_id INT NOT NULL PRIMARY KEY,
    name VARCHAR(20) NOT NULL UNIQUE,
    is_protected BOOLEAN NOT NULL DEFAULT FALSE
) ENGINE=InnoDB;

-- role_privileges
CREATE TABLE role_privileges (
    role_id INT NOT NULL,
    privilege VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PRIMARY KEY(role_id,privilege),
    CONSTRAINT fk_role_privilege_role FOREIGN KEY(role_id) REFERENCES authorization_roles(role_id)
) ENGINE=InnoDB;

-- user_privilege_overrides
CREATE TABLE user_privilege_overrides (
    user_id CHAR(36) NOT NULL,
    privilege VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    is_granted BOOLEAN NOT NULL,
    PRIMARY KEY(user_id,privilege),
    CONSTRAINT fk_user_privilege_user FOREIGN KEY(user_id) REFERENCES users(user_id)
) ENGINE=InnoDB;

-- authorization_audit
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
    occurred_at DATETIME(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
