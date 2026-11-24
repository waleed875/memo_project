-- Migration 058: expose one canonical consumer snapshot for company metrics.
-- The base 054 contract remains immutable; this additive wrapper centralizes the
-- nullable time-compliance rate so desktop and web consumers do not recalculate it.

create or replace function reporting.fn_company_metrics_snapshot_v2(
    p_company_id uuid,
    p_from date,
    p_to date
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
    clean_reviewed_notes int,
    error_notes int,
    incomplete_review_notes int,
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
select
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
    m.clean_reviewed_notes,
    m.error_notes,
    m.incomplete_review_notes,
    m.time_eligible_notes,
    m.on_time_notes,
    m.late_notes,
    m.late_6_to_10_notes,
    m.late_11_to_20_notes,
    m.late_over_20_notes,
    m.missing_writing_date_notes,
    m.invalid_date_order_notes,
    round(m.on_time_notes::numeric * 100 / nullif(m.time_eligible_notes, 0), 2),
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
from reporting.fn_company_metrics_v2(p_company_id, p_from, p_to) m;
$function$;

comment on function reporting.fn_company_metrics_snapshot_v2(uuid, date, date) is
    'Canonical company metrics consumer snapshot. Time compliance is on-time divided by eligible notes and remains NULL when there are no eligible notes.';
