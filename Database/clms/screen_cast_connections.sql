create table screen_cast_connections
(
    screen_cast_connection_id int auto_increment
        primary key,
    cast_session_id           int                                not null,
    computer_unit_id          char(36)                           not null,
    status                    enum ('connected', 'disconnected') not null,
    started_at                datetime                           null,
    ended_at                  datetime                           null,
    constraint cast_session_id
        unique (cast_session_id, computer_unit_id),
    constraint screen_cast_connections_ibfk_1
        foreign key (cast_session_id) references screen_cast_sessions (cast_session_id),
    constraint screen_cast_connections_ibfk_2
        foreign key (computer_unit_id) references computer_units (computer_unit_id)
);

create index computer_unit_id
    on screen_cast_connections (computer_unit_id);

