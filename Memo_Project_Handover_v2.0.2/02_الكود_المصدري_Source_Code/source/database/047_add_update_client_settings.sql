-- Release 1.0.3: desktop update-client configuration shared by all desktop installations.
insert into system.app_settings(key, value) values
    ('UPDATE_SERVICE_URL', null),
    ('UPDATE_PUBLIC_KEY', null)
on conflict (key) do nothing;

update system.database_settings
set compatible_application_version = '1.0.3', last_updated_at = now()
where singleton = true;
