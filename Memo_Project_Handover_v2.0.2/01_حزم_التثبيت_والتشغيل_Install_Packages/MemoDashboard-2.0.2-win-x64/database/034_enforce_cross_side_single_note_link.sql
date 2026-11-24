-- Enforce one link per note even when a note appears on different sides of rows.
-- The advisory locks make the cross-column check safe under concurrent inserts.
create or replace function core.validate_note_link()
returns trigger language plpgsql as $$
declare
    source_company uuid;
    related_company uuid;
    source_type varchar(50);
    related_type varchar(50);
    first_lock bigint;
    second_lock bigint;
begin
    first_lock := hashtextextended(least(new.source_note_id::text, new.related_note_id::text), 34026);
    second_lock := hashtextextended(greatest(new.source_note_id::text, new.related_note_id::text), 34026);
    perform pg_advisory_xact_lock(first_lock);
    if second_lock <> first_lock then
        perform pg_advisory_xact_lock(second_lock);
    end if;

    select n.company_id, nt.code into source_company, source_type
    from core.notes n
    join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.source_note_id and not n.is_deleted;

    select n.company_id, nt.code into related_company, related_type
    from core.notes n
    join core.note_types nt on nt.id = n.note_type_id
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

    if exists (
        select 1
        from core.note_links existing
        where existing.id <> new.id
          and (
                existing.source_note_id in (new.source_note_id, new.related_note_id)
             or existing.related_note_id in (new.source_note_id, new.related_note_id)
          )
    ) then
        raise exception using errcode = '23505', message = 'Each note can belong to only one link.';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_note_links_validate on core.note_links;
create trigger trg_note_links_validate
before insert or update of source_note_id, related_note_id on core.note_links
for each row execute function core.validate_note_link();
