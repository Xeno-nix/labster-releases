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

