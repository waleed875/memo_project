-- Migration 070: Fix loot metrics calculation, unlinked datasets, and multi-exit link synchronization
-- 1) Exclude loot entry notes from unlinked_entry_notes and include in mechanism_efficiency_index across all report functions
-- 2) Update core.sync_legacy_note_link_to_source to transition seamlessly from LOOT/other to EXIT instead of throwing exception
-- 3) Ensure fn_company_month_series_v4 forwards p_date_basis to fn_company_metrics_snapshot_v4
-- 4) Reconcile core.notes.is_loot with core.note_sources

-- 1. Update core.sync_legacy_note_link_to_source() to handle source type transition
create or replace function core.sync_legacy_note_link_to_source()
returns trigger language plpgsql as $function$
declare
    v_entry_note_id uuid;
    v_exit_note_id uuid;
    v_entry_type text;
    v_exit_type text;
    v_source_id uuid;
    v_source_type text;
begin
    if tg_op = 'DELETE' then
        select case when source_type.code = 'ENTRY' then old.source_note_id else old.related_note_id end,
               case when source_type.code = 'ENTRY' then old.related_note_id else old.source_note_id end,
               source_type.code,
               related_type.code
          into v_entry_note_id, v_exit_note_id, v_entry_type, v_exit_type
        from core.notes source_note
        join core.note_types source_type on source_type.id = source_note.note_type_id
        join core.notes related_note on related_note.id = old.related_note_id
        join core.note_types related_type on related_type.id = related_note.note_type_id
        where source_note.id = old.source_note_id;

        if (v_entry_type = 'ENTRY' and v_exit_type = 'EXIT')
           or (v_entry_type = 'EXIT' and v_exit_type = 'ENTRY') then
            delete from core.note_source_exit_refs r
            using core.note_sources s
            where s.id = r.source_id
              and s.note_id = v_entry_note_id
              and r.exit_note_id = v_exit_note_id;
        end if;
        return old;
    end if;

    select case when source_type.code = 'ENTRY' then new.source_note_id else new.related_note_id end,
           case when source_type.code = 'ENTRY' then new.related_note_id else new.source_note_id end,
           source_type.code,
           related_type.code
      into v_entry_note_id, v_exit_note_id, v_entry_type, v_exit_type
    from core.notes source_note
    join core.note_types source_type on source_type.id = source_note.note_type_id
    join core.notes related_note on related_note.id = new.related_note_id
    join core.note_types related_type on related_type.id = related_note.note_type_id
    where source_note.id = new.source_note_id;

    if (v_entry_type = 'ENTRY' and v_exit_type = 'EXIT')
       or (v_entry_type = 'EXIT' and v_exit_type = 'ENTRY') then
        insert into core.note_sources(note_id, source_type, created_by)
        values (v_entry_note_id, 'EXIT', new.created_by)
        on conflict (note_id) do nothing;

        select s.id, s.source_type into v_source_id, v_source_type
        from core.note_sources s where s.note_id = v_entry_note_id;

        -- If note previously had a different source type (e.g. LOOT or OTHER), transition it cleanly to EXIT
        if v_source_type <> 'EXIT' then
            delete from core.note_source_loot where source_id = v_source_id;
            delete from core.note_source_invoices where source_id = v_source_id;
            update core.note_sources
            set source_type = 'EXIT',
                other_description = null,
                updated_at = now()
            where id = v_source_id;
        end if;

        -- Make sure is_loot is reset on the entry note since it is now linked to an exit note
        update core.notes
        set is_loot = false
        where id = v_entry_note_id and is_loot = true;

        insert into core.note_source_exit_refs(source_id, exit_note_id, reference_order)
        values (
            v_source_id,
            v_exit_note_id,
            coalesce((select max(r.reference_order) + 1 from core.note_source_exit_refs r where r.source_id = v_source_id), 1))
        on conflict (source_id, exit_note_id) do nothing;
    end if;
    return new;
end;
$function$;

