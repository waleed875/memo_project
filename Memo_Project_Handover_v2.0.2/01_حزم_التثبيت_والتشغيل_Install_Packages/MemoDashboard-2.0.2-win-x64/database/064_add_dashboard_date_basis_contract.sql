-- Migration 064: make the desktop dashboard use the same explicit date basis
-- and arrival cutoff contract as the report center and web dashboard.

do $function$
declare
    definition text;
begin
    select pg_get_functiondef(p.oid)
      into definition
    from pg_proc p
    join pg_namespace n on n.oid = p.pronamespace
    where n.nspname = 'reporting'
      and p.proname = 'fn_dashboard_snapshot'
    order by p.oid desc
    limit 1;

    if definition is null then
        raise exception 'reporting.fn_dashboard_snapshot was not found';
    end if;

    definition := replace(definition,
        'fn_dashboard_snapshot(',
        'fn_dashboard_snapshot_v2(');
    definition := replace(definition,
        'p_timezone text DEFAULT ''Asia/Damascus''::text)',
        'p_timezone text DEFAULT ''Asia/Damascus''::text, p_date_basis text DEFAULT ''WritingDate''::text, p_arrival_cutoff_date date DEFAULT NULL::date)');

    definition := replace(definition,
        'n.approved_at, n.review_started_at',
        E'n.approved_at, n.review_started_at,\n           case when lower(coalesce(p_date_basis, ''WritingDate'')) in (''arrivaldate'', ''arrival_date'', ''arrival'') then n.arrival_at::date else n.writing_date end as period_date');
    definition := replace(definition,
        'and (p_from is null or n.arrival_at >= p_from)',
        'and (p_from is null or (case when lower(coalesce(p_date_basis, ''WritingDate'')) in (''arrivaldate'', ''arrival_date'', ''arrival'') then n.arrival_at::date else n.writing_date end) >= p_from)');
    definition := replace(definition,
        'and (p_to is null or n.arrival_at <= p_to)',
        E'and (p_to is null or (case when lower(coalesce(p_date_basis, ''WritingDate'')) in (''arrivaldate'', ''arrival_date'', ''arrival'') then n.arrival_at::date else n.writing_date end) <= p_to)\n       and (p_arrival_cutoff_date is null or n.arrival_at::date <= p_arrival_cutoff_date)');
    definition := replace(definition,
        'n.arrival_at = clock.today',
        'n.period_date = clock.today');
    definition := replace(definition,
        'date_trunc(''month'', n.arrival_at) = date_trunc(''month'', clock.today)',
        'date_trunc(''month'', n.period_date::timestamp) = date_trunc(''month'', clock.today::timestamp)');
    definition := replace(definition,
        'n.arrival_at >= month_start and n.arrival_at < month_start + interval ''1 month''',
        'n.period_date >= month_start::date and n.period_date < (month_start + interval ''1 month'')::date');
    definition := replace(definition,
        'n.status, n.arrival_at, n.created_at from scoped_notes n',
        'n.status, n.arrival_at, n.created_at, n.period_date from scoped_notes n');
    definition := replace(definition,
        'order by x.arrival_at desc, x.created_at desc',
        'order by x.period_date desc, x.created_at desc');

    execute definition;
end;
$function$;

comment on function reporting.fn_dashboard_snapshot_v2(date, date, uuid, uuid, uuid, uuid, varchar, text, text, date) is
    'Dashboard snapshot with explicit WritingDate/ArrivalDate basis and an independent arrival_at cutoff.';

insert into system.database_settings(singleton, database_version, compatible_application_version, last_updated_at)
values (true, '064', '1.0.6', now())
on conflict (singleton) do update set
    database_version = excluded.database_version,
    compatible_application_version = excluded.compatible_application_version,
    last_updated_at = excluded.last_updated_at;
