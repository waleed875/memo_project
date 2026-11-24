create table if not exists core.factories (
    id uuid primary key default gen_random_uuid(),
    name varchar(200) not null,
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create unique index if not exists ux_factories_name_active
    on core.factories(lower(name)) where is_deleted = false;

alter table core.notes
    add column if not exists factory_id uuid references core.factories(id);

create index if not exists ix_notes_factory_id on core.notes(factory_id) where is_deleted = false;
