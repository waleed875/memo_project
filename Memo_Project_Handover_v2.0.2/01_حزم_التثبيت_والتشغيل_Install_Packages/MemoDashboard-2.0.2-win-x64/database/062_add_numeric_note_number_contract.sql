-- Migration 062: preserve the display number while adding a safe numeric/year key.
-- Existing non-numeric or incomplete rows remain nullable for owner review.

alter table core.notes
    add column if not exists note_number_value bigint,
    add column if not exists note_year integer;

create or replace function common.try_note_number_value(input text)
returns bigint
language plpgsql
immutable
as $function$
declare
    normalized text;
    parsed numeric;
begin
    if input is null or btrim(input) = '' then
        return null;
    end if;

    normalized := translate(
        btrim(input),
        '٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹',
        '01234567890123456789');
    if normalized !~ '^[0-9]+$' then
        return null;
    end if;

    parsed := normalized::numeric;
    if parsed <= 0 or parsed > 9223372036854775807 then
        return null;
    end if;
    return parsed::bigint;
exception
    when numeric_value_out_of_range or invalid_text_representation then
        return null;
end;
$function$;

update core.notes
set note_number_value = common.try_note_number_value(note_number),
    note_year = extract(year from writing_date)::integer
where note_number_value is distinct from common.try_note_number_value(note_number)
   or note_year is distinct from extract(year from writing_date)::integer;

alter table core.notes
    drop constraint if exists ck_notes_note_number_value_positive,
    add constraint ck_notes_note_number_value_positive
        check (note_number_value is null or note_number_value > 0) not valid;

create index if not exists ix_notes_duplicate_numeric_key
    on core.notes(company_id, note_type_id, note_year, note_number_value)
    where not is_deleted and note_number_value is not null and note_year is not null;

create or replace function reporting.fn_note_number_gaps(
    p_company_id uuid,
    p_note_type_id uuid,
    p_note_year integer)
returns table(note_number_value bigint)
language sql
stable
as $function$
with bounds as (
    select min(n.note_number_value) as first_value,
           max(n.note_number_value) as last_value
    from core.notes n
    where not n.is_deleted
      and n.company_id = p_company_id
      and n.note_type_id = p_note_type_id
      and n.note_year = p_note_year
      and n.note_number_value is not null
), expected as (
    select generate_series(first_value, last_value)::bigint as note_number_value
    from bounds
    where first_value is not null and last_value is not null and last_value >= first_value
)
select e.note_number_value
from expected e
left join core.notes n
  on n.company_id = p_company_id
 and n.note_type_id = p_note_type_id
 and n.note_year = p_note_year
 and n.note_number_value = e.note_number_value
 and not n.is_deleted
group by e.note_number_value
having count(n.id) = 0
order by e.note_number_value;
$function$;

comment on column core.notes.note_number_value is
    'Positive normalized integer for sequencing and duplicate matching; null means legacy data needs review.';
comment on column core.notes.note_year is
    'Calendar year derived only from writing_date; null when writing_date is missing.';

-- L4 reporting contract. The v3 functions remain available for compatibility;
-- v4 adds an explicit date basis and applies the cutoff to arrival_at only.
-- The definitions are derived from the already-published v3 bodies so their
-- metric columns and denominator rules cannot drift between versions.
do $migration$
declare
    definition text;
