create table students
(
    student_id  varchar(12) not null
        primary key,
    first_name  varchar(50) null,
    last_name   varchar(50) null,
    middle_name varchar(50) null,
    suffix      varchar(5)  null
);

