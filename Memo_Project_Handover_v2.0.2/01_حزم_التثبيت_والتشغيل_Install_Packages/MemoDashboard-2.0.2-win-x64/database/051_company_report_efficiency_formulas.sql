-- Migration 051: rework Company Detailed Report efficiency formulas.
--  * memo_accuracy (memo-level)  -> memo_items_efficiency_index (item/field-level).
--  * mechanism_efficiency        -> linked-entry ratio (was unlinked ratio; inflated the old tri-average).
--  * overall efficiency          -> (manual form + items + mechanism) / 3 with form = 0 as DB default;
--                                   the manual value is entered at report time and blended in C#.
--  * soft-deleted memos are excluded (is_deleted = false) in scoped_notes, so every block below
--    (aggregates, item totals, links, written months) already ignores them.
-- This migration is additive and must not modify migrations 001-050.

-- Column names of the returns table changed (memo_accuracy -> memo_items_efficiency_index);
-- PostgreSQL forbids changing the return type via CREATE OR REPLACE (42P13), so drop first.
drop function if exists reporting.fn_company_advanced_metrics(uuid, date, date);

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
              and not exists (
                  select 1
                  from core.note_links l
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
cross join duplicate_values d
cross join item_values iv

union all

select
    'written_month'::text,
    to_char(months.month_start, 'YYYY/MM'),
    count(n.id)::int,
    null::int,
    null::int,
    null::int,
    null::int,
    null::int,
    null::int,
    null::numeric,
    null::numeric,
    null::numeric,
    null::numeric,
    null::numeric
from generate_series(
    date_trunc('month', p_from::timestamp),
    date_trunc('month', p_to::timestamp),
    interval '1 month'
) as months(month_start)
left join core.notes n
    on n.company_id = p_company_id
   and n.is_deleted = false
   and n.writing_date >= months.month_start::date
   and n.writing_date < (months.month_start + interval '1 month')::date
group by months.month_start
order by row_kind, period_label;
$function$;