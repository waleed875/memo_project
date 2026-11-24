alter table core.notes
    add column if not exists reviewer_id uuid references auth.users(id),
    add column if not exists review_started_at timestamp,
    add column if not exists return_reason text;

alter table core.notes
    drop constraint if exists ck_notes_status,
    add constraint ck_notes_status check (status in (
        'DRAFT', 'COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW',
        'UNDER_REVIEW', 'RETURNED_FOR_CORRECTION', 'APPROVED'
    ));

create table if not exists core.note_review_history (
    id uuid primary key default gen_random_uuid(),
    note_id uuid not null references core.notes(id) on delete cascade,
    action varchar(50) not null,
    reason text,
    user_id uuid references auth.users(id),
    created_at timestamp not null default now()
);

create index if not exists ix_notes_review_queue
    on core.notes(status, reviewer_id, arrival_at desc)
    where is_deleted = false;

create index if not exists ix_note_review_history_note
    on core.note_review_history(note_id, created_at desc);
