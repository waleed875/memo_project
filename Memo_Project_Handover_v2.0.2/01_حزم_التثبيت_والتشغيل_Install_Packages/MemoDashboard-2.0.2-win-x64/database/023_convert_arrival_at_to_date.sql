-- Drop dependent contracts before changing the physical type. Migration 030
-- recreates the complete reporting contract after all new columns exist.
drop view if exists reporting.vw_note_details cascade;
drop view if exists reporting.vw_note_field_details cascade;
drop view if exists reporting.vw_company_error_summary cascade;
drop view if exists reporting.vw_field_error_summary cascade;
drop view if exists reporting.vw_daily_notes_summary cascade;
drop view if exists reporting.vw_note_type_summary cascade;

alter table core.notes
    alter column arrival_at drop default,
    alter column arrival_at type date using arrival_at::date,
    alter column arrival_at set default current_date;

create index if not exists ix_notes_arrival_at_active
    on core.notes(arrival_at desc) where is_deleted = false;
