create table if not exists core.production_lines (
    id uuid primary key default gen_random_uuid(),
    factory_id uuid not null references core.factories(id),
    name varchar(200) not null,
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create unique index if not exists ux_production_lines_factory_name_active
    on core.production_lines(factory_id, lower(name)) where is_deleted = false;
create index if not exists ix_production_lines_factory_id
    on core.production_lines(factory_id) where is_deleted = false;

alter table core.notes
    add column if not exists production_line_id uuid references core.production_lines(id);
create index if not exists ix_notes_production_line_id
    on core.notes(production_line_id) where is_deleted = false;

create or replace function core.validate_note_factory_production_line()
returns trigger language plpgsql as $$
begin
    if new.production_line_id is not null and new.factory_id is null then
        raise exception 'A production line requires a factory.';
    end if;
    if new.production_line_id is not null and not exists (
        select 1 from core.production_lines pl
        where pl.id = new.production_line_id
          and pl.factory_id = new.factory_id
          and pl.is_active and not pl.is_deleted
    ) then
        raise exception 'The production line must be active and belong to the selected factory.';
    end if;
    return new;
end;
$$;

drop trigger if exists trg_notes_validate_factory_production_line on core.notes;
create trigger trg_notes_validate_factory_production_line
before insert or update of factory_id, production_line_id on core.notes
for each row execute function core.validate_note_factory_production_line();

create or replace function core.protect_used_production_line()
returns trigger language plpgsql as $$
begin
    if exists (select 1 from core.notes n where n.production_line_id = old.id and not n.is_deleted)
       and (new.factory_id is distinct from old.factory_id or (new.is_deleted and not old.is_deleted)) then
        raise exception 'A production line used by notes cannot be moved or deleted.';
    end if;
    return new;
end;
$$;
drop trigger if exists trg_production_lines_protect_used on core.production_lines;
create trigger trg_production_lines_protect_used
before update of factory_id, is_deleted on core.production_lines
for each row execute function core.protect_used_production_line();

create or replace function core.protect_factory_with_production_lines()
returns trigger language plpgsql as $$
begin
    if exists (select 1 from core.production_lines pl where pl.factory_id = old.id and not pl.is_deleted)
       and (new.company_id is distinct from old.company_id or (new.is_deleted and not old.is_deleted)) then
        raise exception 'A factory with production lines cannot be moved or deleted.';
    end if;
    return new;
end;
$$;
drop trigger if exists trg_factories_protect_production_lines on core.factories;
create trigger trg_factories_protect_production_lines
before update of company_id, is_deleted on core.factories
for each row execute function core.protect_factory_with_production_lines();
