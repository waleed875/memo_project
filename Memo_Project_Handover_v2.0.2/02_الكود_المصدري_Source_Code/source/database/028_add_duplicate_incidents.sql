create table if not exists core.duplicate_incidents (
    id uuid primary key default gen_random_uuid(),
    classification varchar(50) not null,
    matched_note_id uuid not null references core.notes(id),
    created_note_id uuid references core.notes(id),
    company_id uuid not null references core.companies(id),
    factory_id uuid references core.factories(id),
    production_line_id uuid references core.production_lines(id),
    note_type_id uuid not null references core.note_types(id),
    note_number varchar(100) not null,
    note_number_normalized varchar(100) not null,
    writing_date date,
    decided_by uuid references auth.users(id),
    metadata jsonb,
    created_at timestamp not null default now(),
    constraint ck_duplicate_incident_classification check (classification in ('COMPANY_DUPLICATE_NUMBER', 'COMPANY_RESEND')),
    constraint ck_duplicate_incident_created_note check (
        (classification = 'COMPANY_DUPLICATE_NUMBER' and created_note_id is not null)
        or (classification = 'COMPANY_RESEND' and created_note_id is null)
    )
);
create index if not exists ix_duplicate_incidents_classification_created on core.duplicate_incidents(classification, created_at desc);
create index if not exists ix_duplicate_incidents_company_created on core.duplicate_incidents(company_id, created_at desc);
create index if not exists ix_duplicate_incidents_matched_note on core.duplicate_incidents(matched_note_id);
