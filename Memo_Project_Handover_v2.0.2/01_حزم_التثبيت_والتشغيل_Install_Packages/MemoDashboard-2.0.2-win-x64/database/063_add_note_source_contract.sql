-- Migration 063: extensible entry-note sources and multi-reference links.
-- Existing core.note_links rows are retained and copied into the compatibility model.

create table if not exists core.note_sources (
    id uuid primary key default gen_random_uuid(),
    note_id uuid not null references core.notes(id) on delete cascade,
    source_type varchar(20) not null,
    other_description text,
    created_by uuid references auth.users(id),
    created_at timestamp not null default now(),
    updated_at timestamp not null default now(),
    constraint ux_note_sources_note unique (note_id),
    constraint ck_note_sources_type check (source_type in ('EXIT', 'INVOICE', 'LOOT', 'OTHER')),
    constraint ck_note_sources_other_description check (source_type <> 'OTHER' or nullif(btrim(other_description), '') is not null)
);

create table if not exists core.note_source_exit_refs (
    id uuid primary key default gen_random_uuid(),
    source_id uuid not null references core.note_sources(id) on delete cascade,
    exit_note_id uuid not null references core.notes(id) on delete restrict,
    reference_order integer not null default 1,
    created_at timestamp not null default now(),
    constraint ux_note_source_exit_ref unique (source_id, exit_note_id),
    constraint ck_note_source_exit_order check (reference_order > 0)
);

create index if not exists ix_note_source_exit_refs_exit_note on core.note_source_exit_refs(exit_note_id);
create index if not exists ix_note_sources_type on core.note_sources(source_type);

create table if not exists core.note_source_invoices (
    id uuid primary key default gen_random_uuid(),
    source_id uuid not null references core.note_sources(id) on delete cascade,
    invoice_number varchar(120) not null,
    invoice_date date,
    destination text,
    attachment_file_name varchar(255),
    attachment_content_type varchar(120),
    attachment_data bytea,
    created_at timestamp not null default now(),
    constraint ck_note_source_invoice_number check (nullif(btrim(invoice_number), '') is not null)
);

create index if not exists ix_note_source_invoices_source on core.note_source_invoices(source_id, invoice_date, invoice_number);

create table if not exists core.note_source_loot (
    source_id uuid primary key references core.note_sources(id) on delete cascade,
    description text not null,
    created_at timestamp not null default now(),
    constraint ck_note_source_loot_description check (nullif(btrim(description), '') is not null)
);

create or replace function core.validate_note_source()
returns trigger language plpgsql as $function$
declare
    note_company uuid;
    note_type text;
begin
    select n.company_id, nt.code into note_company, note_type
    from core.notes n
    join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.note_id and not n.is_deleted;
    if note_company is null or note_type <> 'ENTRY' then
        raise exception using errcode = '23514', message = 'A source can only be assigned to an active ENTRY note.';
    end if;
    return new;
end;
$function$;

drop trigger if exists trg_note_sources_validate on core.note_sources;
create trigger trg_note_sources_validate
before insert or update of note_id, source_type, other_description on core.note_sources
for each row execute function core.validate_note_source();

create or replace function core.validate_note_source_exit_ref()
returns trigger language plpgsql as $function$
declare
    source_note_id uuid;
    source_company uuid;
    source_type text;
    exit_company uuid;
    exit_type text;
begin
    select s.note_id, entry.company_id, s.source_type
      into source_note_id, source_company, source_type
    from core.note_sources s
    join core.notes entry on entry.id = s.note_id and not entry.is_deleted
    where s.id = new.source_id;
    select n.company_id, nt.code into exit_company, exit_type
    from core.notes n
    join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.exit_note_id and not n.is_deleted;
    if source_note_id is null or source_type <> 'EXIT' or exit_company is null or exit_type <> 'EXIT' then
        raise exception using errcode = '23514', message = 'Exit references require an EXIT source and an active EXIT note.';
    end if;
    if source_company <> exit_company then
        raise exception using errcode = '23514', message = 'Linked source notes must belong to the same company.';
    end if;
    if source_note_id = new.exit_note_id then
        raise exception using errcode = '23514', message = 'An ENTRY note cannot reference itself.';
    end if;
    return new;
