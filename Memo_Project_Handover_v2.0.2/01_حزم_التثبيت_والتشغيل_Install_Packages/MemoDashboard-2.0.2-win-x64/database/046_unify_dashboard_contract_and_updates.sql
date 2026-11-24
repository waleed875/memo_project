-- Release 1.0.3: one authoritative dashboard scope plus the update-center schema.
-- The dashboard function deliberately uses arrival_date for every surface.

insert into system.app_settings(key, value) values
    ('INTERACTIVE_DASHBOARD_URL', null),
    ('INTERACTIVE_DASHBOARD_DISPLAY_URL', null)
on conflict (key) do nothing;

create schema if not exists updates;

create table if not exists updates.releases (
    id uuid primary key default gen_random_uuid(),
    version varchar(30) not null unique,
    status varchar(20) not null default 'DRAFT',
    release_notes text not null default '',
    minimum_version varchar(30),
    minimum_database_version varchar(20),
    target_database_version varchar(20),
    published_at timestamp,
    published_by uuid references auth.users(id),
    created_at timestamp not null default now(),
    created_by uuid references auth.users(id),
    constraint ck_updates_release_status check (status in ('DRAFT', 'PUBLISHED', 'STOPPED'))
);

create table if not exists updates.artifacts (
    id uuid primary key default gen_random_uuid(),
    release_id uuid not null references updates.releases(id) on delete cascade,
    operating_system varchar(20) not null,
    architecture varchar(30) not null,
    package_url text not null,
    package_size bigint not null check (package_size >= 0),
    sha256 char(64) not null,
    manifest_signature text not null,
    created_at timestamp not null default now(),
    unique(release_id, operating_system, architecture),
    constraint ck_updates_artifact_os check (operating_system in ('windows', 'linux')),
    constraint ck_updates_artifact_sha256 check (sha256 ~ '^[0-9A-Fa-f]{64}$')
);

create table if not exists updates.installations (
    device_id uuid primary key,
    device_name varchar(200),
    operating_system varchar(20) not null,
    architecture varchar(30) not null,
    installed_version varchar(30),
    last_checked_at timestamp,
    last_update_status varchar(30),
    last_update_message text,
    updated_at timestamp not null default now()
);

create table if not exists updates.audit_log (
    id bigserial primary key,
    user_id uuid references auth.users(id),
    action varchar(80) not null,
    release_id uuid references updates.releases(id),
    description text,
    created_at timestamp not null default now()
);

create index if not exists ix_updates_releases_status_published
    on updates.releases(status, published_at desc);
create index if not exists ix_updates_installations_checked
    on updates.installations(last_checked_at desc);

create or replace function reporting.fn_dashboard_snapshot(
    p_from date default null,
    p_to date default null,
    p_company_id uuid default null,
    p_factory_id uuid default null,
    p_production_line_id uuid default null,
    p_note_type_id uuid default null,
    p_status varchar default null,
    p_timezone text default 'Asia/Damascus')
