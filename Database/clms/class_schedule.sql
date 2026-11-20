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
    year_level        TINYINT UNSIGNED NULL,
    section           VARCHAR(10)     NULL,
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

create index course_id
    on class_schedule (course_id);

create index instructor_id
    on class_schedule (instructor_id);

create index lab_id
    on class_schedule (lab_id);

create index term_id
    on class_schedule (term_id);

