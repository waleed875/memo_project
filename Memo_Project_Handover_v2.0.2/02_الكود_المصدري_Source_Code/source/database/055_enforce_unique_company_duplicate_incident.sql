-- Migration 055: one active company-duplicate-number incident per created note.
-- Historical duplicates are preserved losslessly before the active rows are consolidated.

create table if not exists core.duplicate_incidents_archive (
    id uuid primary key,
    classification varchar(50) not null,
    matched_note_id uuid not null,
    created_note_id uuid,
    company_id uuid not null,
    factory_id uuid,
    production_line_id uuid,
    note_type_id uuid not null,
    note_number varchar(100) not null,
    note_number_normalized varchar(100) not null,
    writing_date date,
    decided_by uuid,
    metadata jsonb,
    created_at timestamp not null,
    duplicate_of uuid not null,
    archived_at timestamp not null default now(),
    reason text not null
);

lock table core.duplicate_incidents in share row exclusive mode;

with ranked as (
    select
        incidents.*,
        first_value(id) over (
            partition by created_note_id
            order by created_at, id
        ) as duplicate_of,
        row_number() over (
            partition by created_note_id
            order by created_at, id
        ) as occurrence
    from core.duplicate_incidents incidents
    where classification = 'COMPANY_DUPLICATE_NUMBER'
)
insert into core.duplicate_incidents_archive (
    id,
    classification,
    matched_note_id,
    created_note_id,
    company_id,
    factory_id,
    production_line_id,
    note_type_id,
    note_number,
    note_number_normalized,
    writing_date,
    decided_by,
    metadata,
    created_at,
    duplicate_of,
    archived_at,
    reason
)
select
    id,
    classification,
    matched_note_id,
    created_note_id,
    company_id,
    factory_id,
    production_line_id,
    note_type_id,
    note_number,
    note_number_normalized,
    writing_date,
    decided_by,
    metadata,
    created_at,
    duplicate_of,
    now(),
    'HISTORICAL_DUPLICATE_CREATED_NOTE_ID'
from ranked
where occurrence > 1
on conflict (id) do nothing;

with ranked as (
    select
        id,
        row_number() over (
            partition by created_note_id
            order by created_at, id
        ) as occurrence
    from core.duplicate_incidents
    where classification = 'COMPANY_DUPLICATE_NUMBER'
)
delete from core.duplicate_incidents incidents
using ranked
where incidents.id = ranked.id
  and ranked.occurrence > 1;

create unique index if not exists ux_duplicate_incidents_company_duplicate_created_note
    on core.duplicate_incidents(created_note_id)
    where classification = 'COMPANY_DUPLICATE_NUMBER';
