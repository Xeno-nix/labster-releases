create table refresh_tokens
(
    id         char(36)                                 not null
        primary key,
    user_id    char(36)                                 not null,
    token_hash varchar(255)                             not null,
    created_at datetime(6) default CURRENT_TIMESTAMP(6) not null,
    expires_at datetime(6)                              not null,
    revoked_at datetime(6)                              null,
    constraint uq_refresh_tokens_token_hash
        unique (token_hash),
    constraint fk_refresh_tokens_user
        foreign key (user_id) references users (user_id)
            on delete cascade
);

create index idx_refresh_tokens_user_id
    on refresh_tokens (user_id);

