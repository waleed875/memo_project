-- Migration 069: Add loot and invoice flags to core.notes and update machinery efficiency KPI

alter table core.notes add column if not exists is_loot boolean not null default false;
alter table core.notes add column if not exists is_invoice boolean not null default false;

create index if not exists ix_notes_loot on core.notes(company_id, is_loot) where not is_deleted and is_loot = true;
create index if not exists ix_notes_invoice on core.notes(company_id, is_invoice) where not is_deleted and is_invoice = true;

-- Enforce that loot can only be set on ENTRY notes, and invoice can only be set on EXIT notes
create or replace function core.validate_note_loot_invoice_flags()
returns trigger
language plpgsql
as $$
declare
    note_type_code text;
begin
    if new.is_loot or new.is_invoice then
        select code into note_type_code
        from core.note_types
        where id = new.note_type_id;

        if new.is_loot and note_type_code <> 'ENTRY' then
            raise exception using errcode = '23514', message = 'خيار الغنائم والمستوردات متاح فقط لمذكرات الإدخال.';
        end if;

        if new.is_invoice and note_type_code <> 'EXIT' then
            raise exception using errcode = '23514', message = 'خيار الفاتورة متاح فقط لمذكرات الإخراج.';
        end if;
    end if;

    return new;
end;
$$;

drop trigger if exists trg_notes_validate_loot_invoice on core.notes;
create trigger trg_notes_validate_loot_invoice
before insert or update of note_type_id, is_loot, is_invoice on core.notes
for each row execute function core.validate_note_loot_invoice_flags();

-- Backfill from existing core.note_sources
update core.notes n
set is_loot = true
from core.note_sources s
where s.note_id = n.id and s.source_type = 'LOOT' and not n.is_loot;