begin
    select pg_get_functiondef('reporting.fn_company_metrics_snapshot_v3(uuid,date,date,date)'::regprocedure)
      into definition;
    definition := replace(definition,
        'reporting.fn_company_metrics_snapshot_v3(',
        'reporting.fn_company_metrics_snapshot_v4(');
    definition := replace(definition,
        'p_entry_cutoff_date date default null',
        'p_entry_cutoff_date date default null, p_date_basis text default ''WritingDate''');
    definition := replace(definition,
        'p_entry_cutoff_date date DEFAULT NULL::date',
        'p_entry_cutoff_date date DEFAULT NULL::date, p_date_basis text DEFAULT ''WritingDate''');
    definition := replace(definition,
        'n.arrival_at >= p_from',
        'case when p_date_basis = ''WritingDate'' then n.writing_date else n.arrival_at::date end >= p_from');
    definition := replace(definition,
        'n.arrival_at < (p_to + 1)::timestamp',
        'case when p_date_basis = ''WritingDate'' then n.writing_date else n.arrival_at::date end <= p_to');
    definition := replace(definition,
        'n.created_at < (p_entry_cutoff_date + 1)::timestamp',
        'n.arrival_at::date <= p_entry_cutoff_date');
    definition := replace(definition,
        'counterpart.created_at < (p_entry_cutoff_date + 1)::timestamp',
        'counterpart.arrival_at::date <= p_entry_cutoff_date');
    execute definition;

    select pg_get_functiondef('reporting.fn_company_metrics_snapshot_v3_filtered(uuid,date,date,uuid,uuid,uuid,text,date)'::regprocedure)
      into definition;
    definition := replace(definition,
        'reporting.fn_company_metrics_snapshot_v3_filtered(',
        'reporting.fn_company_metrics_snapshot_v4_filtered(');
    definition := replace(definition,
        'p_entry_cutoff_date date default null',
        'p_entry_cutoff_date date default null, p_date_basis text default ''WritingDate''');
    definition := replace(definition,
        'p_entry_cutoff_date date DEFAULT NULL::date',
        'p_entry_cutoff_date date DEFAULT NULL::date, p_date_basis text DEFAULT ''WritingDate''');
    definition := replace(definition,
        'n.arrival_at >= p_from',
        'case when p_date_basis = ''WritingDate'' then n.writing_date else n.arrival_at::date end >= p_from');
    definition := replace(definition,
        'n.arrival_at < (p_to + 1)::timestamp',
        'case when p_date_basis = ''WritingDate'' then n.writing_date else n.arrival_at::date end <= p_to');
    definition := replace(definition,
        'n.created_at < (p_entry_cutoff_date + 1)::timestamp',
        'n.arrival_at::date <= p_entry_cutoff_date');
    definition := replace(definition,
        'counterpart.created_at < (p_entry_cutoff_date + 1)::timestamp',
        'counterpart.arrival_at::date <= p_entry_cutoff_date');
    execute definition;

    select pg_get_functiondef('reporting.fn_company_month_series_v3(uuid,date,date,date)'::regprocedure)
      into definition;
    definition := replace(definition,
        'reporting.fn_company_month_series_v3(',
        'reporting.fn_company_month_series_v4(');
    definition := replace(definition,
        'p_entry_cutoff_date date default null',
        'p_entry_cutoff_date date default null, p_date_basis text default ''WritingDate''');
    definition := replace(definition,
        'p_entry_cutoff_date date DEFAULT NULL::date',
        'p_entry_cutoff_date date DEFAULT NULL::date, p_date_basis text DEFAULT ''WritingDate''');
    definition := replace(definition,
        'reporting.fn_company_metrics_snapshot_v3(',
        'reporting.fn_company_metrics_snapshot_v4(');
    execute definition;
end;
$migration$;

comment on function reporting.fn_company_metrics_snapshot_v4(uuid,date,date,date,text) is
    'Canonical company analytics with explicit WritingDate or ArrivalDate scope and arrival-date cutoff.';
comment on function reporting.fn_company_metrics_snapshot_v4_filtered(uuid,date,date,uuid,uuid,uuid,text,date,text) is
    'Filtered canonical company analytics with explicit WritingDate or ArrivalDate scope and arrival-date cutoff.';
comment on function reporting.fn_company_month_series_v4(uuid,date,date,date,text) is
    'Company monthly series backed by the v4 date-basis-aware snapshot.';

insert into system.database_settings(singleton, database_version, compatible_application_version, last_updated_at)
values (true, '062', '1.0.6', now())
on conflict (singleton) do update set
    database_version = excluded.database_version,
    compatible_application_version = excluded.compatible_application_version,
    last_updated_at = excluded.last_updated_at;
