-- Migration 068: Add multi-attachment support for notes
create table if not exists core.note_attachments (
    id uuid primary key default gen_random_uuid(),
    note_id uuid not null references core.notes(id) on delete cascade,
    file_name text not null,
    content_type varchar(120) not null,
    file_data bytea not null,
    file_size bigint not null default 0,
    created_at timestamptz not null default clock_timestamp(),
    created_by uuid references auth.users(id)
);

create index if not exists idx_note_attachments_note_id on core.note_attachments(note_id);

-- Backfill from existing core.notes attachments
insert into core.note_attachments (note_id, file_name, content_type, file_data, file_size, created_at, created_by)
select
    id,
    coalesce(attachment_file_name, 'attachment'),
    coalesce(attachment_content_type, 'application/octet-stream'),
    attachment_data,
    coalesce(octet_length(attachment_data), 0),
    coalesce(updated_at, created_at, clock_timestamp()),
    updated_by
from core.notes
where attachment_data is not null
  and not exists (
      select 1 from core.note_attachments na where na.note_id = core.notes.id
  );
