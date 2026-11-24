-- Migration 057: durable idempotency keys for note creation.
-- The deferred note foreign key lets the operation row reserve its opaque key
-- before any note side effects are written in the same transaction.

create table if not exists core.note_create_operations (
    operation_id uuid primary key,
    note_id uuid not null,
    requested_by uuid not null references auth.users(id),
    fingerprint varchar(64) not null,
    created_at timestamp not null default now(),
    constraint ux_note_create_operations_note_id unique (note_id),
    constraint fk_note_create_operations_note
        foreign key (note_id) references core.notes(id)
        deferrable initially deferred,
    constraint ck_note_create_operations_fingerprint
        check (fingerprint ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_note_create_operations_requester_created
    on core.note_create_operations(requested_by, created_at desc);
