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
    constraint uq_computer_units_lab_number
        unique (laboratory_id, unit_number),
    constraint computer_units_ibfk_1
        foreign key (laboratory_id) references laboratories (lab_id)
);

