do $$
begin
    if exists (
        select 1 from (
            select source_note_id as note_id from core.note_links
            union all select related_note_id from core.note_links
        ) x group by note_id having count(*) > 1
    ) then
        raise exception 'Cannot enforce single note links: existing notes have more than one link.';
    end if;
end $$;

create unique index if not exists ux_note_links_source_single on core.note_links(source_note_id);
create unique index if not exists ux_note_links_related_single on core.note_links(related_note_id);

create or replace function core.validate_note_link()
returns trigger language plpgsql as $$
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
        raise exception 'Both notes in a link must exist and be active.';
    end if;
    if source_company <> related_company then
        raise exception 'Linked notes must belong to the same company.';
    end if;
    if not ((source_type = 'ENTRY' and related_type = 'EXIT') or (source_type = 'EXIT' and related_type = 'ENTRY')) then
        raise exception 'A link is allowed only between ENTRY and EXIT notes.';
    end if;
    return new;
end;
$$;
drop trigger if exists trg_note_links_validate on core.note_links;
create trigger trg_note_links_validate
before insert or update of source_note_id, related_note_id on core.note_links
for each row execute function core.validate_note_link();
