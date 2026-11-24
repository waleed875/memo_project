create table if not exists system.report_identity_settings (
    singleton boolean primary key default true check (singleton),
    ministry_name text not null default '',
    administrative_level_one text not null default '',
    administrative_level_two text not null default '',
    company_name text not null default '',
    issuing_entity_name text not null default '',
    default_city text not null default '',
    updated_at timestamp not null default now(),
    updated_by uuid references auth.users(id)
);

insert into system.report_identity_settings (singleton)
values (true)
on conflict (singleton) do nothing;
