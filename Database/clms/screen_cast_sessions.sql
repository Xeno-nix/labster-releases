create table screen_cast_sessions
(
    cast_session_id int auto_increment
        primary key,
    initiated_by    char(36)                           not null,
    status          enum ('active', 'ended', 'failed') not null,
    started_at      datetime                           null,
    ended_at        datetime                           null,
    session_token   varchar(255)                       null,
    constraint screen_cast_sessions_ibfk_1
        foreign key (initiated_by) references users (user_id)
);

create index initiated_by
    on screen_cast_sessions (initiated_by);