end;
$function$;

drop trigger if exists trg_note_source_exit_refs_validate on core.note_source_exit_refs;
create trigger trg_note_source_exit_refs_validate
before insert or update of source_id, exit_note_id on core.note_source_exit_refs
for each row execute function core.validate_note_source_exit_ref();

create or replace function core.validate_note_source_payload()
returns trigger language plpgsql as $function$
declare
    expected_type text;
    actual_type text;
begin
    select source_type into actual_type from core.note_sources where id = new.source_id;
    expected_type := case tg_table_name when 'note_source_invoices' then 'INVOICE' else 'LOOT' end;
    if actual_type is distinct from expected_type then
        raise exception using errcode = '23514', message = 'The source payload does not match the note source type.';
    end if;
    return new;
end;
$function$;

drop trigger if exists trg_note_source_invoices_validate on core.note_source_invoices;
create trigger trg_note_source_invoices_validate
before insert or update of source_id on core.note_source_invoices
for each row execute function core.validate_note_source_payload();

drop trigger if exists trg_note_source_loot_validate on core.note_source_loot;
create trigger trg_note_source_loot_validate
before insert or update of source_id on core.note_source_loot
for each row execute function core.validate_note_source_payload();

-- Copy every legacy pair into the new one-source/many-references shape.
with legacy_pairs as (
    select
        case when source_type.code = 'ENTRY' then l.source_note_id else l.related_note_id end as entry_note_id,
        case when source_type.code = 'ENTRY' then l.related_note_id else l.source_note_id end as exit_note_id,
        l.created_by,
        l.created_at
    from core.note_links l
    join core.notes source_note on source_note.id = l.source_note_id
    join core.note_types source_type on source_type.id = source_note.note_type_id
    join core.notes related_note on related_note.id = l.related_note_id
    join core.note_types related_type on related_type.id = related_note.note_type_id
    where (source_type.code = 'ENTRY' and related_type.code = 'EXIT')
       or (source_type.code = 'EXIT' and related_type.code = 'ENTRY')
)
insert into core.note_sources(note_id, source_type, created_by, created_at, updated_at)
select distinct on (entry_note_id) entry_note_id, 'EXIT', created_by, created_at, created_at
from legacy_pairs
order by entry_note_id, created_at
on conflict (note_id) do nothing;

with legacy_pairs as (
    select
        case when source_type.code = 'ENTRY' then l.source_note_id else l.related_note_id end as entry_note_id,
        case when source_type.code = 'ENTRY' then l.related_note_id else l.source_note_id end as exit_note_id,
        l.created_at
    from core.note_links l
    join core.notes source_note on source_note.id = l.source_note_id
    join core.note_types source_type on source_type.id = source_note.note_type_id
    join core.notes related_note on related_note.id = l.related_note_id
    join core.note_types related_type on related_type.id = related_note.note_type_id
    where (source_type.code = 'ENTRY' and related_type.code = 'EXIT')
       or (source_type.code = 'EXIT' and related_type.code = 'ENTRY')
), numbered as (
    select lp.*, row_number() over (partition by lp.entry_note_id order by lp.created_at, lp.exit_note_id) as reference_order
    from legacy_pairs lp
)
insert into core.note_source_exit_refs(source_id, exit_note_id, reference_order, created_at)
select s.id, n.exit_note_id, n.reference_order::integer, n.created_at
from numbered n
join core.note_sources s on s.note_id = n.entry_note_id and s.source_type = 'EXIT'
on conflict (source_id, exit_note_id) do nothing;