-- Update reporting.fn_company_metrics_snapshot_v4
create or replace function reporting.fn_company_metrics_snapshot_v4(
    p_company_id uuid,
    p_from date,
    p_to date,
    p_entry_cutoff_date date default null::date,
    p_date_basis text default 'WritingDate'::text
)
returns table(
    total_notes integer,
    expected_items integer,
    reviewed_items integer,
    unreviewed_items integer,
    applicable_reviewed_items integer,
    correct_items integer,
    error_items integer,
    not_applicable_items integer,
    review_coverage_percentage numeric,
    item_quality_percentage numeric,
    errors_per_100_checked numeric,
    quality_eligible_note_count integer,
    clean_reviewed_notes integer,
    error_notes integer,
    incomplete_review_notes integer,
    all_not_applicable_note_count integer,
    no_expected_items_note_count integer,
    clean_reviewed_note_percent numeric,
    time_eligible_notes integer,
    on_time_notes integer,
    late_notes integer,
    late_6_to_10_notes integer,
    late_11_to_20_notes integer,
    late_over_20_notes integer,
    missing_writing_date_notes integer,
    invalid_date_order_notes integer,
    time_compliance_percentage numeric,
    average_late_days numeric,
    median_late_days numeric,
    p90_late_days numeric,
    maximum_late_days integer,
    average_excess_delay_days numeric,
    duplicate_number_incidents integer,
    company_resend_incidents integer,
    duplicate_incidents integer,
    affected_notes integer,
    incidents_per_100_notes numeric,
    affected_note_rate_percentage numeric,
    duplicate_free_percentage numeric,
    entry_notes integer,
    linked_entry_notes integer,
    unlinked_entry_notes integer,
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
           n.arrival_at,
           coalesce(n.is_loot, false) as is_loot
    from core.notes n
    where (p_company_id is null or n.company_id = p_company_id)
      and not n.is_deleted
      and (p_from is null or case when p_date_basis = 'WritingDate' then n.writing_date else n.arrival_at::date end >= p_from)
      and (p_to is null or case when p_date_basis = 'WritingDate' then n.writing_date else n.arrival_at::date end <= p_to)
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
    where (p_company_id is null or di.company_id = p_company_id)
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
    select n.id, n.company_id, n.is_loot
    from scoped_notes n
    join core.note_types nt on nt.id = n.note_type_id
    where nt.code = 'ENTRY'
),
linked_entries as (
    select distinct e.id
    from scoped_entries e
    join core.v_note_links_compat l
      on l.source_note_id = e.id or l.related_note_id = e.id
    join core.notes counterpart
      on counterpart.id = case
          when l.source_note_id = e.id then l.related_note_id
          else l.source_note_id
      end
     and not counterpart.is_deleted
     and counterpart.company_id = e.company_id
     and (p_entry_cutoff_date is null
          or counterpart.arrival_at::date <= p_entry_cutoff_date)
    join core.note_types counterpart_type
      on counterpart_type.id = counterpart.note_type_id
     and counterpart_type.code = 'EXIT'
),
loot_entries as (
    select distinct e.id
    from scoped_entries e
    where e.is_loot = true
       or exists (select 1 from core.note_sources s where s.note_id = e.id and s.source_type = 'LOOT')
),
compliant_entries as (
    select id from linked_entries
    union
    select id from loot_entries
),
link_totals as (
    select
        (select count(*)::int from scoped_entries) as entry_notes,
        (select count(*)::int from compliant_entries) as linked_entry_notes
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

-- Update reporting.fn_company_metrics_snapshot_v4_filtered
create or replace function reporting.fn_company_metrics_snapshot_v4_filtered(
    p_company_id uuid,
    p_from date,
    p_to date,
    p_factory_id uuid,
    p_production_line_id uuid,
    p_note_type_id uuid,
    p_status text,
    p_entry_cutoff_date date default null::date,
    p_date_basis text default 'WritingDate'::text
)
returns table(
    total_notes integer,
    expected_items integer,
    reviewed_items integer,
    unreviewed_items integer,
    applicable_reviewed_items integer,
    correct_items integer,
    error_items integer,
    not_applicable_items integer,
    review_coverage_percentage numeric,
    item_quality_percentage numeric,
    errors_per_100_checked numeric,
    quality_eligible_note_count integer,
    clean_reviewed_notes integer,
    error_notes integer,
    incomplete_review_notes integer,
    all_not_applicable_note_count integer,
    no_expected_items_note_count integer,
    clean_reviewed_note_percent numeric,
    time_eligible_notes integer,
    on_time_notes integer,
    late_notes integer,
    late_6_to_10_notes integer,
    late_11_to_20_notes integer,
    late_over_20_notes integer,
    missing_writing_date_notes integer,
    invalid_date_order_notes integer,
    time_compliance_percentage numeric,
    average_late_days numeric,
    median_late_days numeric,
    p90_late_days numeric,
    maximum_late_days integer,
    average_excess_delay_days numeric,
    duplicate_number_incidents integer,
    company_resend_incidents integer,
    duplicate_incidents integer,
    affected_notes integer,
    incidents_per_100_notes numeric,
    affected_note_rate_percentage numeric,
    duplicate_free_percentage numeric,
    entry_notes integer,
    linked_entry_notes integer,
    unlinked_entry_notes integer,
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
           n.arrival_at,
           coalesce(n.is_loot, false) as is_loot
    from core.notes n
    where (p_company_id is null or n.company_id = p_company_id)
      and not n.is_deleted
      and (p_factory_id is null or n.factory_id = p_factory_id)
      and (p_production_line_id is null or n.production_line_id = p_production_line_id)
      and (p_note_type_id is null or n.note_type_id = p_note_type_id)
      and (p_status is null or n.status = p_status)
      and (p_from is null or case when p_date_basis = 'WritingDate' then n.writing_date else n.arrival_at::date end >= p_from)
      and (p_to is null or case when p_date_basis = 'WritingDate' then n.writing_date else n.arrival_at::date end <= p_to)
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
    where (p_company_id is null or di.company_id = p_company_id)
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
    select n.id, n.company_id, n.is_loot
    from scoped_notes n
    join core.note_types nt on nt.id = n.note_type_id
    where nt.code = 'ENTRY'
),
linked_entries as (
    select distinct e.id
    from scoped_entries e
    join core.v_note_links_compat l
      on l.source_note_id = e.id or l.related_note_id = e.id
    join core.notes counterpart
      on counterpart.id = case
          when l.source_note_id = e.id then l.related_note_id
          else l.source_note_id
      end
     and not counterpart.is_deleted
     and counterpart.company_id = e.company_id
     and (p_entry_cutoff_date is null
          or counterpart.arrival_at::date <= p_entry_cutoff_date)
    join core.note_types counterpart_type
      on counterpart_type.id = counterpart.note_type_id
     and counterpart_type.code = 'EXIT'
),
loot_entries as (
    select distinct e.id
    from scoped_entries e
    where e.is_loot = true
       or exists (select 1 from core.note_sources s where s.note_id = e.id and s.source_type = 'LOOT')
),
compliant_entries as (
    select id from linked_entries
    union
    select id from loot_entries
),
link_totals as (
    select
        (select count(*)::int from scoped_entries) as entry_notes,
        (select count(*)::int from compliant_entries) as linked_entry_notes
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

-- Also update v3 functions for backward compatibility with integration tests
create or replace function reporting.fn_company_metrics_snapshot_v3(
    p_company_id uuid,
    p_from date,
    p_to date,
    p_entry_cutoff_date date default null::date
)
returns table(
    total_notes integer,
    expected_items integer,
    reviewed_items integer,
    unreviewed_items integer,
    applicable_reviewed_items integer,
    correct_items integer,
    error_items integer,
    not_applicable_items integer,
    review_coverage_percentage numeric,
    item_quality_percentage numeric,
    errors_per_100_checked numeric,
    quality_eligible_note_count integer,
    clean_reviewed_notes integer,
    error_notes integer,
    incomplete_review_notes integer,
    all_not_applicable_note_count integer,
    no_expected_items_note_count integer,
    clean_reviewed_note_percent numeric,
    time_eligible_notes integer,
    on_time_notes integer,
    late_notes integer,
    late_6_to_10_notes integer,
    late_11_to_20_notes integer,
    late_over_20_notes integer,
    missing_writing_date_notes integer,
    invalid_date_order_notes integer,
    time_compliance_percentage numeric,
    average_late_days numeric,
    median_late_days numeric,
    p90_late_days numeric,
    maximum_late_days integer,
    average_excess_delay_days numeric,
    duplicate_number_incidents integer,
    company_resend_incidents integer,
    duplicate_incidents integer,
    affected_notes integer,
    incidents_per_100_notes numeric,
    affected_note_rate_percentage numeric,
    duplicate_free_percentage numeric,
    entry_notes integer,
    linked_entry_notes integer,
    unlinked_entry_notes integer,
    link_efficiency_percentage numeric
)
language sql
stable
as $function$
    select * from reporting.fn_company_metrics_snapshot_v4(p_company_id, p_from, p_to, p_entry_cutoff_date, 'ArrivalDate');
$function$;

create or replace function reporting.fn_company_metrics_snapshot_v3_filtered(
    p_company_id uuid,
    p_from date,
    p_to date,
    p_factory_id uuid,
    p_production_line_id uuid,
    p_note_type_id uuid,
    p_status text,
    p_entry_cutoff_date date default null::date
)
returns table(
    total_notes integer,
    expected_items integer,
    reviewed_items integer,
    unreviewed_items integer,
    applicable_reviewed_items integer,
    correct_items integer,
    error_items integer,
    not_applicable_items integer,
    review_coverage_percentage numeric,
    item_quality_percentage numeric,
    errors_per_100_checked numeric,
    quality_eligible_note_count integer,
    clean_reviewed_notes integer,
    error_notes integer,
    incomplete_review_notes integer,
    all_not_applicable_note_count integer,
    no_expected_items_note_count integer,
    clean_reviewed_note_percent numeric,
    time_eligible_notes integer,
    on_time_notes integer,
    late_notes integer,
    late_6_to_10_notes integer,
    late_11_to_20_notes integer,
    late_over_20_notes integer,
    missing_writing_date_notes integer,
    invalid_date_order_notes integer,
    time_compliance_percentage numeric,
    average_late_days numeric,
    median_late_days numeric,
    p90_late_days numeric,
    maximum_late_days integer,
    average_excess_delay_days numeric,
    duplicate_number_incidents integer,
    company_resend_incidents integer,
    duplicate_incidents integer,
    affected_notes integer,
    incidents_per_100_notes numeric,
    affected_note_rate_percentage numeric,
    duplicate_free_percentage numeric,
    entry_notes integer,
    linked_entry_notes integer,
    unlinked_entry_notes integer,
    link_efficiency_percentage numeric
)
language sql
stable
as $function$
    select * from reporting.fn_company_metrics_snapshot_v4_filtered(p_company_id, p_from, p_to, p_factory_id, p_production_line_id, p_note_type_id, p_status, p_entry_cutoff_date, 'ArrivalDate');
$function$;

insert into system.database_settings(singleton, database_version, compatible_application_version, last_updated_at)
values (true, '069', '1.0.7', now())
on conflict (singleton) do update set
    database_version = excluded.database_version,
    compatible_application_version = excluded.compatible_application_version,
    last_updated_at = excluded.last_updated_at;
