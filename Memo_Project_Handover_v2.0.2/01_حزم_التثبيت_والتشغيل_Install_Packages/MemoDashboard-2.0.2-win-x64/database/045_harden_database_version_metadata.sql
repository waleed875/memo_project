alter table system.database_settings
    add column if not exists compatible_application_version varchar(30);

insert into system.database_settings (
    singleton,
    database_version,
    last_updated_at,
    last_updated_by,
    compatible_application_version)
select
    true,
    coalesce((
        select version
        from system.schema_migrations
        where version ~ '^[0-9]+$'
        order by version::numeric desc
        limit 1
    ), '044'),
    now(),
    null,
    '1.0.1'
on conflict (singleton) do update
set compatible_application_version = excluded.compatible_application_version,
    last_updated_at = excluded.last_updated_at;

create or replace view system.database_version_status as
select
    coalesce((
        select version
        from system.schema_migrations
        where version ~ '^[0-9]+$'
        order by version::numeric desc
        limit 1
    ), settings.database_version, '000') as database_version,
    settings.compatible_application_version,
    settings.last_updated_at,
    settings.last_updated_by
from system.database_settings settings
where settings.singleton = true;

comment on view system.database_version_status is
    'Single read-only source for the installed database migration and compatible application release.';
