alter table core.notes
    add column if not exists note_number_normalized varchar(100),
    add column if not exists duplicate_reason text;

update core.notes
set note_number_normalized = upper(regexp_replace(note_number, '[^[:alnum:]]', '', 'g'))
where note_number_normalized is null;

alter table core.notes
    alter column note_number_normalized set not null;

drop index if exists core.ux_notes_business_key_active;

create index if not exists ix_notes_duplicate_lookup
    on core.notes(company_id, note_type_id, note_number_normalized)
    where is_deleted = false;

create table if not exists core.note_links (
    id uuid primary key default gen_random_uuid(),
    source_note_id uuid not null references core.notes(id) on delete cascade,
    related_note_id uuid not null references core.notes(id) on delete cascade,
    created_by uuid references auth.users(id),
    created_at timestamp not null default now(),
    constraint ck_note_links_not_self check (source_note_id <> related_note_id)
);

create unique index if not exists ux_note_links_pair
    on core.note_links(least(source_note_id, related_note_id), greatest(source_note_id, related_note_id));

create index if not exists ix_note_links_source on core.note_links(source_note_id);
create index if not exists ix_note_links_related on core.note_links(related_note_id);
