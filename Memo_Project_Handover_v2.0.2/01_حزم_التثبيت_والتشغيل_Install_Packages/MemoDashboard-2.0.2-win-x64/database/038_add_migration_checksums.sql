alter table system.schema_migrations
    add column if not exists checksum varchar(64);
