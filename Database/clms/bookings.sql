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

create index lab_id
    on bookings (lab_id);

