-- Migration 060: canonical v3 company metrics contract.
-- Additive only: v2 remains available for legacy consumers and is not modified.
-- The analytics classification in this contract is independent from core.notes.status.

create or replace view reporting.vw_note_indicator_facts_v3 as
select
    n.id as note_id,
    n.note_number,
    n.company_id,
    n.factory_id,
    n.production_line_id,
    n.note_type_id,
    nt.code as note_type_code,
    n.status as workflow_status,
    n.created_at,
    n.writing_date,
    n.arrival_at,
    n.created_by,
    ntf.field_id,
    nf.code as field_code,
    nf.name_ar as field_name,
    (nfv.id is not null) as has_value_row,
    nfv.field_value as stored_field_value,
    coalesce(nfv.field_value, 0)::smallint as effective_field_value,
    (nfv.id is null or nfv.field_value = 0) as is_unreviewed,
    (nfv.field_value between 1 and 6) as is_reviewed,
    (nfv.field_value between 1 and 5) as is_applicable_reviewed,
    (nfv.field_value = 1) as is_correct,
    (nfv.field_value between 2 and 5) as is_error,
    (nfv.field_value = 6) as is_not_applicable
from core.notes n
join core.note_types nt
  on nt.id = n.note_type_id
join core.note_type_fields ntf
  on ntf.note_type_id = n.note_type_id
 and ntf.is_active
 and not ntf.is_deleted
join core.note_fields nf
  on nf.id = ntf.field_id
 and nf.is_active
 and not nf.is_deleted
left join core.note_field_values nfv
  on nfv.note_id = n.id
 and nfv.field_id = ntf.field_id
where not n.is_deleted;

comment on view reporting.vw_note_indicator_facts_v3 is
    'v3 facts: missing/0 unreviewed, 1 correct, 2..5 error, 6 not applicable; workflow_status is not analytics classification.';

