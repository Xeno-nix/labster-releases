create table academic_terms
(
    academic_term_id int auto_increment
        primary key,
    academic_year    varchar(20)                        null,
    semester         enum ('First', 'Second', 'Summer') null,
    start_date       date                               null,
    end_date         date                               null
);

