CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

create table if not exists transaction_pix
(
    id          uuid default uuid_generate_v4() not null
        constraint transaction_pix_pk
            primary key,
    name        varchar(100)                    not null,
    external_id varchar(100)                    not null,
    value       integer                         not null,
    qr_code     varchar(255)
);

alter table transaction_pix
    owner to root;