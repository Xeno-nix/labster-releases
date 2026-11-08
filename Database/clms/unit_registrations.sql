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

create index ix_registration_created
    on unit_registrations (created_at);

create index ix_registration_status_expiry
    on unit_registrations (status, expires_at);