returns jsonb
language sql
stable
as $$
with dashboard_clock as (
    select (now() at time zone p_timezone)::date as today
),
scoped_notes as (
    select n.id, n.note_number, n.company_id, n.factory_id, n.production_line_id,
           n.note_type_id, n.status, n.writing_date, n.arrival_at, n.created_at,
           n.approved_at, n.review_started_at
    from core.notes n
    where not n.is_deleted
      and (p_from is null or n.arrival_at >= p_from)
      and (p_to is null or n.arrival_at <= p_to)
      and (p_company_id is null or n.company_id = p_company_id)
      and (p_factory_id is null or n.factory_id = p_factory_id)
      and (p_production_line_id is null or n.production_line_id = p_production_line_id)
      and (p_note_type_id is null or n.note_type_id = p_note_type_id)
      and (p_status is null or n.status = p_status)
),
scoped_duplicates as (
    select d.*
    from core.duplicate_incidents d
    join scoped_notes n on n.id = d.matched_note_id
),
summary as (
    select
        count(*)::int as total_notes,
        count(*) filter (where n.arrival_at = clock.today)::int as today_notes,
        count(*) filter (where date_trunc('month', n.arrival_at) = date_trunc('month', clock.today))::int as current_month_notes,
        count(*) filter (where n.status = 'APPROVED')::int as approved_notes,
        count(*) filter (where n.status in ('COMPLETE', 'HAS_ERRORS', 'NEEDS_REVIEW', 'UNDER_REVIEW'))::int as pending_review_notes,
        count(*) filter (where n.status = 'RETURNED_FOR_CORRECTION')::int as returned_notes,
        count(*) filter (where n.writing_date is not null and n.arrival_at >= n.writing_date and n.arrival_at - n.writing_date > 5)::int as late_notes,
        count(*) filter (where n.writing_date is not null and n.arrival_at >= n.writing_date and n.arrival_at - n.writing_date > 5 and date_trunc('month', n.arrival_at) = date_trunc('month', n.writing_date))::int as late_same_month_notes,
        count(*) filter (where n.writing_date is not null and n.arrival_at >= n.writing_date and n.arrival_at - n.writing_date > 5 and date_trunc('month', n.arrival_at) <> date_trunc('month', n.writing_date))::int as late_cross_month_notes,
        count(*) filter (where n.writing_date is not null and n.arrival_at >= n.writing_date and n.arrival_at - n.writing_date between 0 and 5)::int as on_time_notes,
        count(*) filter (where n.writing_date is not null and n.arrival_at < n.writing_date)::int as invalid_date_order_notes,
        coalesce(round(avg(n.arrival_at - n.writing_date) filter (where n.writing_date is not null and n.arrival_at >= n.writing_date and n.arrival_at - n.writing_date > 5), 2), 0) as average_late_days,
        coalesce(max(n.arrival_at - n.writing_date) filter (where n.writing_date is not null and n.arrival_at >= n.writing_date and n.arrival_at - n.writing_date > 5), 0)::int as maximum_late_days,
        max(n.arrival_at) as latest_arrival_at
    from scoped_notes n cross join dashboard_clock clock
)
select jsonb_build_object(
    'summary', jsonb_build_object(
        'totalNotes', summary.total_notes,
        'todayNotes', summary.today_notes,
        'currentMonthNotes', summary.current_month_notes,
        'activeCompanies', (select count(distinct n.company_id)::int from scoped_notes n join core.companies c on c.id = n.company_id where c.is_active and not c.is_deleted),
        'approvedNotes', summary.approved_notes,
        'pendingReviewNotes', summary.pending_review_notes,
        'returnedNotes', summary.returned_notes,
        'companyResendNotes', (select count(*)::int from scoped_duplicates where classification = 'COMPANY_RESEND'),
        'duplicateNumberNotes', (select count(*)::int from scoped_duplicates where classification = 'COMPANY_DUPLICATE_NUMBER'),
        'lateNotes', summary.late_notes,
        'lateSameMonthNotes', summary.late_same_month_notes,
        'lateCrossMonthNotes', summary.late_cross_month_notes,
        'onTimeNotes', summary.on_time_notes,
        'invalidDateOrderNotes', summary.invalid_date_order_notes,
        'averageLateDays', summary.average_late_days,
        'maximumLateDays', summary.maximum_late_days,
        'latestArrivalAt', summary.latest_arrival_at
    ),
    'notesByCompany', coalesce((select jsonb_agg(jsonb_build_object('label', x.label, 'value', x.value) order by x.value desc, x.label)
        from (select c.name as label, count(*)::int as value from scoped_notes n join core.companies c on c.id = n.company_id group by c.name order by count(*) desc, c.name limit 8) x), '[]'::jsonb),
    'monthlyNotes', coalesce((select jsonb_agg(jsonb_build_object('label', x.label, 'value', x.value) order by x.month_start)
        from (select month_start, to_char(month_start, 'YYYY/MM') as label, count(n.id)::int as value
              from dashboard_clock clock
              cross join generate_series(date_trunc('month', clock.today) - interval '5 months', date_trunc('month', clock.today), interval '1 month') month_start
              left join scoped_notes n on n.arrival_at >= month_start and n.arrival_at < month_start + interval '1 month'
              group by month_start) x), '[]'::jsonb),
    'notesByStatus', coalesce((select jsonb_agg(jsonb_build_object('label', x.label, 'value', x.value) order by x.value desc, x.label)
        from (select status as label, count(*)::int as value from scoped_notes group by status) x), '[]'::jsonb),
    'duplicatesByType', coalesce((select jsonb_agg(jsonb_build_object('label', x.label, 'value', x.value) order by x.label)
        from (select classification as label, count(*)::int as value from scoped_duplicates group by classification) x), '[]'::jsonb),
    'recentNotes', coalesce((select jsonb_agg(jsonb_build_object('noteNumber', x.note_number, 'companyName', x.company_name, 'noteTypeName', x.note_type_name, 'status', x.status, 'arrivalAt', x.arrival_at) order by x.arrival_at desc, x.created_at desc)
        from (select n.note_number, c.name as company_name, nt.name_ar as note_type_name, n.status, n.arrival_at, n.created_at from scoped_notes n join core.companies c on c.id = n.company_id join core.note_types nt on nt.id = n.note_type_id order by n.arrival_at desc, n.created_at desc limit 10) x), '[]'::jsonb)
)
from summary;
$$;

comment on function reporting.fn_dashboard_snapshot(date, date, uuid, uuid, uuid, uuid, varchar, text) is
    'The sole dashboard contract. All counts use non-deleted notes, arrival date, supplied filters and the supplied business timezone. Duplicate counts are duplicate incidents linked to notes in the same scope.';

update system.database_settings
set compatible_application_version = '1.0.3', last_updated_at = now()
where singleton = true;