create or replace function reporting.fn_company_metrics_snapshot_v3(
    p_company_id uuid,
    p_from date,
    p_to date,
    p_entry_cutoff_date date default null
)
returns table(
    total_notes int,
    expected_items int,
    reviewed_items int,
    unreviewed_items int,
    applicable_reviewed_items int,
    correct_items int,
    error_items int,
    not_applicable_items int,
    review_coverage_percentage numeric,
    item_quality_percentage numeric,
    errors_per_100_checked numeric,
    quality_eligible_note_count int,
    clean_reviewed_notes int,
    error_notes int,
    incomplete_review_notes int,
    all_not_applicable_note_count int,
    no_expected_items_note_count int,
    clean_reviewed_note_percent numeric,
    time_eligible_notes int,
    on_time_notes int,
    late_notes int,
    late_6_to_10_notes int,
    late_11_to_20_notes int,
    late_over_20_notes int,
    missing_writing_date_notes int,
    invalid_date_order_notes int,
    time_compliance_percentage numeric,
    average_late_days numeric,
    median_late_days numeric,
    p90_late_days numeric,
    maximum_late_days int,
    average_excess_delay_days numeric,
    duplicate_number_incidents int,
    company_resend_incidents int,
    duplicate_incidents int,
    affected_notes int,
    incidents_per_100_notes numeric,
    affected_note_rate_percentage numeric,
    duplicate_free_percentage numeric,
    entry_notes int,
    linked_entry_notes int,
    unlinked_entry_notes int,
    link_efficiency_percentage numeric
)
language sql
stable
as $function$
with scoped_notes as (
    select n.id,
           n.company_id,
           n.note_type_id,
           n.created_at,
           n.writing_date,
           n.arrival_at
    from core.notes n
    where n.company_id = p_company_id
      and not n.is_deleted
      and (p_from is null or n.arrival_at >= p_from)
      and (p_to is null or n.arrival_at <= p_to)
      and (p_entry_cutoff_date is null
           or n.created_at < (p_entry_cutoff_date + 1)::timestamp)
),
note_quality_counts as (
    select
        n.id as note_id,
        count(f.field_id)::int as expected_items,
        count(f.field_id) filter (where f.is_reviewed)::int as reviewed_items,
        count(f.field_id) filter (where f.is_unreviewed)::int as unreviewed_items,
        count(f.field_id) filter (where f.is_applicable_reviewed)::int as applicable_reviewed_items,
        count(f.field_id) filter (where f.is_correct)::int as correct_items,
        count(f.field_id) filter (where f.is_error)::int as error_items,
        count(f.field_id) filter (where f.is_not_applicable)::int as not_applicable_items
    from scoped_notes n
    left join reporting.vw_note_indicator_facts_v3 f on f.note_id = n.id
    group by n.id
),
note_quality as (
    select q.*,
           case
               when q.expected_items = 0 then 'NO_EXPECTED_ITEMS'
               when q.error_items > 0 then 'ERROR'
               when q.unreviewed_items > 0 then 'INCOMPLETE'
               when q.applicable_reviewed_items > 0 then 'CLEAN_REVIEWED'
               when q.not_applicable_items = q.expected_items then 'ALL_NOT_APPLICABLE'
               else 'NO_EXPECTED_ITEMS'
           end as note_quality_classification
    from note_quality_counts q
),
quality_totals as (
    select
        count(*)::int as total_notes,
        coalesce(sum(q.expected_items), 0)::int as expected_items,
        coalesce(sum(q.reviewed_items), 0)::int as reviewed_items,
        coalesce(sum(q.unreviewed_items), 0)::int as unreviewed_items,
        coalesce(sum(q.applicable_reviewed_items), 0)::int as applicable_reviewed_items,
        coalesce(sum(q.correct_items), 0)::int as correct_items,
        coalesce(sum(q.error_items), 0)::int as error_items,
        coalesce(sum(q.not_applicable_items), 0)::int as not_applicable_items,
        count(*) filter (where q.note_quality_classification = 'CLEAN_REVIEWED')::int as clean_reviewed_notes,
        count(*) filter (where q.note_quality_classification = 'ERROR')::int as error_notes,
        count(*) filter (where q.note_quality_classification = 'INCOMPLETE')::int as incomplete_review_notes,
        count(*) filter (where q.note_quality_classification = 'ALL_NOT_APPLICABLE')::int as all_not_applicable_note_count,
        count(*) filter (where q.note_quality_classification = 'NO_EXPECTED_ITEMS')::int as no_expected_items_note_count
    from note_quality q
),
delay_facts as (
    select
        n.id,
        n.writing_date,
        n.arrival_at,
        case
            when n.writing_date is not null and n.arrival_at >= n.writing_date
                then n.arrival_at - n.writing_date
            else null
        end as delay_days
    from scoped_notes n
),
time_totals as (
    select
        count(*) filter (where writing_date is not null and arrival_at >= writing_date)::int as time_eligible_notes,
        count(*) filter (where delay_days between 0 and 5)::int as on_time_notes,
        count(*) filter (where delay_days > 5)::int as late_notes,
        count(*) filter (where delay_days between 6 and 10)::int as late_6_to_10_notes,
        count(*) filter (where delay_days between 11 and 20)::int as late_11_to_20_notes,
        count(*) filter (where delay_days > 20)::int as late_over_20_notes,
        count(*) filter (where writing_date is null)::int as missing_writing_date_notes,
        count(*) filter (where writing_date is not null and arrival_at < writing_date)::int as invalid_date_order_notes,
        round(avg(delay_days) filter (where delay_days > 5), 2) as average_late_days,
        round((percentile_cont(0.5) within group (order by delay_days)
            filter (where delay_days > 5))::numeric, 2) as median_late_days,
        round((percentile_cont(0.9) within group (order by delay_days)
            filter (where delay_days > 5))::numeric, 2) as p90_late_days,
        max(delay_days) filter (where delay_days > 5)::int as maximum_late_days,
        round(avg(delay_days - 5) filter (where delay_days > 5), 2) as average_excess_delay_days
    from delay_facts
),
duplicate_references as (
    select
        di.id,
        di.classification,
        case di.classification
            when 'COMPANY_DUPLICATE_NUMBER' then di.created_note_id
            when 'COMPANY_RESEND' then di.matched_note_id
            else null
        end as reference_note_id
    from core.duplicate_incidents di
    where di.company_id = p_company_id
),
scoped_incidents as (
    select d.id, d.classification, d.reference_note_id
    from duplicate_references d
    join scoped_notes n on n.id = d.reference_note_id
),
duplicate_totals as (
    select
        count(*) filter (where classification = 'COMPANY_DUPLICATE_NUMBER')::int as duplicate_number_incidents,
        count(*) filter (where classification = 'COMPANY_RESEND')::int as company_resend_incidents,
        count(*)::int as duplicate_incidents,
        count(distinct reference_note_id)::int as affected_notes
    from scoped_incidents
),
scoped_entries as (
    select n.id, n.company_id
    from scoped_notes n
    join core.note_types nt on nt.id = n.note_type_id
    where nt.code = 'ENTRY'
),
linked_entries as (
    select distinct e.id
    from scoped_entries e
    join core.note_links l
      on l.source_note_id = e.id or l.related_note_id = e.id
    join core.notes counterpart
      on counterpart.id = case
          when l.source_note_id = e.id then l.related_note_id
          else l.source_note_id
      end
     and not counterpart.is_deleted
     and counterpart.company_id = e.company_id
     and (p_entry_cutoff_date is null
          or counterpart.created_at < (p_entry_cutoff_date + 1)::timestamp)
    join core.note_types counterpart_type
      on counterpart_type.id = counterpart.note_type_id
     and counterpart_type.code = 'EXIT'
),
link_totals as (
    select
        (select count(*)::int from scoped_entries) as entry_notes,
        (select count(*)::int from linked_entries) as linked_entry_notes
)
select
    q.total_notes,
    q.expected_items,
    q.reviewed_items,
    q.unreviewed_items,
    q.applicable_reviewed_items,
    q.correct_items,
    q.error_items,
    q.not_applicable_items,
    round(q.reviewed_items::numeric * 100 / nullif(q.expected_items, 0), 2),
    round(q.correct_items::numeric * 100 / nullif(q.applicable_reviewed_items, 0), 2),
    round(q.error_items::numeric * 100 / nullif(q.applicable_reviewed_items, 0), 2),
    (q.clean_reviewed_notes + q.error_notes)::int,
    q.clean_reviewed_notes,
    q.error_notes,
    q.incomplete_review_notes,
    q.all_not_applicable_note_count,
    q.no_expected_items_note_count,
    round(q.clean_reviewed_notes::numeric * 100 / nullif(q.clean_reviewed_notes + q.error_notes, 0), 2),
    t.time_eligible_notes,
    t.on_time_notes,
    t.late_notes,
    t.late_6_to_10_notes,
    t.late_11_to_20_notes,
    t.late_over_20_notes,
    t.missing_writing_date_notes,
    t.invalid_date_order_notes,
    round(t.on_time_notes::numeric * 100 / nullif(t.time_eligible_notes, 0), 2),
    t.average_late_days,
    t.median_late_days,
    t.p90_late_days,
    t.maximum_late_days,
    t.average_excess_delay_days,
    d.duplicate_number_incidents,
    d.company_resend_incidents,
    d.duplicate_incidents,
    d.affected_notes,
    round(d.duplicate_incidents::numeric * 100 / nullif(q.total_notes, 0), 2),
    round(d.affected_notes::numeric * 100 / nullif(q.total_notes, 0), 2),
    round(100 - d.affected_notes::numeric * 100 / nullif(q.total_notes, 0), 2),
    l.entry_notes,
    l.linked_entry_notes,
    l.entry_notes - l.linked_entry_notes,
    round(l.linked_entry_notes::numeric * 100 / nullif(l.entry_notes, 0), 2)
