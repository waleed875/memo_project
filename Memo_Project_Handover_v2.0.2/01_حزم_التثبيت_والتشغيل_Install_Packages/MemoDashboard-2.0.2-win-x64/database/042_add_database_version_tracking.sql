create table if not exists system.database_settings (
    singleton boolean primary key default true check (singleton),
    database_version varchar(30) not null,
    last_updated_at timestamp not null default now(),
    last_updated_by uuid references auth.users(id)
);

create table if not exists system.database_migration_history (
    migration_version varchar(30) primary key,
    previous_version varchar(30),
    new_version varchar(30) not null,
    started_at timestamp not null,
    completed_at timestamp,
    status varchar(20) not null,
    description text not null,
    error_details text,
    constraint ck_database_migration_history_status
        check (status in ('SUCCESS', 'FAILED', 'RUNNING'))
);

insert into system.database_settings (singleton, database_version, last_updated_at, last_updated_by)
values (true, '042', now(), null)
on conflict (singleton) do update
set database_version = excluded.database_version,
    last_updated_at = excluded.last_updated_at,
    last_updated_by = excluded.last_updated_by;

insert into system.database_migration_history (
    migration_version, previous_version, new_version, started_at, completed_at, status, description, error_details)
values (
    '042', '041', '042', now(), now(), 'SUCCESS',
    'تهيئة سجل إصدار قاعدة البيانات وسجل تاريخ الترقيات.', null)
on conflict (migration_version) do nothing;
