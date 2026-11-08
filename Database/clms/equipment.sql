create table equipment
(
    equipment_id      char(36)                                                                   not null
        primary key,
    equipment_no      varchar(50)                                                                not null,
    serial_number     varchar(100)                                                               null,
    equipment_name    varchar(150)                                                               not null,
    brand             varchar(100)                                                               null,
    model_number      varchar(100)                                                               null,
    equipment_type    varchar(50)                                                                not null,
    status            enum ('available', 'in_use', 'maintenance', 'damaged') default 'available' not null,
    needs_pm          tinyint(1)                                             default 0           not null,
    needs_calibration tinyint(1)                                             default 0           not null,
    remarks           varchar(255)                                                               null,
    deleted_at        datetime                                                                   null,
    constraint equipment_no
        unique (equipment_no)
);

