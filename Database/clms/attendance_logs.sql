create table attendance_logs
(
    utilization_log_id int auto_increment
        primary key,
    laboratory_id      int          null,
    student_id         varchar(12)  null,
    guest_id           int          null,
    purpose_subject    varchar(255) null,
    faculty_in_charge  varchar(50)  null,
    log_date           date         null,
    time_in            datetime     null,
    time_out           datetime     null,
    constraint attendance_logs_ibfk_1
        foreign key (student_id) references students (student_id),
    constraint attendance_logs_ibfk_2
        foreign key (guest_id) references guests (guest_id),
    constraint attendance_logs_ibfk_3
        foreign key (faculty_in_charge) references users (user_id)
);

create index faculty_in_charge
    on attendance_logs (faculty_in_charge);

create index guest_id
    on attendance_logs (guest_id);

create index student_id
    on attendance_logs (student_id);