-- Keep old consumers alive while allowing the new model to carry more than one
-- EXIT reference. Pairs copied from note_links are emitted once.
create or replace view core.v_note_links_compat as
select l.source_note_id, l.related_note_id, l.created_by, l.created_at
from core.note_links l
where not exists (
    select 1
    from core.note_sources s
    join core.note_source_exit_refs r on r.source_id = s.id
    where s.note_id = case
        when exists (select 1 from core.note_types t join core.notes n on n.note_type_id = t.id where n.id = l.source_note_id and t.code = 'ENTRY') then l.source_note_id
        else l.related_note_id end
      and r.exit_note_id = case
        when exists (select 1 from core.note_types t join core.notes n on n.note_type_id = t.id where n.id = l.source_note_id and t.code = 'ENTRY') then l.related_note_id
        else l.source_note_id end
)
union all
select s.note_id, r.exit_note_id, s.created_by, r.created_at
from core.note_sources s
join core.note_source_exit_refs r on r.source_id = s.id;

-- The legacy table remains a compatibility write surface, but its historical
-- one-link indexes cannot remain if the compatibility surface is to accept
-- additional references during the transition.
drop index if exists core.ux_note_links_source_single;
drop index if exists core.ux_note_links_related_single;

create or replace function core.validate_note_link()
returns trigger language plpgsql as $function$
declare
    source_company uuid;
    related_company uuid;
    source_type varchar(50);
    related_type varchar(50);
begin
    select n.company_id, nt.code into source_company, source_type
    from core.notes n join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.source_note_id and not n.is_deleted;
    select n.company_id, nt.code into related_company, related_type
    from core.notes n join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.related_note_id and not n.is_deleted;
    if source_company is null or related_company is null then
        raise exception using errcode = '23503', message = 'Both notes in a link must exist and be active.';
    end if;
    if source_company <> related_company then
        raise exception using errcode = '23514', message = 'Linked notes must belong to the same company.';
    end if;
    if not ((source_type = 'ENTRY' and related_type = 'EXIT')
         or (source_type = 'EXIT' and related_type = 'ENTRY')) then
        raise exception using errcode = '23514', message = 'A link is allowed only between ENTRY and EXIT notes.';
    end if;
    return new;
end;
$function$;

-- Keep the old link API and the new source API bidirectionally compatible.
-- Existing application paths that still write core.note_links therefore also
-- populate the extensible source model, while new source references remain
-- visible to legacy readers.
create or replace function core.sync_legacy_note_link_to_source()
returns trigger language plpgsql as $function$
declare
    v_entry_note_id uuid;
    v_exit_note_id uuid;
    v_entry_type text;
    v_exit_type text;
    v_source_id uuid;
    v_source_type text;
begin
    if tg_op = 'DELETE' then
        select case when source_type.code = 'ENTRY' then old.source_note_id else old.related_note_id end,
               case when source_type.code = 'ENTRY' then old.related_note_id else old.source_note_id end,
               source_type.code,
               related_type.code
          into v_entry_note_id, v_exit_note_id, v_entry_type, v_exit_type
        from core.notes source_note
        join core.note_types source_type on source_type.id = source_note.note_type_id
        join core.notes related_note on related_note.id = old.related_note_id
        join core.note_types related_type on related_type.id = related_note.note_type_id
        where source_note.id = old.source_note_id;
        if (v_entry_type = 'ENTRY' and v_exit_type = 'EXIT')
           or (v_entry_type = 'EXIT' and v_exit_type = 'ENTRY') then
            delete from core.note_source_exit_refs r
            using core.note_sources s
            where s.id = r.source_id
              and s.note_id = v_entry_note_id
              and r.exit_note_id = v_exit_note_id;
        end if;
        return old;
    end if;

    select case when source_type.code = 'ENTRY' then new.source_note_id else new.related_note_id end,
           case when source_type.code = 'ENTRY' then new.related_note_id else new.source_note_id end,
           source_type.code,
           related_type.code
      into v_entry_note_id, v_exit_note_id, v_entry_type, v_exit_type
    from core.notes source_note
    join core.note_types source_type on source_type.id = source_note.note_type_id
    join core.notes related_note on related_note.id = new.related_note_id
    join core.note_types related_type on related_type.id = related_note.note_type_id
    where source_note.id = new.source_note_id;
    if (v_entry_type = 'ENTRY' and v_exit_type = 'EXIT')
       or (v_entry_type = 'EXIT' and v_exit_type = 'ENTRY') then
        insert into core.note_sources(note_id, source_type, created_by)
        values (v_entry_note_id, 'EXIT', new.created_by)
        on conflict (note_id) do nothing;
        select s.id, s.source_type into v_source_id, v_source_type
        from core.note_sources s where s.note_id = v_entry_note_id;
        if v_source_type <> 'EXIT' then
            raise exception using errcode = '23514', message = 'An ENTRY note can have only one source type.';
        end if;
        insert into core.note_source_exit_refs(source_id, exit_note_id, reference_order)
        values (
            v_source_id,
            v_exit_note_id,
            coalesce((select max(r.reference_order) + 1 from core.note_source_exit_refs r where r.source_id = v_source_id), 1))
        on conflict (source_id, exit_note_id) do nothing;
    end if;
    return new;
