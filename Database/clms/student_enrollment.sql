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

create index class_schedule_id
    on student_enrollment (class_schedule_id);

