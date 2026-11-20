create table supplies
(
    supply_id int auto_increment
        primary key,
    name      varchar(100)                                                                       not null,
    category  varchar(50)                                                                        not null,
    unit      varchar(30)                                                                        not null,
    status    enum ('fully_stocked', 'in_stock', 'low_stock', 'out_of_stock') default 'in_stock' not null,
    remarks   varchar(255)                                                                       null
);