from quality_totals q
cross join time_totals t
cross join duplicate_totals d
cross join link_totals l;
$function$;

comment on function reporting.fn_company_metrics_snapshot_v3(uuid, date, date, date) is
    'Canonical v3 company analytics. Error takes precedence over incomplete; all-N/A and no-expected notes are separate; quality denominator is clean plus error. arrival_at scopes the period and created_at cutoff is inclusive through the supplied day.';

create or replace function reporting.fn_company_month_series_v3(
    p_company_id uuid,
    p_from date,
    p_to date,
    p_entry_cutoff_date date default null
)
returns table(
    month_start date,
    period_label text,
    total_notes int,
    expected_items int,
    reviewed_items int,
    unreviewed_items int,
    applicable_reviewed_items int,
    correct_items int,
    error_items int,
    not_applicable_items int,
    review_coverage_percentage numeric,
    item_quality_percentage numeric,
    errors_per_100_checked numeric,
    quality_eligible_note_count int,
    clean_reviewed_notes int,
    error_notes int,
    incomplete_review_notes int,
    all_not_applicable_note_count int,
    no_expected_items_note_count int,
    clean_reviewed_note_percent numeric,
    time_eligible_notes int,
    on_time_notes int,
    late_notes int,
    late_6_to_10_notes int,
    late_11_to_20_notes int,
    late_over_20_notes int,
    missing_writing_date_notes int,
    invalid_date_order_notes int,
    time_compliance_percentage numeric,
    average_late_days numeric,
    median_late_days numeric,
    p90_late_days numeric,
    maximum_late_days int,
    average_excess_delay_days numeric,
    duplicate_number_incidents int,
    company_resend_incidents int,
    duplicate_incidents int,
    affected_notes int,
    incidents_per_100_notes numeric,
    affected_note_rate_percentage numeric,
    duplicate_free_percentage numeric,
    entry_notes int,
    linked_entry_notes int,
    unlinked_entry_notes int,
    link_efficiency_percentage numeric
)
language sql
stable
as $function$
with month_bounds as (
    select
        month_value::date as month_start,
        greatest(month_value::date, p_from) as period_from,
        least((month_value + interval '1 month - 1 day')::date, p_to) as period_to
    from generate_series(
        date_trunc('month', p_from::timestamp),
        date_trunc('month', p_to::timestamp),
        interval '1 month'
    ) month_value
    where p_from <= p_to
)
select
    b.month_start,
    to_char(b.month_start, 'YYYY/MM') as period_label,
    m.total_notes,
    m.expected_items,
    m.reviewed_items,
    m.unreviewed_items,
    m.applicable_reviewed_items,
    m.correct_items,
    m.error_items,
    m.not_applicable_items,
    m.review_coverage_percentage,
    m.item_quality_percentage,
    m.errors_per_100_checked,
    m.quality_eligible_note_count,
    m.clean_reviewed_notes,
    m.error_notes,
    m.incomplete_review_notes,
    m.all_not_applicable_note_count,
    m.no_expected_items_note_count,
    m.clean_reviewed_note_percent,
    m.time_eligible_notes,
    m.on_time_notes,
    m.late_notes,
    m.late_6_to_10_notes,
    m.late_11_to_20_notes,
    m.late_over_20_notes,
    m.missing_writing_date_notes,
    m.invalid_date_order_notes,
    m.time_compliance_percentage,
    m.average_late_days,
    m.median_late_days,
    m.p90_late_days,
    m.maximum_late_days,
    m.average_excess_delay_days,
    m.duplicate_number_incidents,
    m.company_resend_incidents,
    m.duplicate_incidents,
    m.affected_notes,
    m.incidents_per_100_notes,
    m.affected_note_rate_percentage,
    m.duplicate_free_percentage,
    m.entry_notes,
    m.linked_entry_notes,
    m.unlinked_entry_notes,
    m.link_efficiency_percentage
from month_bounds b
cross join lateral reporting.fn_company_metrics_snapshot_v3(
    p_company_id,
    b.period_from,
    b.period_to,
    p_entry_cutoff_date
) m
order by b.month_start;
$function$;

comment on function reporting.fn_company_month_series_v3(uuid, date, date, date) is
    'Six-month arrival-date series backed by the v3 snapshot. Every month uses the same optional inclusive created_at cutoff and the same nullable denominators.';