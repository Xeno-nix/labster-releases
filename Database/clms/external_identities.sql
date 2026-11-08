create table external_identities
(
    id           char(36)     not null
        primary key,
    user_id      char(36)     null,
    provider     varchar(50)  not null,
    provider_sub varchar(255) not null,
    constraint provider_sub
        unique (provider_sub),
    constraint uq_external_provider_subject
        unique (provider, provider_sub),
    constraint uq_external_provider_user
        unique (provider, user_id),
    constraint fk_external_identities_users
        foreign key (user_id) references users (user_id)
            on delete cascade
);

create index ix_external_identities_user_id
    on external_identities (user_id);

