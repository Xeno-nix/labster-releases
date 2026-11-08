create table guests
(
    guest_id       int auto_increment
        primary key,
    guest_name     varchar(150) not null,
    organization   varchar(150) null,
    contact_number varchar(30)  null,
    purpose        varchar(255) null
);

