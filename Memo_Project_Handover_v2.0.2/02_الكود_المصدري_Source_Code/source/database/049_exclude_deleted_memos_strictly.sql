-- Migration 049: strict exclusion of soft-deleted memos from all reporting contracts.
-- Never edit migrations 001-048. This migration is idempotent for existing databases.

alter table core.notes add column if not exists deleted_at timestamp;
alter table core.notes add column if not exists deleted_by uuid references auth.users(id);
create index if not exists ix_notes_deleted_at on core.notes(deleted_at) where is_deleted = false;
create index if not exists ix_notes_deleted_by on core.notes(deleted_by) where is_deleted = true;

comment on column core.notes.deleted_at is 'UTC timestamp when an ADMIN soft-deleted the memo.';
comment on column core.notes.deleted_by is 'ADMIN user that soft-deleted the memo.';

-- Re-assert the reporting views with an explicit non-deleted predicate.
create or replace view reporting.vw_note_details as
select n.id as note_id, n.note_number, nt.id as note_type_id, nt.name_ar as note_type_name,
       c.id as company_id, c.name as company_name, n.factory_id, f.name as factory_name,
       n.production_line_id, pl.name as production_line_name,
       n.writing_date, n.arrival_at, n.status, n.image_path,
       n.created_at, n.created_by, creator.full_name as created_by_name,
       n.review_started_at, n.approved_at, n.approved_by, approver.full_name as approved_by_name,
       count(ntf.field_id)::int as total_fields,
       count(nfv.field_id) filter (where nfv.field_value = 1)::int as active_count,
       count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::int as inactive_count,
       count(nfv.field_id) filter (where nfv.field_value = 0)::int as unknown_count,
       coalesce(round(count(nfv.field_id) filter (where nfv.field_value = 1)::numeric * 100 / nullif(count(nfv.field_id) filter (where nfv.field_value <> 0), 0), 2), 0) as accuracy_percentage,
       coalesce(round(count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::numeric * 100 / nullif(count(nfv.field_id) filter (where nfv.field_value <> 0), 0), 2), 0) as error_percentage
from core.notes n
join core.note_types nt on nt.id = n.note_type_id
join core.companies c on c.id = n.company_id
left join core.factories f on f.id = n.factory_id
left join core.production_lines pl on pl.id = n.production_line_id
left join auth.users creator on creator.id = n.created_by
left join auth.users approver on approver.id = n.approved_by
join core.note_type_fields ntf on ntf.note_type_id = n.note_type_id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = ntf.field_id
where n.is_deleted = false
group by n.id, nt.id, nt.name_ar, c.id, c.name, f.id, f.name, pl.id, pl.name,
         creator.full_name, approver.full_name;

create or replace view reporting.vw_note_field_details as
select n.id as note_id, n.note_number, c.id as company_id, c.name as company_name,
       n.factory_id, f.name as factory_name, n.production_line_id, pl.name as production_line_name,
       nt.id as note_type_id, nt.name_ar as note_type_name, nf.id as field_id, nf.name_ar as field_name,
       nfv.field_value,
       case nfv.field_value when 0 then 'لم تتم المراجعة' when 1 then 'صحيح' when 2 then 'غير صحيح' when 3 then 'مفقود' when 4 then 'غير واضح' when 5 then 'غير مكتمل' when 6 then 'لا ينطبق' else 'غير مدخل' end as field_value_label_ar,
       nfv.inactive_reason, n.writing_date, n.arrival_at, n.status,
       n.created_at, n.review_started_at, n.approved_at
from core.notes n
join core.companies c on c.id = n.company_id
join core.note_types nt on nt.id = n.note_type_id
left join core.factories f on f.id = n.factory_id
left join core.production_lines pl on pl.id = n.production_line_id
join core.note_type_fields ntf on ntf.note_type_id = nt.id and ntf.is_active and not ntf.is_deleted
join core.note_fields nf on nf.id = ntf.field_id and nf.is_active and not nf.is_deleted
left join core.note_field_values nfv on nfv.note_id = n.id and nfv.field_id = nf.id
where n.is_deleted = false;

-- Productivity is a reporting contract too; deleted notes must not contribute to it.
create or replace function reporting.fn_user_productivity(p_from date, p_to date)
returns table(user_id uuid, full_name varchar, created_notes int, updated_notes int, approved_notes int, last_activity timestamp)
language sql stable as $$
    select u.id, u.full_name,
           count(distinct n.id) filter (where n.created_by = u.id)::int,
           count(distinct n.id) filter (where n.updated_by = u.id)::int,
           count(distinct n.id) filter (where n.approved_by = u.id)::int,
           greatest(max(n.created_at), max(n.updated_at), max(n.approved_at))
    from auth.users u
    left join core.notes n on (n.created_by = u.id or n.updated_by = u.id or n.approved_by = u.id)
        and n.is_deleted = false
        and n.arrival_at::date between p_from and p_to
    where not u.is_deleted
    group by u.id, u.full_name
    order by 3 desc, 4 desc;
$$;

-- Re-assert the sole interactive dashboard contract with the same strict scope.
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
    where n.is_deleted = false
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