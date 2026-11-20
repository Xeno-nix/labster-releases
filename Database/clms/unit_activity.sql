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

create index computer_unit_id
    on unit_activity (computer_unit_id);

create index guest_id
    on unit_activity (guest_id);

create index resolved_by
    on unit_activity (resolved_by);

create index student_id
    on unit_activity (student_id);

