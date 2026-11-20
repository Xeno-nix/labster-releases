create table courses
(
    course_id   int auto_increment
        primary key,
    course_code varchar(20)  null,
    course_name varchar(100) null,
    constraint course_code
        unique (course_code)
);