-- 2. Update reporting.fn_company_advanced_metrics to exclude loot entry notes from unlinked_entry_notes
create or replace function reporting.fn_company_advanced_metrics(
    p_company_id uuid,
    p_from date,
    p_to date
)
returns table(
    row_kind text,
    period_label text,
    written_count int,
    total_notes int,
    on_time_notes int,
    late_notes int,
    duplicate_notes int,
    entry_notes int,
    unlinked_entry_notes int,
    memo_items_efficiency_index numeric,
    time_stability_index numeric,
    recurrence_rate_index numeric,
    mechanism_efficiency_index numeric,
    overall_general_efficiency_index numeric
)
language sql
stable
as $function$
with scoped_notes as (
    select n.id,
           n.note_type_id,
           n.writing_date,
           n.arrival_at,
           nt.code as note_type_code,
           coalesce(n.is_loot, false) as is_loot,
           coalesce(q.inactive_count, 0)::int as inactive_count
    from core.notes n
    join core.note_types nt on nt.id = n.note_type_id
    left join (
        select n2.id as note_id,
               count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::int as inactive_count
        from core.notes n2
        join core.note_type_fields ntf
          on ntf.note_type_id = n2.note_type_id
         and ntf.is_active
         and not ntf.is_deleted
        join core.note_fields nf
          on nf.id = ntf.field_id
         and nf.is_active
         and not nf.is_deleted
        left join core.note_field_values nfv
          on nfv.note_id = n2.id
         and nfv.field_id = ntf.field_id
        group by n2.id
    ) q on q.note_id = n.id
    where n.company_id = p_company_id
      and n.is_deleted = false
      and n.arrival_at::date between p_from and p_to
),
item_values as (
    select
        count(nfv.field_id)::int as total_items,
        count(nfv.field_id) filter (where nfv.field_value between 2 and 6)::int as error_items
    from scoped_notes s
    join core.note_type_fields ntf
      on ntf.note_type_id = s.note_type_id
     and ntf.is_active
     and not ntf.is_deleted
    join core.note_fields nf
      on nf.id = ntf.field_id
     and nf.is_active
     and not nf.is_deleted
    left join core.note_field_values nfv
      on nfv.note_id = s.id
     and nfv.field_id = ntf.field_id
),
aggregate_values as (
    select
        count(*)::int as total_notes,
        count(*) filter (
            where writing_date is not null
              and arrival_at::date >= writing_date
              and arrival_at::date - writing_date between 0 and 5
        )::int as on_time_notes,
        count(*) filter (
            where writing_date is not null
              and arrival_at::date - writing_date > 5
        )::int as late_notes,
        count(*) filter (
            where inactive_count = 0
        )::int as clean_notes,
        count(*) filter (where note_type_code = 'ENTRY')::int as entry_notes,
        count(*) filter (
            where note_type_code = 'ENTRY'
              and not scoped_notes.is_loot
              and not exists (
                  select 1 from core.note_sources s
                  where s.note_id = scoped_notes.id and s.source_type = 'LOOT'
              )
              and not exists (
                  select 1
                  from core.v_note_links_compat l
                  where l.source_note_id = scoped_notes.id
                     or l.related_note_id = scoped_notes.id
              )
        )::int as unlinked_entry_notes
    from scoped_notes
),
duplicate_values as (
    select count(*)::int as duplicate_notes
    from core.duplicate_incidents di
    join core.notes duplicate_note
      on duplicate_note.id = coalesce(di.created_note_id, di.matched_note_id)
    where di.company_id = p_company_id
      and duplicate_note.is_deleted = false
      and duplicate_note.arrival_at::date between p_from and p_to
)
select
    'metrics'::text as row_kind,
    null::text as period_label,
    0::int,
    a.total_notes,
    a.on_time_notes,
    a.late_notes,
    d.duplicate_notes,
    a.entry_notes,
    a.unlinked_entry_notes,
    round(coalesce(
        (iv.total_items - iv.error_items)::numeric * 100 / nullif(iv.total_items, 0),
        0
    ), 2),
    round(coalesce(
        (a.on_time_notes::numeric * 0.9 + a.late_notes::numeric * 0.1)
        * 100 / nullif(a.total_notes, 0), 0), 2),
    round(coalesce(d.duplicate_notes::numeric * 100 / nullif(a.total_notes, 0), 0), 2),
    round(coalesce(
        (a.entry_notes - a.unlinked_entry_notes)::numeric * 100 / nullif(a.entry_notes, 0),
        0
    ), 2),
    round(coalesce(
        (
            (iv.total_items - iv.error_items)::numeric * 100 / nullif(iv.total_items, 0)
            + (a.entry_notes - a.unlinked_entry_notes)::numeric * 100 / nullif(a.entry_notes, 0)
        ) / 3.0,
        0
    ), 2)
from aggregate_values a
cross join item_values iv
cross join duplicate_values d;
$function$;

-- 3. Update reporting.fn_company_metrics_v2 to include loot in compliant entry notes
create or replace function reporting.fn_company_metrics_v2(
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
           n.writing_date,
           n.arrival_at,
           coalesce(n.is_loot, false) as is_loot
    from core.notes n
    where n.company_id = p_company_id
      and not n.is_deleted
      and n.arrival_at between p_from and p_to
),
note_quality as (
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
    left join reporting.vw_note_indicator_facts_v2 f on f.note_id = n.id
    group by n.id
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
        count(*) filter (
            where q.expected_items > 0
              and q.unreviewed_items = 0
              and q.error_items = 0
              and q.applicable_reviewed_items > 0
        )::int as clean_reviewed_notes,
        count(*) filter (where q.error_items > 0)::int as error_notes,
        count(*) filter (where q.unreviewed_items > 0)::int as incomplete_review_notes
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
    q.clean_reviewed_notes,
    q.error_notes,
    q.incomplete_review_notes,
    t.time_eligible_notes,
    t.on_time_notes,
    t.late_notes,
    t.late_6_to_10_notes,
    t.late_11_to_20_notes,
    t.late_over_20_notes,
    t.missing_writing_date_notes,
    t.invalid_date_order_notes,
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

-- 4. Update reporting.fn_company_month_series_v4 to forward p_date_basis
create or replace function reporting.fn_company_month_series_v4(
    p_company_id uuid,
    p_from date,
    p_to date,
    p_entry_cutoff_date date default null::date,
    p_date_basis text default 'WritingDate'::text
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
cross join lateral reporting.fn_company_metrics_snapshot_v4(
    p_company_id,
    b.period_from,
    b.period_to,
    p_entry_cutoff_date,
    p_date_basis
) m;
$function$;

-- 5. Reconcile existing is_loot flags with note_sources
update core.notes n
set is_loot = true
from core.note_sources s
where s.note_id = n.id and s.source_type = 'LOOT' and not coalesce(n.is_loot, false);

update core.notes n
set is_loot = false
from core.note_sources s
where s.note_id = n.id and s.source_type <> 'LOOT' and n.is_loot;

-- 6. Set database settings version
insert into system.database_settings(singleton, database_version, compatible_application_version, last_updated_at)
values (true, '070', '2.0.0', now())
on conflict (singleton) do update set
    database_version = excluded.database_version,
    compatible_application_version = excluded.compatible_application_version,
    last_updated_at = excluded.last_updated_at;