end;
$function$;

create or replace function core.sync_source_ref_to_legacy_note_link()
returns trigger language plpgsql as $function$
declare
    entry_note_id uuid;
begin
    select s.note_id into entry_note_id
    from core.note_sources s where s.id = coalesce(new.source_id, old.source_id);
    if tg_op = 'INSERT' then
        insert into core.note_links(source_note_id, related_note_id, created_by)
        select new_source.note_id, new.exit_note_id, new_source.created_by
        from core.note_sources new_source
        where new_source.id = new.source_id
          and not exists (
              select 1 from core.note_links l
              where (l.source_note_id = new_source.note_id and l.related_note_id = new.exit_note_id)
                 or (l.source_note_id = new.exit_note_id and l.related_note_id = new_source.note_id));
        return new;
    end if;
    delete from core.note_links
    where (source_note_id = entry_note_id and related_note_id = old.exit_note_id)
       or (source_note_id = old.exit_note_id and related_note_id = entry_note_id);
    return old;
end;
$function$;

drop trigger if exists trg_legacy_note_link_to_source on core.note_links;
create trigger trg_legacy_note_link_to_source
after insert or delete on core.note_links
for each row execute function core.sync_legacy_note_link_to_source();

drop trigger if exists trg_source_ref_to_legacy_note_link on core.note_source_exit_refs;
create trigger trg_source_ref_to_legacy_note_link
after insert or delete on core.note_source_exit_refs
for each row execute function core.sync_source_ref_to_legacy_note_link();

comment on table core.note_sources is
    'Exclusive source type for an ENTRY note: EXIT references, invoices, loot, or other.';
comment on view core.v_note_links_compat is
    'Compatibility projection of legacy note_links and the extensible note source model.';

-- Reports must see both historical links and additional references created by
-- the extensible source model. Recreate reporting functions from their current
-- definitions so this remains compatible with installations that received
-- 062 before 063.
do $function$
declare
    function_row record;
    definition text;
begin
    for function_row in
        select p.oid
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'reporting'
          and p.proname in (
              'fn_company_metrics_snapshot_v3',
              'fn_company_metrics_snapshot_v4',
              'fn_company_metrics_snapshot_v4_filtered',
              'fn_company_month_series_v3',
              'fn_company_month_series_v4')
    loop
        definition := pg_get_functiondef(function_row.oid);
        if position('core.note_links' in definition) > 0 then
            execute replace(definition, 'core.note_links', 'core.v_note_links_compat');
        end if;
    end loop;
end;
$function$;

insert into system.database_settings(singleton, database_version, compatible_application_version, last_updated_at)
values (true, '063', '1.0.6', now())
on conflict (singleton) do update set
    database_version = excluded.database_version,
    compatible_application_version = excluded.compatible_application_version,
    last_updated_at = excluded.last_updated_at;
