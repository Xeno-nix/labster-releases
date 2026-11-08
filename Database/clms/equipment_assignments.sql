create table equipment_assignments
(
    assignment_id    char(36)                                                          not null
        primary key,
    equipment_id     char(36)                                                          not null,
    computer_unit_id char(36)                                                          null,
    laboratory_id    int                                                               null,
    status           enum ('active', 'ended') default 'active'                         not null,
    end_reason       enum ('broken', 'replaced', 'moved', 'returned', 'lost', 'other') null,
    remarks          varchar(255)                                                      null,
    assigned_at      datetime                 default CURRENT_TIMESTAMP                not null,
    ended_at         datetime                                                          null,
    constraint equipment_assignments_ibfk_1
        foreign key (equipment_id) references equipment (equipment_id),
    constraint equipment_assignments_ibfk_2
        foreign key (computer_unit_id) references computer_units (computer_unit_id),
    constraint equipment_assignments_ibfk_3
        foreign key (laboratory_id) references laboratories (lab_id),
    constraint chk_assignment_target
        check (((`computer_unit_id` is not null) and (`laboratory_id` is null)) or
               ((`computer_unit_id` is null) and (`laboratory_id` is not null)))
);

create index computer_unit_id
    on equipment_assignments (computer_unit_id);

create index equipment_id
    on equipment_assignments (equipment_id);

create index laboratory_id
    on equipment_assignments (laboratory_id);

