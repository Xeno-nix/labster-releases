create table users
(
    user_id                char(36)                             not null
        primary key,
    first_name             varchar(50)                          null,
    last_name              varchar(50)                          null,
    middle_name            varchar(50)                          null,
    suffix                 varchar(5)                           null,
    email                  varchar(100)                         null,
    password_hash          varchar(255)                         null,
    role_type              enum ('admin', 'faculty', 'student') null,
    deleted_at             datetime                             null,
    reset_token            varchar(64)                          null,
    reset_token_expiration datetime                             null,
    created_at             datetime   default CURRENT_TIMESTAMP null,
    last_modified_at       datetime   default CURRENT_TIMESTAMP null,
    password_changed       tinyint(1) default 0                 not null,
    failed_login_attempts   int default 0                        not null,
    lockout_end            datetime                             null,
    two_factor_enabled     boolean default true                 not null,
    constraint email
        unique (email)
);

