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

create index computer_unit_id
    on unit_utilization_logs (computer_unit_id);

create index guest_id
    on unit_utilization_logs (guest_id);

create index student_id
    on unit_utilization_logs (student_id);

