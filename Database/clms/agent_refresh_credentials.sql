create table agent_refresh_credentials
(
    credential_id         char(36)                   not null
        primary key,
    computer_unit_id      char(36)                   not null,
    credential_generation int                        not null,
    family_id             char(36)                   not null,
    token_hash            char(44) collate ascii_bin not null,
    created_at            datetime(6)                not null,
    expires_at            datetime(6)                not null,
    revoked_at            datetime(6)                null,
    replaced_by_id        char(36)                   null,
    constraint uq_agent_refresh_hash
        unique (token_hash),
    constraint fk_agent_refresh_successor
        foreign key (replaced_by_id) references agent_refresh_credentials (credential_id),
    constraint fk_agent_refresh_unit
        foreign key (computer_unit_id) references computer_units (computer_unit_id)
);

create index ix_agent_refresh_family
    on agent_refresh_credentials (family_id, revoked_at, expires_at);

create index ix_agent_refresh_unit
    on agent_refresh_credentials (computer_unit_id, revoked_at);

