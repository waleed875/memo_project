--
-- PostgreSQL database dump
--

\restrict giPczMLaYjNZF6NizZJSGuSrWHVfnsNVmZpOAeQeUau7hrVhMNS02Gf9zNV9l1l

-- Dumped from database version 18.2
-- Dumped by pg_dump version 18.2

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: audit; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA audit;


--
-- Name: auth; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA auth;


--
-- Name: common; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA common;


--
-- Name: core; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA core;


--
-- Name: reporting; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA reporting;


--
-- Name: system; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA system;


--
-- Name: updates; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA updates;


--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


--
-- Name: set_updated_at(); Type: FUNCTION; Schema: common; Owner: -
--

CREATE FUNCTION common.set_updated_at() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
    new.updated_at = now();
    return new;
end;
$$;


--
-- Name: try_note_number_value(text); Type: FUNCTION; Schema: common; Owner: -
--

CREATE FUNCTION common.try_note_number_value(input text) RETURNS bigint
    LANGUAGE plpgsql IMMUTABLE
    AS $_$
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
$_$;


--
-- Name: protect_company_soft_delete(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.protect_company_soft_delete() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
    if new.is_deleted and not old.is_deleted and (
        exists(select 1 from core.notes n where n.company_id = old.id and not n.is_deleted)
        or exists(select 1 from core.factories f where f.company_id = old.id and not f.is_deleted)
    ) then
        raise exception 'لا يمكن حذف شركة مرتبطة بمذكرات أو مصانع';
    end if;

    return new;
end;
$$;


--
-- Name: protect_factory_with_production_lines(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.protect_factory_with_production_lines() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
    if exists (select 1 from core.production_lines pl where pl.factory_id = old.id and not pl.is_deleted)
       and (new.company_id is distinct from old.company_id or (new.is_deleted and not old.is_deleted)) then
        raise exception 'A factory with production lines cannot be moved or deleted.';
    end if;
    return new;
end;
$$;


--
-- Name: protect_used_factory_relation(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.protect_used_factory_relation() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
    if exists (
        select 1
        from core.notes n
        where n.factory_id = old.id
          and not n.is_deleted
    ) and (
        new.company_id is distinct from old.company_id
        or (new.is_deleted and not old.is_deleted)
    ) then
        raise exception 'لا يمكن نقل أو حذف مصنع مستخدم في مذكرات';
    end if;

    return new;
end;
$$;


--
-- Name: protect_used_production_line(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.protect_used_production_line() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
    if exists (select 1 from core.notes n where n.production_line_id = old.id and not n.is_deleted)
       and (new.factory_id is distinct from old.factory_id or (new.is_deleted and not old.is_deleted)) then
        raise exception 'A production line used by notes cannot be moved or deleted.';
    end if;
    return new;
end;
$$;


--
-- Name: sync_legacy_note_link_to_source(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.sync_legacy_note_link_to_source() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
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
$$;


--
-- Name: sync_source_ref_to_legacy_note_link(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.sync_source_ref_to_legacy_note_link() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
declare
    entry_note_id uuid;
begin
    select s.note_id into entry_note_id
    from core.note_sources s where s.id = coalesce(new.source_id, old.source_id);
    if tg_op = 'INSERT' then
        insert into core.note_links(source_note_id, related_note_id, created_by)
        select new_source.note_id, new.exit_note_id, new_source.created_by
        from core.note_sources new_source
        where new_source.id = new.source_id
          and not exists (
              select 1 from core.note_links l
              where (l.source_note_id = new_source.note_id and l.related_note_id = new.exit_note_id)
                 or (l.source_note_id = new.exit_note_id and l.related_note_id = new_source.note_id));
        return new;
    end if;
    delete from core.note_links
    where (source_note_id = entry_note_id and related_note_id = old.exit_note_id)
       or (source_note_id = old.exit_note_id and related_note_id = entry_note_id);
    return old;
end;
$$;


--
-- Name: validate_note_factory_company(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_factory_company() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
    if new.factory_id is not null and not exists (
        select 1
        from core.factories f
        where f.id = new.factory_id
          and f.company_id = new.company_id
          and f.is_active = true
          and f.is_deleted = false
    ) then
        raise exception 'المصنع المختار يجب أن يكون نشطاً ويتبع للشركة المحددة';
    end if;

    return new;
end;
$$;


--
-- Name: validate_note_factory_production_line(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_factory_production_line() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
begin
    if new.production_line_id is null then
        return new;
    end if;

    if new.factory_id is null then
        raise exception 'لا يمكن اختيار خط إنتاج دون تحديد مصنع.';
    end if;

    if tg_op = 'UPDATE'
       and new.production_line_id is not distinct from old.production_line_id
       and new.factory_id is not distinct from old.factory_id then
        return new;
    end if;

    if tg_op = 'INSERT' and exists (
        select 1
        from core.notes existing
        where existing.id = new.id
          and existing.production_line_id is not distinct from new.production_line_id
          and existing.factory_id is not distinct from new.factory_id
    ) then
        return new;
    end if;

    if not exists (
        select 1
        from core.production_lines pl
        where pl.id = new.production_line_id
          and pl.factory_id = new.factory_id
          and pl.is_active
          and not pl.is_deleted
    ) then
        raise exception 'خط الإنتاج المحدد غير فعال أو لا يتبع للمصنع المختار.';
    end if;

    return new;
end;
$$;


--
-- Name: validate_note_field_value(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_field_value() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
declare
    v_note_type_id uuid;
begin
    select note_type_id into v_note_type_id from core.notes where id = new.note_id;
    if v_note_type_id is null then
        raise exception 'المذكرة غير موجودة.';
    end if;

    if not exists (
        select 1
        from core.note_type_fields ntf
        join core.note_fields nf on nf.id = ntf.field_id
        where ntf.note_type_id = v_note_type_id
          and ntf.field_id = new.field_id
          and ntf.is_active and not ntf.is_deleted
          and nf.is_active and not nf.is_deleted
    ) then
        raise exception 'المؤشر غير فعال أو غير مرتبط بنوع المذكرة.';
    end if;
    return new;
end;
$$;


--
-- Name: validate_note_link(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_link() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
declare
    source_company uuid;
    related_company uuid;
    source_type varchar(50);
    related_type varchar(50);
begin
    select n.company_id, nt.code into source_company, source_type
    from core.notes n join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.source_note_id and not n.is_deleted;
    select n.company_id, nt.code into related_company, related_type
    from core.notes n join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.related_note_id and not n.is_deleted;
    if source_company is null or related_company is null then
        raise exception using errcode = '23503', message = 'Both notes in a link must exist and be active.';
    end if;
    if source_company <> related_company then
        raise exception using errcode = '23514', message = 'Linked notes must belong to the same company.';
    end if;
    if not ((source_type = 'ENTRY' and related_type = 'EXIT')
         or (source_type = 'EXIT' and related_type = 'ENTRY')) then
        raise exception using errcode = '23514', message = 'A link is allowed only between ENTRY and EXIT notes.';
    end if;
    return new;
end;
$$;


--
-- Name: validate_note_loot_invoice_flags(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_loot_invoice_flags() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
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


--
-- Name: validate_note_source(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_source() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
declare
    note_company uuid;
    note_type text;
begin
    select n.company_id, nt.code into note_company, note_type
    from core.notes n
    join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.note_id and not n.is_deleted;
    if note_company is null or note_type <> 'ENTRY' then
        raise exception using errcode = '23514', message = 'A source can only be assigned to an active ENTRY note.';
    end if;
    return new;
end;
$$;


--
-- Name: validate_note_source_exit_ref(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_source_exit_ref() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
declare
    source_note_id uuid;
    source_company uuid;
    source_type text;
    exit_company uuid;
    exit_type text;
begin
    select s.note_id, entry.company_id, s.source_type
      into source_note_id, source_company, source_type
    from core.note_sources s
    join core.notes entry on entry.id = s.note_id and not entry.is_deleted
    where s.id = new.source_id;
    select n.company_id, nt.code into exit_company, exit_type
    from core.notes n
    join core.note_types nt on nt.id = n.note_type_id
    where n.id = new.exit_note_id and not n.is_deleted;
    if source_note_id is null or source_type <> 'EXIT' or exit_company is null or exit_type <> 'EXIT' then
        raise exception using errcode = '23514', message = 'Exit references require an EXIT source and an active EXIT note.';
    end if;
    if source_company <> exit_company then
        raise exception using errcode = '23514', message = 'Linked source notes must belong to the same company.';
    end if;
    if source_note_id = new.exit_note_id then
        raise exception using errcode = '23514', message = 'An ENTRY note cannot reference itself.';
    end if;
    return new;
end;
$$;


--
-- Name: validate_note_source_payload(); Type: FUNCTION; Schema: core; Owner: -
--

CREATE FUNCTION core.validate_note_source_payload() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
declare
    expected_type text;
    actual_type text;
begin
    select source_type into actual_type from core.note_sources where id = new.source_id;
    expected_type := case tg_table_name when 'note_source_invoices' then 'INVOICE' else 'LOOT' end;
    if actual_type is distinct from expected_type then
        raise exception using errcode = '23514', message = 'The source payload does not match the note source type.';
    end if;
    return new;
end;
$$;


--
-- Name: fn_company_advanced_metrics(uuid, date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_advanced_metrics(p_company_id uuid, p_from date, p_to date) RETURNS TABLE(row_kind text, period_label text, written_count integer, total_notes integer, on_time_notes integer, late_notes integer, duplicate_notes integer, entry_notes integer, unlinked_entry_notes integer, memo_items_efficiency_index numeric, time_stability_index numeric, recurrence_rate_index numeric, mechanism_efficiency_index numeric, overall_general_efficiency_index numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: fn_company_errors(uuid, date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_errors(p_company_id uuid, p_from date, p_to date) RETURNS TABLE(note_id uuid, note_number character varying, writing_date date, total_errors integer, total_unknown integer)
    LANGUAGE sql STABLE
    AS $$
    select note_id, note_number, writing_date,
           count(field_id) filter (where field_value between 2 and 6)::int,
           count(field_id) filter (where field_value = 0)::int
    from reporting.vw_note_field_details
    where company_id = p_company_id and writing_date between p_from and p_to
    group by note_id, note_number, writing_date
    having count(field_id) filter (where field_value between 2 and 6) > 0
    order by writing_date desc;
$$;


--
-- Name: fn_company_metrics_snapshot_v2(uuid, date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_metrics_snapshot_v2(p_company_id uuid, p_from date, p_to date) RETURNS TABLE(total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: FUNCTION fn_company_metrics_snapshot_v2(p_company_id uuid, p_from date, p_to date); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_metrics_snapshot_v2(p_company_id uuid, p_from date, p_to date) IS 'Canonical company metrics consumer snapshot. Time compliance is on-time divided by eligible notes and remains NULL when there are no eligible notes.';


--
-- Name: fn_company_metrics_snapshot_v3(uuid, date, date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_metrics_snapshot_v3(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date DEFAULT NULL::date) RETURNS TABLE(total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, quality_eligible_note_count integer, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, all_not_applicable_note_count integer, no_expected_items_note_count integer, clean_reviewed_note_percent numeric, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
    select * from reporting.fn_company_metrics_snapshot_v4(p_company_id, p_from, p_to, p_entry_cutoff_date, 'ArrivalDate');
$$;


--
-- Name: FUNCTION fn_company_metrics_snapshot_v3(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_metrics_snapshot_v3(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date) IS 'Canonical v3 company analytics. Error takes precedence over incomplete; all-N/A and no-expected notes are separate; quality denominator is clean plus error. arrival_at scopes the period and created_at cutoff is inclusive through the supplied day.';


--
-- Name: fn_company_metrics_snapshot_v3_filtered(uuid, date, date, uuid, uuid, uuid, text, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_metrics_snapshot_v3_filtered(p_company_id uuid, p_from date, p_to date, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status text, p_entry_cutoff_date date DEFAULT NULL::date) RETURNS TABLE(total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, quality_eligible_note_count integer, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, all_not_applicable_note_count integer, no_expected_items_note_count integer, clean_reviewed_note_percent numeric, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
    select * from reporting.fn_company_metrics_snapshot_v4_filtered(p_company_id, p_from, p_to, p_factory_id, p_production_line_id, p_note_type_id, p_status, p_entry_cutoff_date, 'ArrivalDate');
$$;


--
-- Name: FUNCTION fn_company_metrics_snapshot_v3_filtered(p_company_id uuid, p_from date, p_to date, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status text, p_entry_cutoff_date date); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_metrics_snapshot_v3_filtered(p_company_id uuid, p_from date, p_to date, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status text, p_entry_cutoff_date date) IS 'Filtered canonical v3 company analytics. Factory, production line, note type, status, arrival period, company, and created_at cutoff are applied before all KPI calculations.';


--
-- Name: fn_company_metrics_snapshot_v4(uuid, date, date, date, text); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_metrics_snapshot_v4(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date DEFAULT NULL::date, p_date_basis text DEFAULT 'WritingDate'::text) RETURNS TABLE(total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, quality_eligible_note_count integer, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, all_not_applicable_note_count integer, no_expected_items_note_count integer, clean_reviewed_note_percent numeric, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: FUNCTION fn_company_metrics_snapshot_v4(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date, p_date_basis text); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_metrics_snapshot_v4(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date, p_date_basis text) IS 'Canonical company analytics with explicit WritingDate or ArrivalDate scope and arrival-date cutoff.';


--
-- Name: fn_company_metrics_snapshot_v4_filtered(uuid, date, date, uuid, uuid, uuid, text, date, text); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_metrics_snapshot_v4_filtered(p_company_id uuid, p_from date, p_to date, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status text, p_entry_cutoff_date date DEFAULT NULL::date, p_date_basis text DEFAULT 'WritingDate'::text) RETURNS TABLE(total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, quality_eligible_note_count integer, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, all_not_applicable_note_count integer, no_expected_items_note_count integer, clean_reviewed_note_percent numeric, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: FUNCTION fn_company_metrics_snapshot_v4_filtered(p_company_id uuid, p_from date, p_to date, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status text, p_entry_cutoff_date date, p_date_basis text); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_metrics_snapshot_v4_filtered(p_company_id uuid, p_from date, p_to date, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status text, p_entry_cutoff_date date, p_date_basis text) IS 'Filtered canonical company analytics with explicit WritingDate or ArrivalDate scope and arrival-date cutoff.';


--
-- Name: fn_company_metrics_v2(uuid, date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_metrics_v2(p_company_id uuid, p_from date, p_to date) RETURNS TABLE(total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: FUNCTION fn_company_metrics_v2(p_company_id uuid, p_from date, p_to date); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_metrics_v2(p_company_id uuid, p_from date, p_to date) IS 'Canonical company metrics for a non-deleted arrival-date cohort. Rates are NULL when their denominator is zero. Links describe current state and require an active EXIT counterpart.';


--
-- Name: fn_company_month_series_v2(uuid, date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_month_series_v2(p_company_id uuid, p_from date, p_to date) RETURNS TABLE(month_start date, period_label text, total_notes integer, expected_items integer, reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, on_time_notes integer, late_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
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
    m.correct_items,
    m.error_items,
    m.not_applicable_items,
    m.review_coverage_percentage,
    m.item_quality_percentage,
    m.on_time_notes,
    m.late_notes,
    m.missing_writing_date_notes,
    m.invalid_date_order_notes,
    round(m.on_time_notes::numeric * 100 / nullif(m.time_eligible_notes, 0), 2),
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
cross join lateral reporting.fn_company_metrics_v2(
    p_company_id,
    b.period_from,
    b.period_to
) m
order by b.month_start;
$$;


--
-- Name: FUNCTION fn_company_month_series_v2(p_company_id uuid, p_from date, p_to date); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_month_series_v2(p_company_id uuid, p_from date, p_to date) IS 'Monthly arrival-date series backed by fn_company_metrics_v2; first and last months are clipped to the requested inclusive range.';


--
-- Name: fn_company_month_series_v3(uuid, date, date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_month_series_v3(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date DEFAULT NULL::date) RETURNS TABLE(month_start date, period_label text, total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, quality_eligible_note_count integer, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, all_not_applicable_note_count integer, no_expected_items_note_count integer, clean_reviewed_note_percent numeric, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: FUNCTION fn_company_month_series_v3(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_month_series_v3(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date) IS 'Six-month arrival-date series backed by the v3 snapshot. Every month uses the same optional inclusive created_at cutoff and the same nullable denominators.';


--
-- Name: fn_company_month_series_v4(uuid, date, date, date, text); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_company_month_series_v4(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date DEFAULT NULL::date, p_date_basis text DEFAULT 'WritingDate'::text) RETURNS TABLE(month_start date, period_label text, total_notes integer, expected_items integer, reviewed_items integer, unreviewed_items integer, applicable_reviewed_items integer, correct_items integer, error_items integer, not_applicable_items integer, review_coverage_percentage numeric, item_quality_percentage numeric, errors_per_100_checked numeric, quality_eligible_note_count integer, clean_reviewed_notes integer, error_notes integer, incomplete_review_notes integer, all_not_applicable_note_count integer, no_expected_items_note_count integer, clean_reviewed_note_percent numeric, time_eligible_notes integer, on_time_notes integer, late_notes integer, late_6_to_10_notes integer, late_11_to_20_notes integer, late_over_20_notes integer, missing_writing_date_notes integer, invalid_date_order_notes integer, time_compliance_percentage numeric, average_late_days numeric, median_late_days numeric, p90_late_days numeric, maximum_late_days integer, average_excess_delay_days numeric, duplicate_number_incidents integer, company_resend_incidents integer, duplicate_incidents integer, affected_notes integer, incidents_per_100_notes numeric, affected_note_rate_percentage numeric, duplicate_free_percentage numeric, entry_notes integer, linked_entry_notes integer, unlinked_entry_notes integer, link_efficiency_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: FUNCTION fn_company_month_series_v4(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date, p_date_basis text); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_company_month_series_v4(p_company_id uuid, p_from date, p_to date, p_entry_cutoff_date date, p_date_basis text) IS 'Company monthly series backed by the v4 date-basis-aware snapshot.';


--
-- Name: fn_dashboard_snapshot(date, date, uuid, uuid, uuid, uuid, character varying, text); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_dashboard_snapshot(p_from date DEFAULT NULL::date, p_to date DEFAULT NULL::date, p_company_id uuid DEFAULT NULL::uuid, p_factory_id uuid DEFAULT NULL::uuid, p_production_line_id uuid DEFAULT NULL::uuid, p_note_type_id uuid DEFAULT NULL::uuid, p_status character varying DEFAULT NULL::character varying, p_timezone text DEFAULT 'Asia/Damascus'::text) RETURNS jsonb
    LANGUAGE sql STABLE
    AS $$
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
duplicate_references as (
    select d.*,
           case d.classification
               when 'COMPANY_DUPLICATE_NUMBER' then d.created_note_id
               when 'COMPANY_RESEND' then d.matched_note_id
               else null
           end as reference_note_id
    from core.duplicate_incidents d
),
scoped_duplicates as (
    select d.*
    from duplicate_references d
    join scoped_notes n on n.id = d.reference_note_id
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


--
-- Name: FUNCTION fn_dashboard_snapshot(p_from date, p_to date, p_company_id uuid, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status character varying, p_timezone text); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_dashboard_snapshot(p_from date, p_to date, p_company_id uuid, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status character varying, p_timezone text) IS 'The dashboard JSON contract. Duplicate-number incidents use created_note_id; company resends use matched_note_id. All note counts use non-deleted arrival-date scope and supplied filters.';


--
-- Name: fn_dashboard_snapshot_v2(date, date, uuid, uuid, uuid, uuid, character varying, text, text, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_dashboard_snapshot_v2(p_from date DEFAULT NULL::date, p_to date DEFAULT NULL::date, p_company_id uuid DEFAULT NULL::uuid, p_factory_id uuid DEFAULT NULL::uuid, p_production_line_id uuid DEFAULT NULL::uuid, p_note_type_id uuid DEFAULT NULL::uuid, p_status character varying DEFAULT NULL::character varying, p_timezone text DEFAULT 'Asia/Damascus'::text, p_date_basis text DEFAULT 'WritingDate'::text, p_arrival_cutoff_date date DEFAULT NULL::date) RETURNS jsonb
    LANGUAGE sql STABLE
    AS $$
with dashboard_clock as (
    select (now() at time zone p_timezone)::date as today
),
scoped_notes as (
    select n.id, n.note_number, n.company_id, n.factory_id, n.production_line_id,
           n.note_type_id, n.status, n.writing_date, n.arrival_at, n.created_at,
           n.approved_at, n.review_started_at,
           case when lower(coalesce(p_date_basis, 'WritingDate')) in ('arrivaldate', 'arrival_date', 'arrival') then n.arrival_at::date else n.writing_date end as period_date
    from core.notes n
    where not n.is_deleted
      and (p_from is null or (case when lower(coalesce(p_date_basis, 'WritingDate')) in ('arrivaldate', 'arrival_date', 'arrival') then n.arrival_at::date else n.writing_date end) >= p_from)
      and (p_to is null or (case when lower(coalesce(p_date_basis, 'WritingDate')) in ('arrivaldate', 'arrival_date', 'arrival') then n.arrival_at::date else n.writing_date end) <= p_to)
       and (p_arrival_cutoff_date is null or n.arrival_at::date <= p_arrival_cutoff_date)
      and (p_company_id is null or n.company_id = p_company_id)
      and (p_factory_id is null or n.factory_id = p_factory_id)
      and (p_production_line_id is null or n.production_line_id = p_production_line_id)
      and (p_note_type_id is null or n.note_type_id = p_note_type_id)
      and (p_status is null or n.status = p_status)
),
duplicate_references as (
    select d.*,
           case d.classification
               when 'COMPANY_DUPLICATE_NUMBER' then d.created_note_id
               when 'COMPANY_RESEND' then d.matched_note_id
               else null
           end as reference_note_id
    from core.duplicate_incidents d
),
scoped_duplicates as (
    select d.*
    from duplicate_references d
    join scoped_notes n on n.id = d.reference_note_id
),
summary as (
    select
        count(*)::int as total_notes,
        count(*) filter (where n.period_date = clock.today)::int as today_notes,
        count(*) filter (where date_trunc('month', n.period_date::timestamp) = date_trunc('month', clock.today::timestamp))::int as current_month_notes,
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
              left join scoped_notes n on n.period_date >= month_start::date and n.period_date < (month_start + interval '1 month')::date
              group by month_start) x), '[]'::jsonb),
    'notesByStatus', coalesce((select jsonb_agg(jsonb_build_object('label', x.label, 'value', x.value) order by x.value desc, x.label)
        from (select status as label, count(*)::int as value from scoped_notes group by status) x), '[]'::jsonb),
    'duplicatesByType', coalesce((select jsonb_agg(jsonb_build_object('label', x.label, 'value', x.value) order by x.label)
        from (select classification as label, count(*)::int as value from scoped_duplicates group by classification) x), '[]'::jsonb),
    'recentNotes', coalesce((select jsonb_agg(jsonb_build_object('noteNumber', x.note_number, 'companyName', x.company_name, 'noteTypeName', x.note_type_name, 'status', x.status, 'arrivalAt', x.arrival_at) order by x.period_date desc, x.created_at desc)
        from (select n.note_number, c.name as company_name, nt.name_ar as note_type_name, n.status, n.arrival_at, n.created_at, n.period_date from scoped_notes n join core.companies c on c.id = n.company_id join core.note_types nt on nt.id = n.note_type_id order by n.arrival_at desc, n.created_at desc limit 10) x), '[]'::jsonb)
)
from summary;
$$;


--
-- Name: FUNCTION fn_dashboard_snapshot_v2(p_from date, p_to date, p_company_id uuid, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status character varying, p_timezone text, p_date_basis text, p_arrival_cutoff_date date); Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON FUNCTION reporting.fn_dashboard_snapshot_v2(p_from date, p_to date, p_company_id uuid, p_factory_id uuid, p_production_line_id uuid, p_note_type_id uuid, p_status character varying, p_timezone text, p_date_basis text, p_arrival_cutoff_date date) IS 'Dashboard snapshot with explicit WritingDate/ArrivalDate basis and an independent arrival_at cutoff.';


--
-- Name: fn_note_accuracy(uuid); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_note_accuracy(p_note_id uuid) RETURNS TABLE(total_fields integer, active_count integer, inactive_count integer, unknown_count integer, accuracy_percentage numeric, error_percentage numeric)
    LANGUAGE sql STABLE
    AS $$
    select total_fields, active_count, inactive_count, unknown_count, accuracy_percentage, error_percentage
    from reporting.vw_note_details where note_id = p_note_id;
$$;


--
-- Name: fn_note_number_gaps(uuid, uuid, integer); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_note_number_gaps(p_company_id uuid, p_note_type_id uuid, p_note_year integer) RETURNS TABLE(note_number_value bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: fn_notes_summary(date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_notes_summary(p_from date, p_to date) RETURNS TABLE(total_notes integer, total_errors integer, total_unknown integer, average_accuracy numeric)
    LANGUAGE sql STABLE
    AS $$
    select count(distinct note_id)::int,
           count(field_id) filter (where field_value between 2 and 6)::int,
           count(field_id) filter (where field_value = 0)::int,
           coalesce(round(count(field_id) filter (where field_value = 1)::numeric * 100 / nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0)
    from reporting.vw_note_field_details
    where writing_date between p_from and p_to;
$$;


--
-- Name: fn_top_error_fields(date, date, integer); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_top_error_fields(p_from date, p_to date, p_limit integer) RETURNS TABLE(field_id uuid, field_name character varying, total_errors integer, total_unknown integer, error_rate numeric)
    LANGUAGE sql STABLE
    AS $$
    select field_id, field_name,
           count(field_id) filter (where field_value between 2 and 6)::int as total_errors,
           count(field_id) filter (where field_value = 0)::int as total_unknown,
           coalesce(round(count(field_id) filter (where field_value between 2 and 6)::numeric * 100 / nullif(count(field_id) filter (where field_value <> 0), 0), 2), 0) as error_rate
    from reporting.vw_note_field_details
    where writing_date between p_from and p_to and field_value is not null
    group by field_id, field_name
    order by 3 desc, 4 desc
    limit p_limit;
$$;


--
-- Name: fn_user_productivity(date, date); Type: FUNCTION; Schema: reporting; Owner: -
--

CREATE FUNCTION reporting.fn_user_productivity(p_from date, p_to date) RETURNS TABLE(user_id uuid, full_name character varying, created_notes integer, updated_notes integer, approved_notes integer, last_activity timestamp without time zone)
    LANGUAGE sql STABLE
    AS $$
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


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: logs; Type: TABLE; Schema: audit; Owner: -
--

CREATE TABLE audit.logs (
    id bigint NOT NULL,
    user_id uuid,
    action_type character varying(100) NOT NULL,
    schema_name character varying(100),
    table_name character varying(100),
    record_id uuid,
    description text,
    old_value jsonb,
    new_value jsonb,
    created_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: logs_id_seq; Type: SEQUENCE; Schema: audit; Owner: -
--

CREATE SEQUENCE audit.logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: logs_id_seq; Type: SEQUENCE OWNED BY; Schema: audit; Owner: -
--

ALTER SEQUENCE audit.logs_id_seq OWNED BY audit.logs.id;


--
-- Name: roles; Type: TABLE; Schema: auth; Owner: -
--

CREATE TABLE auth.roles (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(50) NOT NULL,
    name_ar character varying(100) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: auth; Owner: -
--

CREATE TABLE auth.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    username character varying(100) NOT NULL,
    password_hash text NOT NULL,
    full_name character varying(255) NOT NULL,
    role_id uuid NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    must_change_password boolean DEFAULT false NOT NULL
);


--
-- Name: companies; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.companies (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: duplicate_incidents; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.duplicate_incidents (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    classification character varying(50) NOT NULL,
    matched_note_id uuid NOT NULL,
    created_note_id uuid,
    company_id uuid NOT NULL,
    factory_id uuid,
    production_line_id uuid,
    note_type_id uuid NOT NULL,
    note_number character varying(100) NOT NULL,
    note_number_normalized character varying(100) NOT NULL,
    writing_date date,
    decided_by uuid,
    metadata jsonb,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_duplicate_incident_classification CHECK (((classification)::text = ANY (ARRAY[('COMPANY_DUPLICATE_NUMBER'::character varying)::text, ('COMPANY_RESEND'::character varying)::text]))),
    CONSTRAINT ck_duplicate_incident_created_note CHECK (((((classification)::text = 'COMPANY_DUPLICATE_NUMBER'::text) AND (created_note_id IS NOT NULL)) OR (((classification)::text = 'COMPANY_RESEND'::text) AND (created_note_id IS NULL))))
);


--
-- Name: duplicate_incidents_archive; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.duplicate_incidents_archive (
    id uuid NOT NULL,
    classification character varying(50) NOT NULL,
    matched_note_id uuid NOT NULL,
    created_note_id uuid,
    company_id uuid NOT NULL,
    factory_id uuid,
    production_line_id uuid,
    note_type_id uuid NOT NULL,
    note_number character varying(100) NOT NULL,
    note_number_normalized character varying(100) NOT NULL,
    writing_date date,
    decided_by uuid,
    metadata jsonb,
    created_at timestamp without time zone NOT NULL,
    duplicate_of uuid NOT NULL,
    archived_at timestamp without time zone DEFAULT now() NOT NULL,
    reason text NOT NULL
);


--
-- Name: factories; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.factories (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(200) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    company_id uuid
);


--
-- Name: note_attachments; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_attachments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    note_id uuid NOT NULL,
    file_name text NOT NULL,
    content_type character varying(120) NOT NULL,
    file_data bytea NOT NULL,
    file_size bigint DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT clock_timestamp() NOT NULL,
    created_by uuid
);


--
-- Name: note_create_operations; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_create_operations (
    operation_id uuid NOT NULL,
    note_id uuid NOT NULL,
    requested_by uuid NOT NULL,
    fingerprint character varying(64) NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_note_create_operations_fingerprint CHECK (((fingerprint)::text ~ '^[0-9a-f]{64}$'::text))
);


--
-- Name: note_field_values; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_field_values (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    note_id uuid NOT NULL,
    field_id uuid NOT NULL,
    field_value smallint NOT NULL,
    inactive_reason text,
    created_by uuid,
    updated_by uuid,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_note_field_values_value_reason CHECK ((field_value = ANY (ARRAY[0, 1, 2, 3, 4, 5, 6])))
);


--
-- Name: note_fields; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_fields (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(80) NOT NULL,
    name_ar character varying(150) NOT NULL,
    description text,
    default_sort_order integer DEFAULT 0 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: note_links; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_links (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    source_note_id uuid NOT NULL,
    related_note_id uuid NOT NULL,
    created_by uuid,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_note_links_not_self CHECK ((source_note_id <> related_note_id))
);


--
-- Name: note_review_history; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_review_history (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    note_id uuid NOT NULL,
    action character varying(50) NOT NULL,
    reason text,
    user_id uuid,
    created_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: note_source_exit_refs; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_source_exit_refs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    source_id uuid NOT NULL,
    exit_note_id uuid NOT NULL,
    reference_order integer DEFAULT 1 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_note_source_exit_order CHECK ((reference_order > 0))
);


--
-- Name: note_source_invoices; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_source_invoices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    source_id uuid NOT NULL,
    invoice_number character varying(120) NOT NULL,
    invoice_date date,
    destination text,
    attachment_file_name character varying(255),
    attachment_content_type character varying(120),
    attachment_data bytea,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_note_source_invoice_number CHECK ((NULLIF(btrim((invoice_number)::text), ''::text) IS NOT NULL))
);


--
-- Name: note_source_loot; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_source_loot (
    source_id uuid NOT NULL,
    description text NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_note_source_loot_description CHECK ((NULLIF(btrim(description), ''::text) IS NOT NULL))
);


--
-- Name: note_sources; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_sources (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    note_id uuid NOT NULL,
    source_type character varying(20) NOT NULL,
    other_description text,
    created_by uuid,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_note_sources_other_description CHECK ((((source_type)::text <> 'OTHER'::text) OR (NULLIF(btrim(other_description), ''::text) IS NOT NULL))),
    CONSTRAINT ck_note_sources_type CHECK (((source_type)::text = ANY ((ARRAY['EXIT'::character varying, 'INVOICE'::character varying, 'LOOT'::character varying, 'OTHER'::character varying])::text[])))
);


--
-- Name: TABLE note_sources; Type: COMMENT; Schema: core; Owner: -
--

COMMENT ON TABLE core.note_sources IS 'Exclusive source type for an ENTRY note: EXIT references, invoices, loot, or other.';


--
-- Name: note_type_fields; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_type_fields (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    note_type_id uuid NOT NULL,
    field_id uuid NOT NULL,
    sort_order integer NOT NULL,
    is_required boolean DEFAULT true NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: note_types; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.note_types (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    code character varying(50) NOT NULL,
    name_ar character varying(100) NOT NULL,
    description text,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: notes; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.notes (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    note_number character varying(100) NOT NULL,
    note_type_id uuid NOT NULL,
    company_id uuid NOT NULL,
    writing_date date,
    arrival_at date DEFAULT CURRENT_DATE NOT NULL,
    image_path text,
    file_path text,
    notes text,
    status character varying(50) DEFAULT 'DRAFT'::character varying NOT NULL,
    created_by uuid,
    updated_by uuid,
    approved_by uuid,
    approved_at timestamp without time zone,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    attachment_file_name text,
    attachment_content_type character varying(120),
    attachment_data bytea,
    reviewer_id uuid,
    review_started_at timestamp without time zone,
    return_reason text,
    note_number_normalized character varying(100) NOT NULL,
    duplicate_reason text,
    factory_id uuid,
    production_line_id uuid,
    deleted_at timestamp without time zone,
    deleted_by uuid,
    note_number_value bigint,
    note_year integer,
    is_loot boolean DEFAULT false NOT NULL,
    is_invoice boolean DEFAULT false NOT NULL,
    CONSTRAINT ck_notes_normalized_number_not_empty CHECK ((length(TRIM(BOTH FROM note_number_normalized)) > 0)),
    CONSTRAINT ck_notes_status CHECK (((status)::text = ANY (ARRAY[('DRAFT'::character varying)::text, ('COMPLETE'::character varying)::text, ('HAS_ERRORS'::character varying)::text, ('NEEDS_REVIEW'::character varying)::text, ('UNDER_REVIEW'::character varying)::text, ('RETURNED_FOR_CORRECTION'::character varying)::text, ('APPROVED'::character varying)::text])))
);


--
-- Name: COLUMN notes.deleted_at; Type: COMMENT; Schema: core; Owner: -
--

COMMENT ON COLUMN core.notes.deleted_at IS 'UTC timestamp when an ADMIN soft-deleted the memo.';


--
-- Name: COLUMN notes.deleted_by; Type: COMMENT; Schema: core; Owner: -
--

COMMENT ON COLUMN core.notes.deleted_by IS 'ADMIN user that soft-deleted the memo.';


--
-- Name: COLUMN notes.note_number_value; Type: COMMENT; Schema: core; Owner: -
--

COMMENT ON COLUMN core.notes.note_number_value IS 'Positive normalized integer for sequencing and duplicate matching; null means legacy data needs review.';


--
-- Name: COLUMN notes.note_year; Type: COMMENT; Schema: core; Owner: -
--

COMMENT ON COLUMN core.notes.note_year IS 'Calendar year derived only from writing_date; null when writing_date is missing.';


--
-- Name: production_lines; Type: TABLE; Schema: core; Owner: -
--

CREATE TABLE core.production_lines (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    factory_id uuid NOT NULL,
    name character varying(200) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: v_note_links_compat; Type: VIEW; Schema: core; Owner: -
--

CREATE VIEW core.v_note_links_compat AS
 SELECT l.source_note_id,
    l.related_note_id,
    l.created_by,
    l.created_at
   FROM core.note_links l
  WHERE (NOT (EXISTS ( SELECT 1
           FROM (core.note_sources s
             JOIN core.note_source_exit_refs r ON ((r.source_id = s.id)))
          WHERE ((s.note_id =
                CASE
                    WHEN (EXISTS ( SELECT 1
                       FROM (core.note_types t
                         JOIN core.notes n ON ((n.note_type_id = t.id)))
                      WHERE ((n.id = l.source_note_id) AND ((t.code)::text = 'ENTRY'::text)))) THEN l.source_note_id
                    ELSE l.related_note_id
                END) AND (r.exit_note_id =
                CASE
                    WHEN (EXISTS ( SELECT 1
                       FROM (core.note_types t
                         JOIN core.notes n ON ((n.note_type_id = t.id)))
                      WHERE ((n.id = l.source_note_id) AND ((t.code)::text = 'ENTRY'::text)))) THEN l.related_note_id
                    ELSE l.source_note_id
                END)))))
UNION ALL
 SELECT s.note_id AS source_note_id,
    r.exit_note_id AS related_note_id,
    s.created_by,
    r.created_at
   FROM (core.note_sources s
     JOIN core.note_source_exit_refs r ON ((r.source_id = s.id)));


--
-- Name: VIEW v_note_links_compat; Type: COMMENT; Schema: core; Owner: -
--

COMMENT ON VIEW core.v_note_links_compat IS 'Compatibility projection of legacy note_links and the extensible note source model.';


--
-- Name: vw_note_field_details; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_note_field_details AS
 SELECT n.id AS note_id,
    n.note_number,
    c.id AS company_id,
    c.name AS company_name,
    n.factory_id,
    f.name AS factory_name,
    n.production_line_id,
    pl.name AS production_line_name,
    nt.id AS note_type_id,
    nt.name_ar AS note_type_name,
    nf.id AS field_id,
    nf.name_ar AS field_name,
    nfv.field_value,
        CASE nfv.field_value
            WHEN 0 THEN 'لم تتم المراجعة'::text
            WHEN 1 THEN 'صحيح'::text
            WHEN 2 THEN 'غير صحيح'::text
            WHEN 3 THEN 'مفقود'::text
            WHEN 4 THEN 'غير واضح'::text
            WHEN 5 THEN 'غير مكتمل'::text
            WHEN 6 THEN 'لا ينطبق'::text
            ELSE 'غير مدخل'::text
        END AS field_value_label_ar,
    nfv.inactive_reason,
    n.writing_date,
    n.arrival_at,
    n.status,
    n.created_at,
    n.review_started_at,
    n.approved_at
   FROM (((((((core.notes n
     JOIN core.companies c ON ((c.id = n.company_id)))
     JOIN core.note_types nt ON ((nt.id = n.note_type_id)))
     LEFT JOIN core.factories f ON ((f.id = n.factory_id)))
     LEFT JOIN core.production_lines pl ON ((pl.id = n.production_line_id)))
     JOIN core.note_type_fields ntf ON (((ntf.note_type_id = nt.id) AND ntf.is_active AND (NOT ntf.is_deleted))))
     JOIN core.note_fields nf ON (((nf.id = ntf.field_id) AND nf.is_active AND (NOT nf.is_deleted))))
     LEFT JOIN core.note_field_values nfv ON (((nfv.note_id = n.id) AND (nfv.field_id = nf.id))))
  WHERE (n.is_deleted = false);


--
-- Name: vw_company_error_summary; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_company_error_summary AS
 SELECT company_id,
    company_name,
    (count(DISTINCT note_id))::integer AS total_notes,
    (count(field_id))::integer AS total_fields_checked,
    (count(field_id) FILTER (WHERE ((field_value >= 2) AND (field_value <= 6))))::integer AS total_errors,
    (count(field_id) FILTER (WHERE (field_value = 0)))::integer AS total_unknown,
    COALESCE(round((((count(field_id) FILTER (WHERE (field_value = 1)))::numeric * (100)::numeric) / (NULLIF(count(field_id) FILTER (WHERE (field_value <> 0)), 0))::numeric), 2), (0)::numeric) AS average_accuracy,
    COALESCE(round((((count(field_id) FILTER (WHERE ((field_value >= 2) AND (field_value <= 6))))::numeric * (100)::numeric) / (NULLIF(count(field_id) FILTER (WHERE (field_value <> 0)), 0))::numeric), 2), (0)::numeric) AS error_rate
   FROM reporting.vw_note_field_details
  WHERE (field_value IS NOT NULL)
  GROUP BY company_id, company_name;


--
-- Name: vw_daily_notes_summary; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_daily_notes_summary AS
 SELECT COALESCE(n.writing_date, n.arrival_at) AS day,
    (count(n.id))::integer AS total_notes,
    (count(v.field_id) FILTER (WHERE ((v.field_value >= 2) AND (v.field_value <= 6))))::integer AS total_errors,
    (count(v.field_id) FILTER (WHERE (v.field_value = 0)))::integer AS total_unknown,
    COALESCE(round((((count(v.field_id) FILTER (WHERE (v.field_value = 1)))::numeric * (100)::numeric) / (NULLIF(count(v.field_id) FILTER (WHERE (v.field_value <> 0)), 0))::numeric), 2), (0)::numeric) AS average_accuracy
   FROM (core.notes n
     LEFT JOIN core.note_field_values v ON ((v.note_id = n.id)))
  WHERE (NOT n.is_deleted)
  GROUP BY COALESCE(n.writing_date, n.arrival_at);


--
-- Name: vw_field_error_summary; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_field_error_summary AS
 SELECT field_id,
    field_name,
    (count(field_id))::integer AS total_checked,
    (count(field_id) FILTER (WHERE ((field_value >= 2) AND (field_value <= 6))))::integer AS total_errors,
    (count(field_id) FILTER (WHERE (field_value = 0)))::integer AS total_unknown,
    COALESCE(round((((count(field_id) FILTER (WHERE ((field_value >= 2) AND (field_value <= 6))))::numeric * (100)::numeric) / (NULLIF(count(field_id) FILTER (WHERE (field_value <> 0)), 0))::numeric), 2), (0)::numeric) AS error_rate
   FROM reporting.vw_note_field_details
  WHERE (field_value IS NOT NULL)
  GROUP BY field_id, field_name;


--
-- Name: vw_note_details; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_note_details AS
SELECT
    NULL::uuid AS note_id,
    NULL::character varying(100) AS note_number,
    NULL::uuid AS note_type_id,
    NULL::character varying(100) AS note_type_name,
    NULL::uuid AS company_id,
    NULL::character varying(255) AS company_name,
    NULL::uuid AS factory_id,
    NULL::character varying(200) AS factory_name,
    NULL::uuid AS production_line_id,
    NULL::character varying(200) AS production_line_name,
    NULL::date AS writing_date,
    NULL::date AS arrival_at,
    NULL::character varying(50) AS status,
    NULL::text AS image_path,
    NULL::timestamp without time zone AS created_at,
    NULL::uuid AS created_by,
    NULL::character varying(255) AS created_by_name,
    NULL::timestamp without time zone AS review_started_at,
    NULL::timestamp without time zone AS approved_at,
    NULL::uuid AS approved_by,
    NULL::character varying(255) AS approved_by_name,
    NULL::integer AS total_fields,
    NULL::integer AS active_count,
    NULL::integer AS inactive_count,
    NULL::integer AS unknown_count,
    NULL::numeric AS accuracy_percentage,
    NULL::numeric AS error_percentage;


--
-- Name: vw_note_indicator_facts_v2; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_note_indicator_facts_v2 AS
 SELECT n.id AS note_id,
    n.note_number,
    n.company_id,
    n.factory_id,
    n.production_line_id,
    n.note_type_id,
    nt.code AS note_type_code,
    n.status,
    n.writing_date,
    n.arrival_at,
    n.created_by,
    ntf.field_id,
    nf.code AS field_code,
    nf.name_ar AS field_name,
    (nfv.id IS NOT NULL) AS has_value_row,
    nfv.field_value AS stored_field_value,
    (COALESCE((nfv.field_value)::integer, 0))::smallint AS effective_field_value,
    ((nfv.id IS NULL) OR (nfv.field_value = 0)) AS is_unreviewed,
    ((nfv.field_value >= 1) AND (nfv.field_value <= 6)) AS is_reviewed,
    ((nfv.field_value >= 1) AND (nfv.field_value <= 5)) AS is_applicable_reviewed,
    (nfv.field_value = 1) AS is_correct,
    ((nfv.field_value >= 2) AND (nfv.field_value <= 5)) AS is_error,
    (nfv.field_value = 6) AS is_not_applicable
   FROM ((((core.notes n
     JOIN core.note_types nt ON ((nt.id = n.note_type_id)))
     JOIN core.note_type_fields ntf ON (((ntf.note_type_id = n.note_type_id) AND ntf.is_active AND (NOT ntf.is_deleted))))
     JOIN core.note_fields nf ON (((nf.id = ntf.field_id) AND nf.is_active AND (NOT nf.is_deleted))))
     LEFT JOIN core.note_field_values nfv ON (((nfv.note_id = n.id) AND (nfv.field_id = ntf.field_id))))
  WHERE (NOT n.is_deleted);


--
-- Name: VIEW vw_note_indicator_facts_v2; Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON VIEW reporting.vw_note_indicator_facts_v2 IS 'One fact per non-deleted memo and currently active expected field. Missing rows and value 0 are unreviewed; 1 is correct; 2-5 are errors; 6 is not applicable.';


--
-- Name: vw_note_indicator_facts_v3; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_note_indicator_facts_v3 AS
 SELECT n.id AS note_id,
    n.note_number,
    n.company_id,
    n.factory_id,
    n.production_line_id,
    n.note_type_id,
    nt.code AS note_type_code,
    n.status AS workflow_status,
    n.created_at,
    n.writing_date,
    n.arrival_at,
    n.created_by,
    ntf.field_id,
    nf.code AS field_code,
    nf.name_ar AS field_name,
    (nfv.id IS NOT NULL) AS has_value_row,
    nfv.field_value AS stored_field_value,
    (COALESCE((nfv.field_value)::integer, 0))::smallint AS effective_field_value,
    ((nfv.id IS NULL) OR (nfv.field_value = 0)) AS is_unreviewed,
    ((nfv.field_value >= 1) AND (nfv.field_value <= 6)) AS is_reviewed,
    ((nfv.field_value >= 1) AND (nfv.field_value <= 5)) AS is_applicable_reviewed,
    (nfv.field_value = 1) AS is_correct,
    ((nfv.field_value >= 2) AND (nfv.field_value <= 5)) AS is_error,
    (nfv.field_value = 6) AS is_not_applicable
   FROM ((((core.notes n
     JOIN core.note_types nt ON ((nt.id = n.note_type_id)))
     JOIN core.note_type_fields ntf ON (((ntf.note_type_id = n.note_type_id) AND ntf.is_active AND (NOT ntf.is_deleted))))
     JOIN core.note_fields nf ON (((nf.id = ntf.field_id) AND nf.is_active AND (NOT nf.is_deleted))))
     LEFT JOIN core.note_field_values nfv ON (((nfv.note_id = n.id) AND (nfv.field_id = ntf.field_id))))
  WHERE (NOT n.is_deleted);


--
-- Name: VIEW vw_note_indicator_facts_v3; Type: COMMENT; Schema: reporting; Owner: -
--

COMMENT ON VIEW reporting.vw_note_indicator_facts_v3 IS 'v3 facts: missing/0 unreviewed, 1 correct, 2..5 error, 6 not applicable; workflow_status is not analytics classification.';


--
-- Name: vw_note_type_summary; Type: VIEW; Schema: reporting; Owner: -
--

CREATE VIEW reporting.vw_note_type_summary AS
 SELECT nt.id AS note_type_id,
    nt.name_ar AS note_type_name,
    (count(n.id))::integer AS total_notes,
    (count(n.id) FILTER (WHERE ((n.status)::text = 'DRAFT'::text)))::integer AS draft_count,
    (count(n.id) FILTER (WHERE ((n.status)::text = 'UNDER_REVIEW'::text)))::integer AS under_review_count,
    (count(n.id) FILTER (WHERE ((n.status)::text = 'RETURNED_FOR_CORRECTION'::text)))::integer AS returned_for_correction_count,
    (count(n.id) FILTER (WHERE ((n.status)::text = 'COMPLETE'::text)))::integer AS complete_count,
    (count(n.id) FILTER (WHERE ((n.status)::text = 'HAS_ERRORS'::text)))::integer AS has_errors_count,
    (count(n.id) FILTER (WHERE ((n.status)::text = 'NEEDS_REVIEW'::text)))::integer AS needs_review_count,
    (count(n.id) FILTER (WHERE ((n.status)::text = 'APPROVED'::text)))::integer AS approved_count
   FROM (core.note_types nt
     LEFT JOIN core.notes n ON (((n.note_type_id = nt.id) AND (NOT n.is_deleted))))
  WHERE (NOT nt.is_deleted)
  GROUP BY nt.id, nt.name_ar;


--
-- Name: app_settings; Type: TABLE; Schema: system; Owner: -
--

CREATE TABLE system.app_settings (
    key character varying(100) NOT NULL,
    value text,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: backup_history; Type: TABLE; Schema: system; Owner: -
--

CREATE TABLE system.backup_history (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    backup_type character varying(50) NOT NULL,
    source_profile_name character varying(100),
    target_profile_name character varying(100),
    file_path text,
    status character varying(50) NOT NULL,
    started_at timestamp without time zone DEFAULT now() NOT NULL,
    finished_at timestamp without time zone,
    error_message text,
    created_by uuid,
    CONSTRAINT ck_backup_history_status CHECK (((status)::text = ANY (ARRAY[('RUNNING'::character varying)::text, ('SUCCESS'::character varying)::text, ('FAILED'::character varying)::text, ('CANCELLED'::character varying)::text]))),
    CONSTRAINT ck_backup_history_type CHECK (((backup_type)::text = ANY (ARRAY[('SERVER_TO_FILE'::character varying)::text, ('FILE_TO_LOCAL'::character varying)::text])))
);


--
-- Name: connection_profiles; Type: TABLE; Schema: system; Owner: -
--

CREATE TABLE system.connection_profiles (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(100) NOT NULL,
    profile_type character varying(20) NOT NULL,
    host character varying(255) NOT NULL,
    port integer DEFAULT 5432 NOT NULL,
    database_name character varying(255) NOT NULL,
    username character varying(255) NOT NULL,
    encrypted_password text NOT NULL,
    is_active boolean DEFAULT false NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_connection_profiles_type CHECK (((profile_type)::text = ANY (ARRAY[('SERVER'::character varying)::text, ('LOCAL'::character varying)::text])))
);


--
-- Name: database_migration_history; Type: TABLE; Schema: system; Owner: -
--

CREATE TABLE system.database_migration_history (
    migration_version character varying(30) NOT NULL,
    previous_version character varying(30),
    new_version character varying(30) NOT NULL,
    started_at timestamp without time zone NOT NULL,
    completed_at timestamp without time zone,
    status character varying(20) NOT NULL,
    description text NOT NULL,
    error_details text,
    CONSTRAINT ck_database_migration_history_status CHECK (((status)::text = ANY (ARRAY[('SUCCESS'::character varying)::text, ('FAILED'::character varying)::text, ('RUNNING'::character varying)::text])))
);


--
-- Name: database_settings; Type: TABLE; Schema: system; Owner: -
--

CREATE TABLE system.database_settings (
    singleton boolean DEFAULT true NOT NULL,
    database_version character varying(30) NOT NULL,
    last_updated_at timestamp without time zone DEFAULT now() NOT NULL,
    last_updated_by uuid,
    compatible_application_version character varying(30),
    CONSTRAINT database_settings_singleton_check CHECK (singleton)
);


--
-- Name: schema_migrations; Type: TABLE; Schema: system; Owner: -
--

CREATE TABLE system.schema_migrations (
    version character varying(30) NOT NULL,
    script_name text NOT NULL,
    checksum character varying(64),
    applied_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: database_version_status; Type: VIEW; Schema: system; Owner: -
--

CREATE VIEW system.database_version_status AS
 SELECT COALESCE(( SELECT schema_migrations.version
           FROM system.schema_migrations
          WHERE ((schema_migrations.version)::text ~ '^[0-9]+$'::text)
          ORDER BY (schema_migrations.version)::numeric DESC
         LIMIT 1), database_version, '000'::character varying) AS database_version,
    compatible_application_version,
    last_updated_at,
    last_updated_by
   FROM system.database_settings settings
  WHERE (singleton = true);


--
-- Name: VIEW database_version_status; Type: COMMENT; Schema: system; Owner: -
--

COMMENT ON VIEW system.database_version_status IS 'Single read-only source for the installed database migration and compatible application release.';


--
-- Name: report_identity_settings; Type: TABLE; Schema: system; Owner: -
--

CREATE TABLE system.report_identity_settings (
    singleton boolean DEFAULT true NOT NULL,
    ministry_name text DEFAULT ''::text NOT NULL,
    administrative_level_one text DEFAULT ''::text NOT NULL,
    administrative_level_two text DEFAULT ''::text NOT NULL,
    company_name text DEFAULT ''::text NOT NULL,
    issuing_entity_name text DEFAULT ''::text NOT NULL,
    default_city text DEFAULT ''::text NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_by uuid,
    republic_name_ar text DEFAULT 'الجمهورية العربية السورية'::text NOT NULL,
    republic_name_en text DEFAULT 'Syrian Arab Republic'::text NOT NULL,
    ministry_name_en text DEFAULT ''::text NOT NULL,
    company_name_en text DEFAULT ''::text NOT NULL,
    administrative_level_one_en text DEFAULT ''::text NOT NULL,
    administrative_level_two_en text DEFAULT ''::text NOT NULL,
    administrative_level_three text DEFAULT ''::text NOT NULL,
    administrative_level_three_en text DEFAULT ''::text NOT NULL,
    CONSTRAINT report_identity_settings_singleton_check CHECK (singleton)
);


--
-- Name: artifacts; Type: TABLE; Schema: updates; Owner: -
--

CREATE TABLE updates.artifacts (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    release_id uuid NOT NULL,
    operating_system character varying(20) NOT NULL,
    architecture character varying(30) NOT NULL,
    package_url text NOT NULL,
    package_size bigint NOT NULL,
    sha256 character(64) NOT NULL,
    manifest_signature text NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT artifacts_package_size_check CHECK ((package_size >= 0)),
    CONSTRAINT ck_updates_artifact_os CHECK (((operating_system)::text = ANY (ARRAY[('windows'::character varying)::text, ('linux'::character varying)::text]))),
    CONSTRAINT ck_updates_artifact_sha256 CHECK ((sha256 ~ '^[0-9A-Fa-f]{64}$'::text))
);


--
-- Name: audit_log; Type: TABLE; Schema: updates; Owner: -
--

CREATE TABLE updates.audit_log (
    id bigint NOT NULL,
    user_id uuid,
    action character varying(80) NOT NULL,
    release_id uuid,
    description text,
    created_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: audit_log_id_seq; Type: SEQUENCE; Schema: updates; Owner: -
--

CREATE SEQUENCE updates.audit_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: audit_log_id_seq; Type: SEQUENCE OWNED BY; Schema: updates; Owner: -
--

ALTER SEQUENCE updates.audit_log_id_seq OWNED BY updates.audit_log.id;


--
-- Name: installations; Type: TABLE; Schema: updates; Owner: -
--

CREATE TABLE updates.installations (
    device_id uuid NOT NULL,
    device_name character varying(200),
    operating_system character varying(20) NOT NULL,
    architecture character varying(30) NOT NULL,
    installed_version character varying(30),
    last_checked_at timestamp without time zone,
    last_update_status character varying(30),
    last_update_message text,
    updated_at timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: releases; Type: TABLE; Schema: updates; Owner: -
--

CREATE TABLE updates.releases (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    version character varying(30) NOT NULL,
    status character varying(20) DEFAULT 'DRAFT'::character varying NOT NULL,
    release_notes text DEFAULT ''::text NOT NULL,
    minimum_version character varying(30),
    minimum_database_version character varying(20),
    target_database_version character varying(20),
    published_at timestamp without time zone,
    published_by uuid,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    created_by uuid,
    CONSTRAINT ck_updates_release_status CHECK (((status)::text = ANY (ARRAY[('DRAFT'::character varying)::text, ('PUBLISHED'::character varying)::text, ('STOPPED'::character varying)::text])))
);


--
-- Name: logs id; Type: DEFAULT; Schema: audit; Owner: -
--

ALTER TABLE ONLY audit.logs ALTER COLUMN id SET DEFAULT nextval('audit.logs_id_seq'::regclass);


--
-- Name: audit_log id; Type: DEFAULT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.audit_log ALTER COLUMN id SET DEFAULT nextval('updates.audit_log_id_seq'::regclass);


--
-- Data for Name: roles; Type: TABLE DATA; Schema: auth; Owner: -
--

COPY auth.roles (id, code, name_ar, is_active, created_at, updated_at) FROM stdin;
f8b54ff1-88ee-40b7-86ba-d16d9d910533	ADMIN	مدير النظام	t	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
566e9cce-cb21-441e-9be2-f10e46cf0d73	DATA_ENTRY	مدخل بيانات	t	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
c30683ec-5ca0-4c1c-8945-06a7014b1b52	REVIEWER	مراجع	t	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
83e74ee6-f1c3-4d05-9aa4-42461dc25efd	VIEWER	قارئ	t	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
\.


--
-- Data for Name: users; Type: TABLE DATA; Schema: auth; Owner: -
--

COPY auth.users (id, username, password_hash, full_name, role_id, is_active, is_deleted, created_at, updated_at, must_change_password) FROM stdin;
a01b08ff-cd77-4437-a4a4-3634db36df4d	3	$2a$11$5eG3fXep.S9.O.bFauuKY.LGqz4h2ZM7/IoemSifDYm0pqxEHkQsq	عبدالله عمر زعزع	566e9cce-cb21-441e-9be2-f10e46cf0d73	t	f	2026-07-27 16:18:50.537937	2026-07-29 16:58:16.536543	f
30d7ac47-67c8-4930-a932-fb612960d019	4	$2a$11$nEhd.fdooupoJPsP3dmaduWboYe/SsjX0R5TwhS/0eU34Iqpx4chm	أبو عادل	566e9cce-cb21-441e-9be2-f10e46cf0d73	t	f	2026-08-19 18:15:55.430782	2026-08-19 18:19:36.693392	f
581a015f-3df0-4ac5-8cac-8621baee2a57	5	$2a$11$X/CM7bjcMXXObYZ7mpk/lOaT7OPE4TRv.jR23l2WcoIrFqvb9KLh6	أبو الحارث	566e9cce-cb21-441e-9be2-f10e46cf0d73	t	f	2026-08-19 18:16:57.951231	2026-08-19 18:20:16.078825	f
609381b2-0201-4e12-8fd5-737b841da8dd	6	$2a$11$ioMEwAZc3VHlaJQAsAb3zOlGrtX1hzJLOhM0PDGmkdJnY0QBuPC/e	أبو البراء	c30683ec-5ca0-4c1c-8945-06a7014b1b52	t	f	2026-08-19 18:18:27.217832	2026-08-19 18:20:49.611317	f
280d7132-11a2-489c-b80f-4f33892c48d8	7	$2a$11$1zI5fS.bsE/WVfHhNj8OuOWT0J/4UwnDLkVbFLU7bW5khS1tLYqTG	أبو الجود	f8b54ff1-88ee-40b7-86ba-d16d9d910533	t	f	2026-08-20 14:12:57.238935	2026-08-24 15:09:19.81833	f
4a38d53d-e737-4c9a-9061-1965241b76d5	1	$2a$11$bEMuL0kOr/1ImuJ1iQHj1eZjdnzr0wy3PlLHksyYgtCzVmd3SXNPu	محمد ديب	566e9cce-cb21-441e-9be2-f10e46cf0d73	t	f	2026-07-27 16:16:24.882902	2026-08-24 16:24:44.270394	f
cd7bd06e-a7e3-474b-b267-748be7c282d2	2	$2a$11$apm65qrBAiZ/6KTSmNn6hOA5QBjn9.ED8dbfuqXxr1NHjKlHnV.eG	ابو الجود	c30683ec-5ca0-4c1c-8945-06a7014b1b52	t	f	2026-07-27 16:17:33.417486	2026-08-24 16:24:47.086625	f
912125dc-767c-4fbb-a6d1-1fef9e330f60	admin	$2a$11$cAvAzVCigAiB29vu4IgT1uO9i2gQzHw2THufi/Dol7ai3g2SGioKe	مدير النظام	f8b54ff1-88ee-40b7-86ba-d16d9d910533	t	f	2026-07-27 15:42:08.081606	2026-09-08 23:55:41.591089	f
\.


--
-- Data for Name: companies; Type: TABLE DATA; Schema: core; Owner: -
--

COPY core.companies (id, name, is_active, is_deleted, created_at, updated_at) FROM stdin;
8f64dac0-bbff-45a2-bdc0-54a215e9b349	الشهباء	t	f	2026-07-27 16:20:13.525436	2026-07-27 16:20:13.525436
779ca67a-d17e-4f38-bd62-36faa81dc78b	الفيحاء	t	f	2026-07-27 16:20:18.522562	2026-07-27 16:20:18.522562
dcbfcb8c-922c-467f-9c70-473fb276277b	صقر	t	f	2026-07-27 16:20:29.408462	2026-07-27 16:20:29.408462
838681f7-882e-4e53-9015-5f09297f9115	شهاب	t	f	2026-07-27 16:20:35.393609	2026-07-27 16:20:35.393609
2653be67-4c7d-4b79-ad39-6375bca921d8	عطاء	t	f	2026-07-27 16:20:40.860453	2026-07-27 16:20:40.860453
fe261c61-5fc8-4ae9-9012-d7c1d748d17a	رماح	t	f	2026-07-27 16:20:45.96331	2026-07-27 16:20:45.96331
299d87aa-0151-4990-aef9-24657c5ada4a	رعد	t	f	2026-07-27 16:20:51.94418	2026-07-27 16:20:51.94418
636751a2-2413-4d86-80d8-eb12e91d9db0	الرائد	t	f	2026-07-27 16:20:24.447334	2026-09-09 10:14:03.009094
0e8487aa-a478-4213-949c-d7023dac1543	باشق	t	t	2026-09-09 10:14:15.977901	2026-09-09 10:14:21.759934
\.


--
-- Data for Name: factories; Type: TABLE DATA; Schema: core; Owner: -
--

COPY core.factories (id, name, is_active, is_deleted, created_at, updated_at, company_id) FROM stdin;
f87552d4-30cb-4504-b131-0384b821678a	الوقود المتجانس	t	f	2026-07-27 16:21:35.16846	2026-07-27 16:21:35.16846	8f64dac0-bbff-45a2-bdc0-54a215e9b349
f966eed8-517a-4553-80fd-3b553e752039	مصنع المواد الحساسة والبوادئ	t	f	2026-07-27 16:21:58.368998	2026-07-27 16:21:58.368998	8f64dac0-bbff-45a2-bdc0-54a215e9b349
1a4a2164-fa06-4e5f-b13a-a6840fcf7fad	مصنع المتفجرات الحربية	t	f	2026-07-27 16:22:18.450993	2026-07-27 16:22:18.450993	8f64dac0-bbff-45a2-bdc0-54a215e9b349
db5f0f59-82cb-4360-971c-584619bb37d1	مصنع الاستطلاع	t	f	2026-07-27 16:32:08.941333	2026-07-27 16:32:08.941333	838681f7-882e-4e53-9015-5f09297f9115
88df013c-c9b3-4577-8b13-15db46e5406c	مصنع الانتحاري	t	f	2026-07-27 16:32:24.113031	2026-07-27 16:32:24.113031	838681f7-882e-4e53-9015-5f09297f9115
16d215d4-7d8b-4d8d-bda3-017d8e418cba	مديرية المستودعات	t	f	2026-07-27 16:34:39.393071	2026-07-27 16:36:26.798349	838681f7-882e-4e53-9015-5f09297f9115
065521ab-bce9-48a8-8710-0525634953b2	المصنع الاول	t	f	2026-07-27 16:37:45.445798	2026-07-27 16:37:45.445798	dcbfcb8c-922c-467f-9c70-473fb276277b
6fe258bb-8cb9-4660-bbda-0d9ce4688781	البوليميرات والمواد المركبة	t	f	2026-07-27 16:40:37.370722	2026-07-27 16:40:37.370722	779ca67a-d17e-4f38-bd62-36faa81dc78b
00070535-4b56-424f-b954-a15a6aa4a46b	القنابل والالغام	t	f	2026-07-27 16:40:56.153429	2026-07-27 16:40:56.153429	779ca67a-d17e-4f38-bd62-36faa81dc78b
b7aefc36-7858-4aab-9ed4-0df0d03bec86	الكهروكيمياء	t	f	2026-07-27 16:41:09.430882	2026-07-27 16:41:09.430882	779ca67a-d17e-4f38-bd62-36faa81dc78b
c52e4b06-fda5-4027-85b0-23bcc152444b	المستودع المركزي m18	t	f	2026-07-27 16:45:26.630178	2026-07-27 16:45:26.630178	779ca67a-d17e-4f38-bd62-36faa81dc78b
8a040034-d784-43e4-8080-517137f4ffab	FD	t	f	2026-07-27 17:49:35.365716	2026-07-27 17:49:35.365716	779ca67a-d17e-4f38-bd62-36faa81dc78b
15864b23-5aea-46ba-98b0-39356df55fe4	المتفجرات الصناعية I	t	f	2026-07-27 18:33:59.076966	2026-07-27 18:33:59.076966	8f64dac0-bbff-45a2-bdc0-54a215e9b349
9cf3c4ae-dcb8-401b-b43d-cc76958b642e	وحدة هندسة الإنتاج	t	f	2026-07-28 13:56:35.441088	2026-07-28 13:56:35.441088	8f64dac0-bbff-45a2-bdc0-54a215e9b349
f7f0efcc-3d58-48fa-bce9-36d194e47103	المستودع المركزي	t	f	2026-07-28 14:45:21.727242	2026-07-28 14:45:21.727242	8f64dac0-bbff-45a2-bdc0-54a215e9b349
53ba14ff-3e49-46c7-9db5-51a790dc3bbf	الراجمات	t	f	2026-07-29 12:36:33.268912	2026-07-29 12:36:33.268912	636751a2-2413-4d86-80d8-eb12e91d9db0
e9e00344-1748-4035-804f-83b4823ffbff	M	t	f	2026-07-29 18:33:27.381772	2026-07-29 18:33:27.381772	779ca67a-d17e-4f38-bd62-36faa81dc78b
40993978-cb3d-4726-88fc-ea340aaf3e5a	الرائد	t	f	2026-07-30 16:54:45.581059	2026-07-30 16:54:45.581059	636751a2-2413-4d86-80d8-eb12e91d9db0
9c0f6b89-1ec9-4200-b2ff-9ff5bbb81b4e	الخدمات الصناعية	t	f	2026-07-30 17:23:25.102205	2026-07-30 17:23:25.102205	636751a2-2413-4d86-80d8-eb12e91d9db0
07e36a68-9b7c-4d5d-b460-6898009feb78	الرائد للصناعات الثقيلة	t	f	2026-08-02 13:06:40.207662	2026-08-02 13:06:40.207662	636751a2-2413-4d86-80d8-eb12e91d9db0
e293f89c-0a5b-407c-86f2-7dec3a25dc0c	FP	t	f	2026-08-02 14:34:05.576204	2026-08-02 14:34:05.576204	779ca67a-d17e-4f38-bd62-36faa81dc78b
e00b93ca-57ac-4041-befa-f1d90bc3790e	المصفحات	t	f	2026-08-05 14:11:27.794237	2026-08-05 14:11:27.794237	636751a2-2413-4d86-80d8-eb12e91d9db0
3f12c825-f053-4c79-bb52-d53c64b68a51	المستودع المركزي	t	f	2026-08-05 18:33:05.106184	2026-08-05 18:33:05.106184	636751a2-2413-4d86-80d8-eb12e91d9db0
62bac5b8-60b2-4a0b-b4f7-0057bbc37b92	المخابر الصيدلانية	t	f	2026-08-09 14:52:17.357024	2026-08-09 14:52:17.357024	779ca67a-d17e-4f38-bd62-36faa81dc78b
1612be8c-8966-40d1-b42a-4c85b8ca5de6	الدفاع السلبي	t	f	2026-08-11 18:08:11.496407	2026-08-11 18:08:11.496407	636751a2-2413-4d86-80d8-eb12e91d9db0
a0a167c8-2aba-4dee-961b-3bbfc5ad7ae8	مديرية البحث والتطوير	t	f	2026-08-24 14:27:36.970794	2026-08-24 14:27:36.970794	779ca67a-d17e-4f38-bd62-36faa81dc78b
014b7481-a5f8-4f4a-826a-aac77d87186f	O	t	f	2026-08-27 16:44:12.470379	2026-08-27 16:44:12.470379	8f64dac0-bbff-45a2-bdc0-54a215e9b349
254ddfe1-2c9c-4ca1-9575-cba831ceb5ce	الشس 2	t	t	2026-09-09 10:14:37.415724	2026-09-09 10:14:46.27607	636751a2-2413-4d86-80d8-eb12e91d9db0
\.


--
-- Data for Name: note_fields; Type: TABLE DATA; Schema: core; Owner: -
--

COPY core.note_fields (id, code, name_ar, description, default_sort_order, is_active, is_deleted, created_at, updated_at) FROM stdin;
78dd960d-84d1-4a66-aaf4-12f4c81f5e03	factory	مصنع	\N	1	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
33a9ec48-2f2b-4a55-a3df-ad887ee45fda	line	خط	\N	2	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
73998832-131d-4e57-9b9f-030b4416df9e	warehouse_code	رمز مستودع	\N	3	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
9805f87d-ac03-46de-afa4-d111e50fd638	memo_time	الوقت	\N	4	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
22fdc650-3ca7-4313-9b98-4ca4673a7099	memo_date	التاريخ	\N	5	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
0acae613-08c6-41fb-9ce1-dc8dc16c201d	memo_number	رقم المذكرة	\N	6	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
351c8aa7-54c4-4c13-bbef-4b9e06a85440	entry_note_label	مذكرة إدخال	\N	7	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
c7cf7c95-b4f4-47bd-a7b6-65899fca3464	exit_note_label	مذكرة إخراج	\N	8	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
77e4f6b0-7169-4f8b-a36b-68c07c5c021d	source_exit_memo_number	رقم مذكرة الإخراج من الجهة المسلمة	\N	9	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
6593e7e8-3557-4b2b-9fd4-06cf26081c17	receiving_entity	الجهة المستلمة	\N	10	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
f46d12be-806b-4f05-a165-3742e2bb477b	material_name	اسم المادة	\N	11	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
991f777c-361e-4dd4-8793-ec35482f2c62	quantity	الكمية	\N	13	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
7b5a033d-c6d0-4e6d-9c18-9bae4ac9151c	unit	الوحدة	\N	14	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
ce84dd80-277d-4666-a495-8c03e5882ce8	deliverer_name_signature	اسم المسلم والتوقيع	\N	15	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
c6896257-e07e-4a05-b61c-5bf7681599b6	receiver_name_signature	اسم المستلم والتوقيع	\N	16	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
61f3d070-8947-4985-b55c-197a30ff0874	delivered_to_brother	تم تسليم الأخ	\N	17	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
cb39b157-fbf0-406e-aeae-8e160bf18c18	entity	الجهة	\N	18	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
f11dffe2-1c1e-4cb2-b040-ff30d99577d3	department	القسم	\N	19	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
ea2d3615-3ebf-45a0-a6f4-4564e63088ec	product_name_code	اسم / رمز المنتج	\N	20	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
7e8f0f49-58c8-4209-a599-8a125e3d2445	delivering_entity	الجهة المسلمة	\N	21	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
30ee9a64-e1ac-47c3-a53f-0b896d3b9c96	material_code	رمز المادة	\N	12	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.464251
693a8f93-f2bb-40c4-9271-d3e46bd0e0f1	بيل	يبليب	\N	15	t	f	2026-09-09 02:39:23.764664	2026-09-09 02:39:23.764664
\.


--
-- Data for Name: note_type_fields; Type: TABLE DATA; Schema: core; Owner: -
--

COPY core.note_type_fields (id, note_type_id, field_id, sort_order, is_required, is_active, is_deleted, created_at, updated_at) FROM stdin;
47b1d920-cb50-451f-b716-256070325799	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	c6896257-e07e-4a05-b61c-5bf7681599b6	15	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
959095f1-7bf3-4104-887c-29edbc9cd8e3	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	ce84dd80-277d-4666-a495-8c03e5882ce8	14	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
71703f26-42f1-446b-93da-431ee17b54d4	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	7b5a033d-c6d0-4e6d-9c18-9bae4ac9151c	13	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
46319eff-714c-4ee3-a12f-77ee97036272	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	991f777c-361e-4dd4-8793-ec35482f2c62	12	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
8d421995-1418-4640-8348-c2a96a8127a0	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	30ee9a64-e1ac-47c3-a53f-0b896d3b9c96	11	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
d9f786cf-952f-4f0e-b858-b623b9ff0899	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	f46d12be-806b-4f05-a165-3742e2bb477b	10	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
2f7cf5c4-9494-449b-90de-2602a156d457	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	6593e7e8-3557-4b2b-9fd4-06cf26081c17	9	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
e0ea29bb-a859-48af-aebb-38c0253c1a94	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	77e4f6b0-7169-4f8b-a36b-68c07c5c021d	8	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
6e3db1bf-5289-47ea-9f1d-c82a47776972	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	351c8aa7-54c4-4c13-bbef-4b9e06a85440	7	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
3833efe9-5c2d-46b6-b0f3-0adac8402a6b	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	0acae613-08c6-41fb-9ce1-dc8dc16c201d	6	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
8c1184d4-c5a6-453c-8930-2d399c6f3aad	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	22fdc650-3ca7-4313-9b98-4ca4673a7099	5	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
4f565c6f-466e-4040-9162-e6d0b99bc248	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	9805f87d-ac03-46de-afa4-d111e50fd638	4	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
be80e28b-03e5-4d10-ac18-3536c4c5066a	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	73998832-131d-4e57-9b9f-030b4416df9e	3	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
837ce490-b43c-4bea-929a-19cb04541b38	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	33a9ec48-2f2b-4a55-a3df-ad887ee45fda	2	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
d08f4673-96bd-4041-8cd6-822cf792c497	0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	78dd960d-84d1-4a66-aaf4-12f4c81f5e03	1	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
dd726aaf-129a-4988-ab2c-4b3233717e34	6751b009-8f0f-4385-8523-f595685cb61e	c6896257-e07e-4a05-b61c-5bf7681599b6	14	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
924a1699-d19c-41f6-b14e-2e9d6d00bd5f	6751b009-8f0f-4385-8523-f595685cb61e	ce84dd80-277d-4666-a495-8c03e5882ce8	13	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
5422ab02-f803-402b-aeff-0a28e9c0bcbc	6751b009-8f0f-4385-8523-f595685cb61e	7b5a033d-c6d0-4e6d-9c18-9bae4ac9151c	12	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
44a7fbfe-05ff-4a06-9228-d4dcd4ad7501	6751b009-8f0f-4385-8523-f595685cb61e	991f777c-361e-4dd4-8793-ec35482f2c62	11	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
bda063ec-07fd-44c1-a70a-8a3f27012e1d	6751b009-8f0f-4385-8523-f595685cb61e	30ee9a64-e1ac-47c3-a53f-0b896d3b9c96	10	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
c0950449-c719-4f92-bb2d-4538463a45e9	6751b009-8f0f-4385-8523-f595685cb61e	f46d12be-806b-4f05-a165-3742e2bb477b	9	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
2ef47c65-3fd2-4e60-b32c-c3d4370de108	6751b009-8f0f-4385-8523-f595685cb61e	6593e7e8-3557-4b2b-9fd4-06cf26081c17	8	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
9cf8d39b-de1d-4bee-8d7b-c416e92b978c	6751b009-8f0f-4385-8523-f595685cb61e	c7cf7c95-b4f4-47bd-a7b6-65899fca3464	7	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
8e6fe6ce-3d92-45ba-83a8-965b7b616715	6751b009-8f0f-4385-8523-f595685cb61e	0acae613-08c6-41fb-9ce1-dc8dc16c201d	6	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
812c7c3b-d068-40a6-84cb-92c06f3be708	6751b009-8f0f-4385-8523-f595685cb61e	22fdc650-3ca7-4313-9b98-4ca4673a7099	5	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
c878dc56-5efb-4073-ac51-99978cefdb2d	6751b009-8f0f-4385-8523-f595685cb61e	9805f87d-ac03-46de-afa4-d111e50fd638	4	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
f698d0d9-574b-4de6-88a9-33ab11503364	6751b009-8f0f-4385-8523-f595685cb61e	73998832-131d-4e57-9b9f-030b4416df9e	3	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
50aa4863-c109-488d-b1f5-241582125e03	6751b009-8f0f-4385-8523-f595685cb61e	33a9ec48-2f2b-4a55-a3df-ad887ee45fda	2	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
2696161d-bdf9-4ce8-8d34-d0e48d599745	6751b009-8f0f-4385-8523-f595685cb61e	78dd960d-84d1-4a66-aaf4-12f4c81f5e03	1	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
b80d16d9-9617-49d7-a388-500e1a033e0a	e3393dcb-9627-4c1b-a594-596be9edb950	ea2d3615-3ebf-45a0-a6f4-4564e63088ec	10	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
5cbd9bde-a7df-4995-92ac-2359f7cb81f5	e3393dcb-9627-4c1b-a594-596be9edb950	f11dffe2-1c1e-4cb2-b040-ff30d99577d3	9	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
ad1bd202-cae3-4fb5-b5d4-5c4d684bb96d	e3393dcb-9627-4c1b-a594-596be9edb950	cb39b157-fbf0-406e-aeae-8e160bf18c18	8	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
20b820d2-38b4-455f-b814-dce44e393ce1	e3393dcb-9627-4c1b-a594-596be9edb950	61f3d070-8947-4985-b55c-197a30ff0874	7	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
e3fa4b69-b79c-46c2-a86b-c1e6d35d9269	e3393dcb-9627-4c1b-a594-596be9edb950	c6896257-e07e-4a05-b61c-5bf7681599b6	14	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
3cf41ae0-affa-4c72-85ff-22f386e5ed8e	e3393dcb-9627-4c1b-a594-596be9edb950	ce84dd80-277d-4666-a495-8c03e5882ce8	13	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
a7876dd4-59e0-4291-bd61-c576cbf5fe5f	e3393dcb-9627-4c1b-a594-596be9edb950	7b5a033d-c6d0-4e6d-9c18-9bae4ac9151c	12	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
9afed660-e8d7-420a-a6ad-55f7c774ffb5	e3393dcb-9627-4c1b-a594-596be9edb950	991f777c-361e-4dd4-8793-ec35482f2c62	11	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
3d088567-a69e-49d6-b083-b55596c117a5	e3393dcb-9627-4c1b-a594-596be9edb950	0acae613-08c6-41fb-9ce1-dc8dc16c201d	6	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
8d264497-2689-4adf-9a08-a91e8e94b71e	e3393dcb-9627-4c1b-a594-596be9edb950	22fdc650-3ca7-4313-9b98-4ca4673a7099	5	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
b04d2362-53bd-43d7-b6f1-3e3ff6ade1c5	e3393dcb-9627-4c1b-a594-596be9edb950	9805f87d-ac03-46de-afa4-d111e50fd638	4	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
fe0bc2e4-d12f-4fdb-8066-557121e01a1c	e3393dcb-9627-4c1b-a594-596be9edb950	73998832-131d-4e57-9b9f-030b4416df9e	3	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
00a2134a-8e78-4014-8f2d-7d678f225926	e3393dcb-9627-4c1b-a594-596be9edb950	33a9ec48-2f2b-4a55-a3df-ad887ee45fda	2	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
d91dab6b-cd82-4e0f-9423-876beeef53ff	e3393dcb-9627-4c1b-a594-596be9edb950	78dd960d-84d1-4a66-aaf4-12f4c81f5e03	1	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
64693ab0-111f-404a-9443-f17dc345547a	fe8e166e-a91a-49ab-be79-d652c64db081	7e8f0f49-58c8-4209-a599-8a125e3d2445	7	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
6097e4db-6688-43bb-a246-fde597878053	fe8e166e-a91a-49ab-be79-d652c64db081	f46d12be-806b-4f05-a165-3742e2bb477b	9	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
954c252e-a4f4-41b0-acd0-52feccc9a19f	fe8e166e-a91a-49ab-be79-d652c64db081	6593e7e8-3557-4b2b-9fd4-06cf26081c17	8	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
0ae6bd6b-3653-4912-84ac-fdf3ad5d411e	fe8e166e-a91a-49ab-be79-d652c64db081	0acae613-08c6-41fb-9ce1-dc8dc16c201d	6	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
f4b3eb8d-32cb-4e67-bf3f-85d75d3663ee	fe8e166e-a91a-49ab-be79-d652c64db081	22fdc650-3ca7-4313-9b98-4ca4673a7099	5	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
5219bb7f-c221-4d7e-a3ce-abb80267e11b	fe8e166e-a91a-49ab-be79-d652c64db081	9805f87d-ac03-46de-afa4-d111e50fd638	4	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
31e41d37-8cfb-4b5a-9787-0abe218ec858	fe8e166e-a91a-49ab-be79-d652c64db081	73998832-131d-4e57-9b9f-030b4416df9e	3	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
74f1f511-6961-45eb-973e-d2567af62a54	fe8e166e-a91a-49ab-be79-d652c64db081	33a9ec48-2f2b-4a55-a3df-ad887ee45fda	2	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
9e9cf65f-5fea-4b9c-909a-839e80346b11	fe8e166e-a91a-49ab-be79-d652c64db081	78dd960d-84d1-4a66-aaf4-12f4c81f5e03	1	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
93c3c29e-cef8-4a53-9c68-2c09d77dcfba	fe8e166e-a91a-49ab-be79-d652c64db081	c6896257-e07e-4a05-b61c-5bf7681599b6	14	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.365661
125453a6-95ff-417e-a229-8bd27f697532	fe8e166e-a91a-49ab-be79-d652c64db081	ce84dd80-277d-4666-a495-8c03e5882ce8	13	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.365661
8b2e5c65-74a7-42ce-8366-6c5b26775166	fe8e166e-a91a-49ab-be79-d652c64db081	7b5a033d-c6d0-4e6d-9c18-9bae4ac9151c	12	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.365661
d57051ae-42bd-48ee-be02-cdf1223ff4a5	fe8e166e-a91a-49ab-be79-d652c64db081	991f777c-361e-4dd4-8793-ec35482f2c62	11	t	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.365661
e274a604-8fdd-4a83-8065-7c67c27040c4	fe8e166e-a91a-49ab-be79-d652c64db081	30ee9a64-e1ac-47c3-a53f-0b896d3b9c96	10	t	t	f	2026-07-27 15:42:08.365661	2026-07-27 15:42:08.464251
736bf998-3860-42ab-98bb-b831021733cc	e3393dcb-9627-4c1b-a594-596be9edb950	693a8f93-f2bb-40c4-9271-d3e46bd0e0f1	15	t	f	f	2026-09-09 02:39:23.764664	2026-09-09 02:39:29.705758
\.


--
-- Data for Name: note_types; Type: TABLE DATA; Schema: core; Owner: -
--

COPY core.note_types (id, code, name_ar, description, is_active, is_deleted, created_at, updated_at) FROM stdin;
0478d5c4-89e0-47d5-ac6e-260a6e6c43f0	ENTRY	مذكرة إدخال	بنود مذكرة الإدخال	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
6751b009-8f0f-4385-8523-f595685cb61e	EXIT	مذكرة إخراج	بنود مذكرة الإخراج	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
e3393dcb-9627-4c1b-a594-596be9edb950	DELIVERY	مذكرة تسليم	بنود مذكرة التسليم	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
fe8e166e-a91a-49ab-be79-d652c64db081	TRANSFER	مذكرة مناقلة	بنود مذكرة المناقلة	t	f	2026-07-27 15:42:08.081606	2026-07-27 15:42:08.081606
\.


--
-- Data for Name: production_lines; Type: TABLE DATA; Schema: core; Owner: -
--

COPY core.production_lines (id, factory_id, name, is_active, is_deleted, created_at, updated_at) FROM stdin;
e5a31399-01b0-4903-abab-07f03ac89f7f	f87552d4-30cb-4504-b131-0384b821678a	o21	t	f	2026-07-27 16:25:46.538936	2026-07-27 16:25:46.538936
6c5d96f3-99af-4061-be83-c461b7f790bb	f87552d4-30cb-4504-b131-0384b821678a	o25	t	f	2026-07-27 16:26:00.473749	2026-07-27 16:26:00.473749
143f757b-fea9-4de7-add1-f2a78793a0f5	f87552d4-30cb-4504-b131-0384b821678a	o300	t	f	2026-07-27 16:26:14.369434	2026-07-27 16:26:14.369434
161d97cb-897c-4288-b6ac-349ba895f9d7	f966eed8-517a-4553-80fd-3b553e752039	خط السينوكسيد	t	f	2026-07-27 16:29:18.418469	2026-07-27 16:29:18.418469
a1af3b9d-3b89-4e53-ab29-2db238deaf64	f966eed8-517a-4553-80fd-3b553e752039	خط المبدئات	t	f	2026-07-27 16:29:56.693852	2026-07-27 16:29:56.693852
eed5b537-a863-4cbd-b277-e6be9907c9e9	f966eed8-517a-4553-80fd-3b553e752039	خط الصواعق	t	f	2026-07-27 16:30:11.890479	2026-07-27 16:30:11.890479
e598595e-37b7-452e-b764-726146c2faa9	f966eed8-517a-4553-80fd-3b553e752039	خط الكبسون	t	f	2026-07-27 16:30:26.892252	2026-07-27 16:30:26.892252
6927a13b-fbac-4445-9ec4-000fa155d346	1a4a2164-fa06-4e5f-b13a-a6840fcf7fad	خط البتن	t	f	2026-07-27 16:30:47.595698	2026-07-27 16:30:47.595698
c92899fc-e464-49b7-856e-faab68f554ce	1a4a2164-fa06-4e5f-b13a-a6840fcf7fad	خط رفع تركيز الاحماض	t	f	2026-07-27 16:31:08.521279	2026-07-27 16:31:08.521279
52364975-7897-4331-b740-abed03892ef7	1a4a2164-fa06-4e5f-b13a-a6840fcf7fad	خط المتفجرات الصناعية	t	f	2026-07-27 16:31:28.303054	2026-07-27 16:31:28.303054
b42aa24a-34f4-47f7-92a4-85ba50f0dd41	db5f0f59-82cb-4360-971c-584619bb37d1	المستودع sh-e1	t	f	2026-07-27 16:33:26.665922	2026-07-27 16:33:26.665922
438be7ac-9c9b-4ffe-8972-ec4a18df2d31	88df013c-c9b3-4577-8b13-15db46e5406c	المستودع sh-a1	t	f	2026-07-27 16:33:59.82101	2026-07-27 16:33:59.82101
1247e808-0d52-469f-977f-c60acf8fb444	16d215d4-7d8b-4d8d-bda3-017d8e418cba	المستودع sh-c	t	f	2026-07-27 16:36:57.515851	2026-07-27 16:36:57.515851
77de939b-0451-4c8d-9daf-1cbb08a0daba	065521ab-bce9-48a8-8710-0525634953b2	الخط 400	t	f	2026-07-27 16:38:40.117117	2026-07-27 16:38:40.117117
eed807ed-0daf-47b6-89dc-12e552c2d998	065521ab-bce9-48a8-8710-0525634953b2	الخط 500	t	f	2026-07-27 16:38:57.825397	2026-07-27 16:38:57.825397
7d90b52b-300a-4e7e-a967-e8056f9d840a	b7aefc36-7858-4aab-9ed4-0df0d03bec86	خط الfe2	t	f	2026-07-27 16:41:36.25247	2026-07-27 16:41:36.25247
5c8f578e-ec39-4fc0-920a-9509a034d745	b7aefc36-7858-4aab-9ed4-0df0d03bec86	خط الfe3	t	f	2026-07-27 16:41:57.510223	2026-07-27 16:41:57.510223
51a7138b-c039-4e2b-8543-1314aee13b9c	00070535-4b56-424f-b954-a15a6aa4a46b	القنابل اليدوية fm1	t	f	2026-07-27 16:42:46.72976	2026-07-27 16:42:46.72976
019246e2-98d1-4a63-8b47-b78cf74ddd25	00070535-4b56-424f-b954-a15a6aa4a46b	الالغام المتفجرة fm2	t	f	2026-07-27 16:43:14.993904	2026-07-27 16:43:14.993904
44174b10-9892-48a0-aa1a-dfa75ccd1d91	00070535-4b56-424f-b954-a15a6aa4a46b	مستودع ال m1	t	f	2026-07-27 16:43:54.030357	2026-07-27 16:43:54.030357
33abf3f6-88e9-4a71-ac4b-4dc8f70e8816	6fe258bb-8cb9-4660-bbda-0d9ce4688781	خط انتاج الدهانات	t	f	2026-07-27 16:46:09.19381	2026-07-27 16:46:09.19381
62189449-eae8-4543-acaf-8692cb1d89d5	8a040034-d784-43e4-8080-517137f4ffab	المصول والترياقات	t	f	2026-07-27 17:50:09.787893	2026-07-27 17:50:09.787893
c517b985-9822-4238-8214-4fe961b11235	15864b23-5aea-46ba-98b0-39356df55fe4	المتفجرات الصناعية	t	f	2026-07-27 18:39:10.00357	2026-07-27 18:39:10.00357
8c4f2573-3c2e-4347-ae25-3437cf74645c	1a4a2164-fa06-4e5f-b13a-a6840fcf7fad	Mex-p	t	f	2026-07-28 11:59:26.580053	2026-07-28 11:59:26.580053
6ffaf55e-34ed-49ac-8585-299934fc2238	9cf3c4ae-dcb8-401b-b43d-cc76958b642e	النجارة	t	f	2026-07-28 13:56:55.067881	2026-07-28 13:56:55.067881
cd34cfbf-4388-48af-b773-3ad66c70b6a6	f7f0efcc-3d58-48fa-bce9-36d194e47103	بدون خط	t	f	2026-07-28 14:45:47.536474	2026-07-28 14:45:47.536474
2aca5967-e44c-49f1-a352-5cba542436f2	53ba14ff-3e49-46c7-9db5-51a790dc3bbf	SF	t	f	2026-07-29 12:36:57.308946	2026-07-29 12:36:57.308946
ffd96198-aa61-4f37-9251-67360ab51c9a	53ba14ff-3e49-46c7-9db5-51a790dc3bbf	AV	t	f	2026-07-29 12:37:25.525348	2026-07-29 12:37:25.525348
b1c69d9d-a1b5-48e2-8a81-70311246afd2	e9e00344-1748-4035-804f-83b4823ffbff	FM	t	f	2026-07-29 18:33:53.499741	2026-07-29 18:33:53.499741
acc91a42-60db-4935-b6ba-f681ea5c22dc	e9e00344-1748-4035-804f-83b4823ffbff	FM1	t	f	2026-07-29 18:37:13.52533	2026-07-29 18:37:13.52533
e70e48c0-e077-49f5-ac64-4f495cd2e798	065521ab-bce9-48a8-8710-0525634953b2	المستودعات	t	f	2026-07-30 16:26:34.832633	2026-07-30 16:26:34.832633
b9ff87f9-49f1-4c38-a05a-0c5cff76213a	9c0f6b89-1ec9-4200-b2ff-9ff5bbb81b4e	Ford	t	f	2026-07-30 17:23:51.162719	2026-07-30 17:23:51.162719
937d0054-05d0-4cb8-a6a5-0fd226e704a9	9c0f6b89-1ec9-4200-b2ff-9ff5bbb81b4e	رعد	t	f	2026-07-30 18:42:16.079069	2026-07-30 18:42:16.079069
d35c806a-3a63-486a-891b-e79584c60d5d	07e36a68-9b7c-4d5d-b460-6898009feb78	الخدمات الصيانة	t	f	2026-08-02 13:07:02.730106	2026-08-02 13:07:02.730106
53c92e10-b88f-415c-afba-52bbaaf9a678	e293f89c-0a5b-407c-86f2-7dec3a25dc0c	انتاج الدهانات FP1	t	f	2026-08-02 14:34:35.368323	2026-08-02 14:34:35.368323
5ba4881c-b91d-4b11-8cc5-73885e1dd515	e9e00344-1748-4035-804f-83b4823ffbff	FM2	t	f	2026-08-04 14:30:23.962919	2026-08-04 14:30:23.962919
acf182e7-9d55-4b66-9e12-b7930e9971a0	e00b93ca-57ac-4041-befa-f1d90bc3790e	AV	t	f	2026-08-05 14:11:49.49627	2026-08-05 14:11:49.49627
1b7d0dbd-f629-4c99-82c4-88a90671d7e2	3f12c825-f053-4c79-bb52-d53c64b68a51	بدون خط	t	f	2026-08-05 18:33:22.800822	2026-08-05 18:33:22.800822
9381d415-07d3-4a3e-bb5f-e68fcd174249	62bac5b8-60b2-4a0b-b4f7-0057bbc37b92	مخبر المصول والتقانة الحيوية (FD110)	t	f	2026-08-09 14:52:59.509754	2026-08-09 14:52:59.509754
6650e4ed-ced8-44d4-953d-c804784ff27f	62bac5b8-60b2-4a0b-b4f7-0057bbc37b92	مخبر المصول والتقانة الحيوية (FD120)	t	f	2026-08-09 15:05:43.422107	2026-08-09 15:05:43.422107
30e82151-a68f-4100-97f5-189bdfd4535a	1612be8c-8966-40d1-b42a-4c85b8ca5de6	النماذج	t	f	2026-08-11 18:08:51.117702	2026-08-11 18:08:51.117702
9e7f7442-f8e6-472f-a7d6-6bdf8d13d537	e293f89c-0a5b-407c-86f2-7dec3a25dc0c	المطاط	t	f	2026-08-20 13:57:37.462792	2026-08-20 13:57:37.462792
e4a04a0e-d871-449c-9b8e-239af143d5ec	a0a167c8-2aba-4dee-961b-3bbfc5ad7ae8	المصول والترياقات	t	f	2026-08-24 14:27:57.893495	2026-08-24 14:27:57.893495
8542d8d2-9d80-46c4-871e-0a5839da9f41	3f12c825-f053-4c79-bb52-d53c64b68a51	WQ	t	f	2026-08-24 15:34:04.834867	2026-08-24 15:34:04.834867
9a9a34d5-a4c2-4b1e-984b-0eb68f404fe7	e9e00344-1748-4035-804f-83b4823ffbff	FM3	t	f	2026-08-25 17:50:22.830044	2026-08-25 17:50:22.830044
fc9efc0f-76a0-4cf3-a101-ac2986e13d65	014b7481-a5f8-4f4a-826a-aac77d87186f	البارود	t	f	2026-08-27 16:44:34.925147	2026-08-27 16:44:34.925147
375c1e19-4898-43e0-9ba9-fde40bfaa62a	f87552d4-30cb-4504-b131-0384b821678a	البارود	t	f	2026-08-27 16:47:26.037929	2026-08-27 16:47:26.037929
22ee3fe4-8b62-413c-ac58-80ccd50dae9d	9c0f6b89-1ec9-4200-b2ff-9ff5bbb81b4e	ال	t	t	2026-09-09 10:14:55.331295	2026-09-09 10:15:01.282288
\.


--
-- Data for Name: app_settings; Type: TABLE DATA; Schema: system; Owner: -
--

COPY system.app_settings (key, value, updated_at) FROM stdin;
ACTIVE_CONNECTION_PROFILE_ID	\N	2026-07-27 15:42:08.081606
LAST_BACKUP_AT	\N	2026-07-27 15:42:08.081606
UPDATE_SERVICE_URL	\N	2026-08-10 13:53:25.040636
UPDATE_PUBLIC_KEY	\N	2026-08-10 13:53:25.040636
INTERACTIVE_DASHBOARD_URL	https://192.168.1.228/	2026-08-10 13:57:03.624987
INTERACTIVE_DASHBOARD_DISPLAY_URL	\N	2026-08-10 13:57:03.624987
SERVER_BASE_ADDRESS	https://192.168.1.228:8080/	2026-08-26 14:31:23.651152
DASHBOARD_BASE_ADDRESS	http://192.168.1.228:8080/	2026-09-07 09:48:37.438445
DEPLOYMENT_MODE	InternalLanHttp	2026-09-07 09:48:37.438445
APP_MODE	SERVER	2026-09-09 10:17:45.378712
\.


--
-- Data for Name: connection_profiles; Type: TABLE DATA; Schema: system; Owner: -
--

COPY system.connection_profiles (id, name, profile_type, host, port, database_name, username, encrypted_password, is_active, created_at, updated_at) FROM stdin;
\.


--
-- Data for Name: database_migration_history; Type: TABLE DATA; Schema: system; Owner: -
--

COPY system.database_migration_history (migration_version, previous_version, new_version, started_at, completed_at, status, description, error_details) FROM stdin;
005	004	005	2026-08-18 14:47:30.566712	2026-09-13 12:30:44.837309	SUCCESS	005_create_views.sql	\N
006	005	006	2026-08-18 14:47:30.572555	2026-09-13 12:30:44.840126	SUCCESS	006_create_functions.sql	\N
007	006	007	2026-08-18 14:47:30.577893	2026-09-13 12:30:44.843275	SUCCESS	007_create_indexes.sql	\N
008	007	008	2026-08-18 14:47:30.583019	2026-09-13 12:30:44.845898	SUCCESS	008_simplify_companies.sql	\N
009	008	009	2026-08-18 14:47:30.588201	2026-09-13 12:30:44.848716	SUCCESS	009_store_note_attachments_in_database.sql	\N
010	009	010	2026-08-18 14:47:30.593905	2026-09-13 12:30:44.851186	SUCCESS	010_allow_notes_without_writing_date.sql	\N
011	010	011	2026-08-18 14:47:30.598498	2026-09-13 12:30:44.854016	SUCCESS	011_add_unreviewed_indicator_value.sql	\N
012	011	012	2026-08-18 14:47:30.603524	2026-09-13 12:30:44.858008	SUCCESS	012_add_review_workflow.sql	\N
013	012	013	2026-08-18 14:47:30.609111	2026-09-13 12:30:44.861747	SUCCESS	013_add_duplicate_and_note_links.sql	\N
014	013	014	2026-08-18 14:47:30.613833	2026-09-13 12:30:44.864347	SUCCESS	014_indicator_model_and_normalization.sql	\N
015	014	015	2026-08-18 14:47:30.618994	2026-09-13 12:30:44.867609	SUCCESS	015_secure_bootstrap_admin.sql	\N
016	015	016	2026-08-18 14:47:30.623695	2026-09-13 12:30:44.870799	SUCCESS	016_add_factories.sql	\N
017	016	017	2026-08-18 14:47:30.629099	2026-09-13 12:30:44.873728	SUCCESS	017_link_factories_to_companies.sql	\N
018	017	018	2026-08-18 14:47:30.63341	2026-09-13 12:30:44.876424	SUCCESS	018_align_indicator_error_classification.sql	\N
019	018	019	2026-08-18 14:47:30.637182	2026-09-13 12:30:44.879899	SUCCESS	019_align_indicator_summary_functions.sql	\N
020	019	020	2026-08-18 14:47:30.640896	2026-09-13 12:30:44.882976	SUCCESS	020_enforce_note_factory_company.sql	\N
021	020	021	2026-08-18 14:47:30.645152	2026-09-13 12:30:44.886402	SUCCESS	021_complete_factory_and_indicator_integrity.sql	\N
022	021	022	2026-08-18 14:47:30.649639	2026-09-13 12:30:44.890583	SUCCESS	022_make_indicator_reasons_optional.sql	\N
023	022	023	2026-08-18 14:47:30.654766	2026-09-13 12:30:44.894036	SUCCESS	023_convert_arrival_at_to_date.sql	\N
024	023	024	2026-08-18 14:47:30.659182	2026-09-13 12:30:44.897097	SUCCESS	024_add_production_lines.sql	\N
025	024	025	2026-08-18 14:47:30.664627	2026-09-13 12:30:44.900144	SUCCESS	025_preserve_review_timestamps.sql	\N
026	025	026	2026-08-18 14:47:30.669664	2026-09-13 12:30:44.90328	SUCCESS	026_enforce_single_entry_exit_link.sql	\N
027	026	027	2026-08-18 14:47:30.674877	2026-09-13 12:30:44.906934	SUCCESS	027_add_transfer_material_code.sql	\N
028	027	028	2026-08-18 14:47:30.679961	2026-09-13 12:30:44.911161	SUCCESS	028_add_duplicate_incidents.sql	\N
029	028	029	2026-08-18 14:47:30.685061	2026-09-13 12:30:44.914196	SUCCESS	029_add_audit_log_filter_indexes.sql	\N
030	029	030	2026-08-18 14:47:30.690054	2026-09-13 12:30:44.917725	SUCCESS	030_refresh_reporting_contracts.sql	\N
031	030	031	2026-08-18 14:47:30.694992	2026-09-13 12:30:44.921271	SUCCESS	031_preserve_inactive_production_line_on_note_update.sql	\N
032	031	032	2026-08-18 14:47:30.700148	2026-09-13 12:30:44.924415	SUCCESS	032_support_historic_line_with_note_upsert.sql	\N
033	032	033	2026-08-18 14:47:30.70547	2026-09-13 12:30:44.927445	SUCCESS	033_correct_review_started_timestamp_semantics.sql	\N
034	033	034	2026-08-18 14:47:30.710954	2026-09-13 12:30:44.930425	SUCCESS	034_enforce_cross_side_single_note_link.sql	\N
035	034	035	2026-08-18 14:47:30.716113	2026-09-13 12:30:44.933501	SUCCESS	035_canonicalize_transfer_material_code.sql	\N
036	035	036	2026-08-18 14:47:30.72086	2026-09-13 12:30:44.936053	SUCCESS	036_restore_reporting_summary_views.sql	\N
037	036	037	2026-08-18 14:47:30.725125	2026-09-13 12:30:44.938984	SUCCESS	037_allow_cancelled_backup_history.sql	\N
038	037	038	2026-08-18 14:47:30.729216	2026-09-13 12:30:44.941708	SUCCESS	038_add_migration_checksums.sql	\N
039	038	039	2026-08-18 14:47:30.734681	2026-09-13 12:30:44.94449	SUCCESS	039_normalize_usernames.sql	\N
040	039	040	2026-08-18 14:47:30.741903	2026-09-13 12:30:44.94755	SUCCESS	040_enforce_normalized_note_number.sql	\N
041	040	041	2026-08-18 14:47:30.747226	2026-09-13 12:30:44.950702	SUCCESS	041_fix_reporting_view_contracts.sql	\N
042	041	042	2026-07-29 15:24:38.380458	2026-09-13 12:30:44.953696	SUCCESS	042_add_database_version_tracking.sql	\N
043	042	043	2026-07-29 15:24:38.404706	2026-09-13 12:30:44.956838	SUCCESS	043_add_report_identity_settings.sql	\N
044	043	044	2026-07-29 15:24:38.423477	2026-09-13 12:30:44.959914	SUCCESS	044_expand_report_identity_bilingual.sql	\N
045	044	045	2026-07-29 15:24:38.4403	2026-09-13 12:30:44.963345	SUCCESS	045_harden_database_version_metadata.sql	\N
046	045	046	2026-08-10 13:53:24.870978	2026-09-13 12:30:44.967069	SUCCESS	046_unify_dashboard_contract_and_updates.sql	\N
047	046	047	2026-08-10 13:53:25.037339	2026-09-13 12:30:44.970121	SUCCESS	047_add_update_client_settings.sql	\N
048	047	048	2026-08-10 13:53:25.05552	2026-09-13 12:30:44.972902	SUCCESS	048_correct_dashboard_duplicate_scope.sql	\N
049	048	049	2026-08-11 12:41:14.97879	2026-09-13 12:30:44.97651	SUCCESS	049_exclude_deleted_memos_strictly.sql	\N
050	049	050	2026-08-11 12:41:15.028017	2026-09-13 12:30:44.980224	SUCCESS	050_add_advanced_company_metrics_and_unlinked_filter.sql	\N
051	050	051	2026-08-11 12:41:15.044303	2026-09-13 12:30:44.983447	SUCCESS	051_company_report_efficiency_formulas.sql	\N
052	051	052	2026-08-11 12:41:15.068467	2026-09-13 12:30:44.986303	SUCCESS	052_fix_company_metrics_duplicate_scope.sql	\N
053	052	053	2026-08-11 12:41:15.083827	2026-09-13 12:30:44.990429	SUCCESS	053_set_application_version_1031.sql	\N
054	053	054	2026-08-12 12:28:00.701309	2026-09-13 12:30:44.994326	SUCCESS	054_add_company_metrics_v2_contract.sql	\N
055	054	055	2026-08-12 12:28:00.750475	2026-09-13 12:30:44.997745	SUCCESS	055_enforce_unique_company_duplicate_incident.sql	\N
056	055	056	2026-08-12 12:28:00.769642	2026-09-13 12:30:45.000977	SUCCESS	056_set_application_version_104.sql	\N
057	056	057	2026-08-12 12:28:00.781938	2026-09-13 12:30:45.004531	SUCCESS	057_add_idempotent_note_create_operations.sql	\N
058	057	058	2026-08-12 12:28:00.797556	2026-09-13 12:30:45.00777	SUCCESS	058_add_company_metrics_snapshot_v2_contract.sql	\N
059	058	059	2026-08-12 12:28:00.809195	2026-09-13 12:30:45.012437	SUCCESS	059_set_application_version_105.sql	\N
060	059	060	2026-08-18 14:47:30.845624	2026-09-13 12:30:45.016385	SUCCESS	060_add_company_metrics_snapshot_v3_contract.sql	\N
061	060	061	2026-08-18 14:47:30.920493	2026-09-13 12:30:45.020085	SUCCESS	061_extend_company_metrics_snapshot_v3_filters.sql	\N
062	061	062	2026-09-06 16:33:26.072054	2026-09-13 12:30:45.023764	SUCCESS	062_add_numeric_note_number_contract.sql	\N
063	062	063	2026-09-06 16:33:26.377632	2026-09-13 12:30:45.028512	SUCCESS	063_add_note_source_contract.sql	\N
064	063	064	2026-09-06 16:33:26.506144	2026-09-13 12:30:45.031972	SUCCESS	064_add_dashboard_date_basis_contract.sql	\N
065	064	065	2026-09-06 16:33:26.51226	2026-09-13 12:30:45.035384	SUCCESS	065_set_application_version_107.sql	\N
066	065	066	2026-09-08 12:19:31.935912	2026-09-13 12:30:45.039246	SUCCESS	066_add_missing_fk_covering_indexes.sql	\N
067	066	067	2026-09-08 19:57:47.090687	2026-09-13 12:30:45.042338	SUCCESS	067_set_application_version_200.sql	\N
068	067	068	2026-09-10 02:01:05.394637	2026-09-13 12:30:45.045859	SUCCESS	068_add_note_attachments_table.sql	\N
001	000	001	2026-08-18 14:47:30.542788	2026-09-13 12:30:44.819788	SUCCESS	001_create_schemas.sql	\N
004	003	004	2026-08-18 14:47:30.561898	2026-09-13 12:30:44.834275	SUCCESS	004_create_seed_data.sql	\N
003	002	003	2026-08-18 14:47:30.556662	2026-09-13 12:30:44.832121	SUCCESS	003_create_constraints.sql	\N
069	068	069	2026-09-13 12:30:45.051523	2026-09-13 12:30:45.159024	SUCCESS	069_add_loot_and_invoice_note_flags.sql	\N
002	001	002	2026-08-18 14:47:30.55121	2026-09-13 12:30:44.829842	SUCCESS	002_create_tables.sql	\N
\.


--
-- Data for Name: database_settings; Type: TABLE DATA; Schema: system; Owner: -
--

COPY system.database_settings (singleton, database_version, last_updated_at, last_updated_by, compatible_application_version) FROM stdin;
t	070	2026-10-04 13:20:53.426792	\N	2.0.0
\.


--
-- Data for Name: report_identity_settings; Type: TABLE DATA; Schema: system; Owner: -
--

COPY system.report_identity_settings (singleton, ministry_name, administrative_level_one, administrative_level_two, company_name, issuing_entity_name, default_city, updated_at, updated_by, republic_name_ar, republic_name_en, ministry_name_en, company_name_en, administrative_level_one_en, administrative_level_two_en, administrative_level_three, administrative_level_three_en) FROM stdin;
t	وزارة الدفاع	إدارة التخطيط ودعم القرار	مديرية الإحصاء وتحليل البيانات	الشركة القابضة للصناعات الدفاعية	رئيس دائرة جمع البيانات	دمشق	2026-09-13 13:00:39.027891	912125dc-767c-4fbb-a6d1-1fef9e330f60	الجمهورية العربية السورية	Syrian Arab Republic	Ministry of Defense	Defense Industries Holding Company	Planning & Decision Support Directorate	Statistics & Data Analysis Division	دائرة جمع البيانات	
\.


--
-- Data for Name: schema_migrations; Type: TABLE DATA; Schema: system; Owner: -
--

COPY system.schema_migrations (version, script_name, checksum, applied_at) FROM stdin;
001	001_create_schemas.sql	856b36fd40beb5f034a62d63516e69096675b4622867e70bec3eca6089fa408b	2026-07-27 15:42:08.011861
002	002_create_tables.sql	67d4d353ce5e58a468706424be51767cee392ce20bd7dd280bd11409a59cb92c	2026-07-27 15:42:08.029349
003	003_create_constraints.sql	41774dcbe908f67cb026e0b3a442c3692f99ca60a05a06e62ad88c468d73434e	2026-07-27 15:42:08.060071
004	004_create_seed_data.sql	a4a9c20b4b8f5fa7f6faa61e7c082f5b6a54604ec357812ae5c96383c4bd5713	2026-07-27 15:42:08.081606
005	005_create_views.sql	f0f6563643210614419c8702dcf3c7af6fe9cdd100d33a610c9c88d123e91349	2026-07-27 15:42:08.097317
006	006_create_functions.sql	06b4c9cf4000758120b543bfe0dfecdea654d49d59944b251f30862a0b336f9d	2026-07-27 15:42:08.125767
007	007_create_indexes.sql	bb11549146689617ba24f73f5f0b16f4870cddcbef9ec6a3fbd263d3773a484f	2026-07-27 15:42:08.137511
008	008_simplify_companies.sql	1bfe7a1ed169e5501022ebd91fdc7333638e59b0991206de5032a1e9861e7ad9	2026-07-27 15:42:08.148951
009	009_store_note_attachments_in_database.sql	5ae0342377d58e0bef9e76efb2660157f4f8192a3a0a96d44596420167c0c6be	2026-07-27 15:42:08.156512
010	010_allow_notes_without_writing_date.sql	2917da2f51bed5e6a8d2c7f7b21dcd015ed07f7aa1b5a2b2fbd7667e0664b6fd	2026-07-27 15:42:08.168942
011	011_add_unreviewed_indicator_value.sql	cecffb900241a63c7ab37377cbc400e870b25713e48c6f7c89a5ad44693a32aa	2026-07-27 15:42:08.177029
012	012_add_review_workflow.sql	661203428ffc67306e01899491ffe18afa35d34a81f1a26aefcc945dada65e6a	2026-07-27 15:42:08.189348
013	013_add_duplicate_and_note_links.sql	1dda62a727ea255b97e03acbd53fa880a3ff4c1c653b3f8518a59836d041f22b	2026-07-27 15:42:08.20273
014	014_indicator_model_and_normalization.sql	f9aa3063a528b2d819cd1da79c5d790e3627d9b2270aea3b42801220a33ba074	2026-07-27 15:42:08.217435
015	015_secure_bootstrap_admin.sql	df4cc4a2fb73aeaeedceb8310c1dabe8c15476fb6277c8805112d661bdafa846	2026-07-27 15:42:08.233312
016	016_add_factories.sql	28dc2e5a6e9e190abd833c401c48d79ec728a3a4344ff05dc1b1ae76fcfa18a9	2026-07-27 15:42:08.242568
017	017_link_factories_to_companies.sql	ef5b3248b4d4302933a8689f5041bb4730405f7ee8146686c27b987aa7cd4fa7	2026-07-27 15:42:08.255715
018	018_align_indicator_error_classification.sql	110ab6e3509b28689cb1ef43277a9beff0300e7bdf07df0f36fa4cf428327407	2026-07-27 15:42:08.266957
019	019_align_indicator_summary_functions.sql	8306dd893d4c1827f6180502ec6210e639c8819240131836fcbe1d9b3806a440	2026-07-27 15:42:08.276744
020	020_enforce_note_factory_company.sql	8bb7022756a3761fdac2f94f8edd664c5f31ddc6e6992cd5d9650bbf832a405e	2026-07-27 15:42:08.284287
021	021_complete_factory_and_indicator_integrity.sql	746efd3d5b6ddcf2175bdf8a52e5b03ee41dc7485146335741bfd36cb2a3d13c	2026-07-27 15:42:08.289953
022	022_make_indicator_reasons_optional.sql	9004705f1c8e2f2928b23555e80fad5a374af4784bbbf9c0b21f9f04b2dac0ec	2026-07-27 15:42:08.307487
023	023_convert_arrival_at_to_date.sql	69c1c7fbb9c9b8252c9a01ca89d7c3fda7a73cfaa27fd422f7838766231e9b88	2026-07-27 15:42:08.316994
024	024_add_production_lines.sql	f103f0fcebc1292fc9315784fc13021ce31262257239ae71841261d6449a5110	2026-07-27 15:42:08.334304
025	025_preserve_review_timestamps.sql	e2812606d6c9d20399d6ae3a107a8ae5880e15967409adb072800f8c0e64f254	2026-07-27 15:42:08.347172
026	026_enforce_single_entry_exit_link.sql	b05fd3ef288de2396f3d5a83c330520366d52dd015e6cb2676765fd18e93d17b	2026-07-27 15:42:08.356945
027	027_add_transfer_material_code.sql	47ccd8feaaa8cd826b61289619ad789a6a10a59e6111d0576d6c2ee70605aa01	2026-07-27 15:42:08.365661
028	028_add_duplicate_incidents.sql	d015b5a32e8f46d8033adeedc307949256ae3a9f3470cba263fb96b290190702	2026-07-27 15:42:08.37369
029	029_add_audit_log_filter_indexes.sql	aa8818bdff779da5e48db984024efc60ac2075541136ba1d273a12a4c610c78a	2026-07-27 15:42:08.387191
030	030_refresh_reporting_contracts.sql	bbeb14c096c7db16d01644ed8e7514271fe0aa52c37e5332af2377d91e3d8543	2026-07-27 15:42:08.399077
031	031_preserve_inactive_production_line_on_note_update.sql	e0130b2aa9d129a916b12a1b71de3b3c842911fc44e56f0bc2d85f9354b56ca2	2026-07-27 15:42:08.418535
032	032_support_historic_line_with_note_upsert.sql	e161ac0ecc6df69d1dc4dc9a330fd79aa3ed780c10df8f3e9790dda5f9402908	2026-07-27 15:42:08.432482
033	033_correct_review_started_timestamp_semantics.sql	144e76b46d43ce4a53afd1686b572d97a1e7cf6276b7b27e60b5676af295d2c2	2026-07-27 15:42:08.441136
034	034_enforce_cross_side_single_note_link.sql	2fc2153b7daccca0e7c6ae0de505004c6b084ce50eec3d86c4a4e91eb26eaa81	2026-07-27 15:42:08.453524
035	035_canonicalize_transfer_material_code.sql	7f41344aa34ecbab81d959abe3f01846e7e451c142a36477ce65cbd33acfee69	2026-07-27 15:42:08.464251
036	036_restore_reporting_summary_views.sql	a0dc7258546ae0ac042b884eb7ea575186bd93b677e57edb25cf4d04cde8ac5e	2026-07-27 15:42:08.478172
037	037_allow_cancelled_backup_history.sql	0ff0455d46060f27b518a96c3cb49e00c3a4a213a0b877db7700a19bda0b1727	2026-07-27 15:42:08.495446
038	038_add_migration_checksums.sql	b5166d223fbcfd28d63c845c851916828f9fbd75c89165f085ac180348bce701	2026-07-27 15:42:08.506192
039	039_normalize_usernames.sql	199f188be329166d09180fedcaca8b0b79ab43e0bb0ff5777ceedef912e983ef	2026-07-27 15:42:08.517097
040	040_enforce_normalized_note_number.sql	3c5fbaaa1f50b8a09bcfecaf28e61d449e65bd84f4fee91b789e46f29c9021aa	2026-07-27 15:42:08.527905
041	041_fix_reporting_view_contracts.sql	634b7ef2377a7182f7d167246f461a51a9e2a789353ef88a8eae3462a8461d4f	2026-07-27 15:42:08.539062
042	042_add_database_version_tracking.sql	51b0c383b2263fc238d361ea1cb91d4573315cef4a0e84cc2916416cc9e3ccaa	2026-07-29 15:24:38.380458
043	043_add_report_identity_settings.sql	d5b66277ea4a3dbb0e1625e59999ac716e2a9e59db863a9d3e20dd4b8f9985b4	2026-07-29 15:24:38.408723
044	044_expand_report_identity_bilingual.sql	543a341d523d0a340dabbe6127140b32df3a557c985173e538f859e2158f4226	2026-07-29 15:24:38.426313
045	045_harden_database_version_metadata.sql	98a78d6f05cff030b99b3ae606ef9065caea785703e03bb796b2f1cf1bdb2e40	2026-07-29 15:24:38.443885
046	046_unify_dashboard_contract_and_updates.sql	63113854c430e0c30ddaa8ff88312ddae8d7fc1740e7af87f100da0d0a9f2e04	2026-08-10 13:53:24.886691
047	047_add_update_client_settings.sql	0409fb207fb28060dee5b4608793a2327f1d308662aeb9f2612fc7f6deb2506f	2026-08-10 13:53:25.040636
048	048_correct_dashboard_duplicate_scope.sql	81de408cbf569d12129edeb03aef35835f719e78beddb5cbab1eda1efd06fd57	2026-08-10 13:53:25.059417
049	049_exclude_deleted_memos_strictly.sql	451763e9540c60b36bce28892dff9aedc09fc8abfc25394ed7e84f1c29b279d0	2026-08-11 12:41:14.984362
050	050_add_advanced_company_metrics_and_unlinked_filter.sql	a230383c99e684aff4ca7fe3b4244783c86079c6c0accdff2aa9c4d971def725	2026-08-11 12:41:15.031246
051	051_company_report_efficiency_formulas.sql	bc3c4d3dd559c25bbc7231fdeedcb5e2664b4ece99ef99189a2c14f11656e433	2026-08-11 12:41:15.046997
052	052_fix_company_metrics_duplicate_scope.sql	d0ffe3c0775d1f4e529c38cbf35a379d0c3e970ac45b0b850b566f558d042293	2026-08-11 12:41:15.072103
053	053_set_application_version_1031.sql	c505e0e2f233f14f5f92a0c8680ded71e4a96c32e3a61979731e54083a91b726	2026-08-11 12:41:15.086469
054	054_add_company_metrics_v2_contract.sql	a9eda6820d48e5f727ff9b8ca1f5497a3993d0c6afe8a094f57c0fbc36cf3976	2026-08-12 12:28:00.707108
055	055_enforce_unique_company_duplicate_incident.sql	35ad717a21cf4d2e20fccf1993bca5909e7eda74319091baf7be5107eebeb9ff	2026-08-12 12:28:00.753778
056	056_set_application_version_104.sql	41c19905046af853a9fa18839b71e91351918380cd0c034a80771c0d9875c916	2026-08-12 12:28:00.771934
057	057_add_idempotent_note_create_operations.sql	8711c9d4b41487f9a5fd1f9991b43b2a6b29a982b292023b79073f60412f0cd9	2026-08-12 12:28:00.785091
058	058_add_company_metrics_snapshot_v2_contract.sql	e4495e74d258718e6f5f0a4db86aec37c99e775ca119d979e9ead1f93cfb5775	2026-08-12 12:28:00.799689
059	059_set_application_version_105.sql	373623234369e17fb72aaff0cf7a55315061f62e795283b611eb3d013565ca50	2026-08-12 12:28:00.811762
060	060_add_company_metrics_snapshot_v3_contract.sql	a7e11f79025dc349074b2d947889358fb7930668252ff52d347ca92ce4b67c72	2026-08-18 14:47:30.857236
061	061_extend_company_metrics_snapshot_v3_filters.sql	efc4fd61184e561c402e751051cd7d97d15bdb8deef52859ec9d46ed86a91ed2	2026-08-18 14:47:30.926842
062	062_add_numeric_note_number_contract.sql	6e67efb16da19df34a48bc386b68e28530e7db9a6dae48d42590f80c08dc2fa9	2026-09-06 16:33:26.077074
063	063_add_note_source_contract.sql	dbbb36fe5ddc6f4f69e1d14ab5101e400e21aa79b4bff598d3f50389cf6af5ce	2026-09-06 16:33:26.379596
064	064_add_dashboard_date_basis_contract.sql	669e8046817602e576505b7d4b04e2f239ab49bda6dc600e35f4d4ecc7309e34	2026-09-06 16:33:26.507358
065	065_set_application_version_107.sql	e7795afa38a7d43ab536b3aa699618ab1196783c5017c504dec20d2cfd6db2e8	2026-09-06 16:33:26.513268
066	066_add_missing_fk_covering_indexes.sql	3630c1f003375d551b94bf6ca2b9cd47037cf63a8962f5a2e0566df510e1f239	2026-09-08 12:19:31.939318
067	067_set_application_version_200.sql	7c22173742467a99ef6ab07332195a24b81116bdd2e4cd56ff745e12e90c70f0	2026-09-08 19:57:47.090687
068	068_add_note_attachments_table.sql	8be6da01af4acdaed91d825cfa80f0b09855c5b00a3c788657469e3acc470c17	2026-09-10 02:01:05.396642
069	069_add_loot_and_invoice_note_flags.sql	e922a3f1b87ba47e3f6d4e1cd8d70f482acc270561701bd1b89517f05db80c95	2026-09-13 12:30:45.063414
070	070_fix_loot_metrics_and_multi_links.sql	1d69919565fb1da2bf28533174317c3160582ba401aa2ca2f91bbc52b7515448	2026-10-04 14:11:41.429241
\.


--
-- Data for Name: releases; Type: TABLE DATA; Schema: updates; Owner: -
--

COPY updates.releases (id, version, status, release_notes, minimum_version, minimum_database_version, target_database_version, published_at, published_by, created_at, created_by) FROM stdin;
\.


--
-- Name: logs_id_seq; Type: SEQUENCE SET; Schema: audit; Owner: -
--

SELECT pg_catalog.setval('audit.logs_id_seq', 18362, true);


--
-- Name: audit_log_id_seq; Type: SEQUENCE SET; Schema: updates; Owner: -
--

SELECT pg_catalog.setval('updates.audit_log_id_seq', 1, false);


--
-- Name: logs logs_pkey; Type: CONSTRAINT; Schema: audit; Owner: -
--

ALTER TABLE ONLY audit.logs
    ADD CONSTRAINT logs_pkey PRIMARY KEY (id);


--
-- Name: roles roles_code_key; Type: CONSTRAINT; Schema: auth; Owner: -
--

ALTER TABLE ONLY auth.roles
    ADD CONSTRAINT roles_code_key UNIQUE (code);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: auth; Owner: -
--

ALTER TABLE ONLY auth.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: auth; Owner: -
--

ALTER TABLE ONLY auth.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: users users_username_key; Type: CONSTRAINT; Schema: auth; Owner: -
--

ALTER TABLE ONLY auth.users
    ADD CONSTRAINT users_username_key UNIQUE (username);


--
-- Name: notes ck_notes_note_number_value_positive; Type: CHECK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE core.notes
    ADD CONSTRAINT ck_notes_note_number_value_positive CHECK (((note_number_value IS NULL) OR (note_number_value > 0))) NOT VALID;


--
-- Name: companies companies_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.companies
    ADD CONSTRAINT companies_pkey PRIMARY KEY (id);


--
-- Name: duplicate_incidents_archive duplicate_incidents_archive_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents_archive
    ADD CONSTRAINT duplicate_incidents_archive_pkey PRIMARY KEY (id);


--
-- Name: duplicate_incidents duplicate_incidents_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_pkey PRIMARY KEY (id);


--
-- Name: factories factories_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.factories
    ADD CONSTRAINT factories_pkey PRIMARY KEY (id);


--
-- Name: note_attachments note_attachments_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_attachments
    ADD CONSTRAINT note_attachments_pkey PRIMARY KEY (id);


--
-- Name: note_create_operations note_create_operations_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_create_operations
    ADD CONSTRAINT note_create_operations_pkey PRIMARY KEY (operation_id);


--
-- Name: note_field_values note_field_values_note_id_field_id_key; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_field_values
    ADD CONSTRAINT note_field_values_note_id_field_id_key UNIQUE (note_id, field_id);


--
-- Name: note_field_values note_field_values_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_field_values
    ADD CONSTRAINT note_field_values_pkey PRIMARY KEY (id);


--
-- Name: note_fields note_fields_code_key; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_fields
    ADD CONSTRAINT note_fields_code_key UNIQUE (code);


--
-- Name: note_fields note_fields_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_fields
    ADD CONSTRAINT note_fields_pkey PRIMARY KEY (id);


--
-- Name: note_links note_links_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_links
    ADD CONSTRAINT note_links_pkey PRIMARY KEY (id);


--
-- Name: note_review_history note_review_history_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_review_history
    ADD CONSTRAINT note_review_history_pkey PRIMARY KEY (id);


--
-- Name: note_source_exit_refs note_source_exit_refs_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_exit_refs
    ADD CONSTRAINT note_source_exit_refs_pkey PRIMARY KEY (id);


--
-- Name: note_source_invoices note_source_invoices_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_invoices
    ADD CONSTRAINT note_source_invoices_pkey PRIMARY KEY (id);


--
-- Name: note_source_loot note_source_loot_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_loot
    ADD CONSTRAINT note_source_loot_pkey PRIMARY KEY (source_id);


--
-- Name: note_sources note_sources_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_sources
    ADD CONSTRAINT note_sources_pkey PRIMARY KEY (id);


--
-- Name: note_type_fields note_type_fields_note_type_id_field_id_key; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_type_fields
    ADD CONSTRAINT note_type_fields_note_type_id_field_id_key UNIQUE (note_type_id, field_id);


--
-- Name: note_type_fields note_type_fields_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_type_fields
    ADD CONSTRAINT note_type_fields_pkey PRIMARY KEY (id);


--
-- Name: note_types note_types_code_key; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_types
    ADD CONSTRAINT note_types_code_key UNIQUE (code);


--
-- Name: note_types note_types_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_types
    ADD CONSTRAINT note_types_pkey PRIMARY KEY (id);


--
-- Name: notes notes_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_pkey PRIMARY KEY (id);


--
-- Name: production_lines production_lines_pkey; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.production_lines
    ADD CONSTRAINT production_lines_pkey PRIMARY KEY (id);


--
-- Name: note_create_operations ux_note_create_operations_note_id; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_create_operations
    ADD CONSTRAINT ux_note_create_operations_note_id UNIQUE (note_id);


--
-- Name: note_source_exit_refs ux_note_source_exit_ref; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_exit_refs
    ADD CONSTRAINT ux_note_source_exit_ref UNIQUE (source_id, exit_note_id);


--
-- Name: note_sources ux_note_sources_note; Type: CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_sources
    ADD CONSTRAINT ux_note_sources_note UNIQUE (note_id);


--
-- Name: app_settings app_settings_pkey; Type: CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.app_settings
    ADD CONSTRAINT app_settings_pkey PRIMARY KEY (key);


--
-- Name: backup_history backup_history_pkey; Type: CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.backup_history
    ADD CONSTRAINT backup_history_pkey PRIMARY KEY (id);


--
-- Name: connection_profiles connection_profiles_pkey; Type: CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.connection_profiles
    ADD CONSTRAINT connection_profiles_pkey PRIMARY KEY (id);


--
-- Name: database_migration_history database_migration_history_pkey; Type: CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.database_migration_history
    ADD CONSTRAINT database_migration_history_pkey PRIMARY KEY (migration_version);


--
-- Name: database_settings database_settings_pkey; Type: CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.database_settings
    ADD CONSTRAINT database_settings_pkey PRIMARY KEY (singleton);


--
-- Name: report_identity_settings report_identity_settings_pkey; Type: CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.report_identity_settings
    ADD CONSTRAINT report_identity_settings_pkey PRIMARY KEY (singleton);


--
-- Name: schema_migrations schema_migrations_pkey; Type: CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.schema_migrations
    ADD CONSTRAINT schema_migrations_pkey PRIMARY KEY (version);


--
-- Name: artifacts artifacts_pkey; Type: CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.artifacts
    ADD CONSTRAINT artifacts_pkey PRIMARY KEY (id);


--
-- Name: artifacts artifacts_release_id_operating_system_architecture_key; Type: CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.artifacts
    ADD CONSTRAINT artifacts_release_id_operating_system_architecture_key UNIQUE (release_id, operating_system, architecture);


--
-- Name: audit_log audit_log_pkey; Type: CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.audit_log
    ADD CONSTRAINT audit_log_pkey PRIMARY KEY (id);


--
-- Name: installations installations_pkey; Type: CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.installations
    ADD CONSTRAINT installations_pkey PRIMARY KEY (device_id);


--
-- Name: releases releases_pkey; Type: CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.releases
    ADD CONSTRAINT releases_pkey PRIMARY KEY (id);


--
-- Name: releases releases_version_key; Type: CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.releases
    ADD CONSTRAINT releases_version_key UNIQUE (version);


--
-- Name: ix_audit_logs_action_created_at; Type: INDEX; Schema: audit; Owner: -
--

CREATE INDEX ix_audit_logs_action_created_at ON audit.logs USING btree (action_type, created_at DESC);


--
-- Name: ix_audit_logs_created_at; Type: INDEX; Schema: audit; Owner: -
--

CREATE INDEX ix_audit_logs_created_at ON audit.logs USING btree (created_at);


--
-- Name: ix_audit_logs_created_at_desc; Type: INDEX; Schema: audit; Owner: -
--

CREATE INDEX ix_audit_logs_created_at_desc ON audit.logs USING btree (created_at DESC);


--
-- Name: ix_audit_logs_new_value_gin; Type: INDEX; Schema: audit; Owner: -
--

CREATE INDEX ix_audit_logs_new_value_gin ON audit.logs USING gin (new_value);


--
-- Name: ix_audit_logs_user; Type: INDEX; Schema: audit; Owner: -
--

CREATE INDEX ix_audit_logs_user ON audit.logs USING btree (user_id);


--
-- Name: ix_audit_logs_user_created_at; Type: INDEX; Schema: audit; Owner: -
--

CREATE INDEX ix_audit_logs_user_created_at ON audit.logs USING btree (user_id, created_at DESC);


--
-- Name: ix_users_role; Type: INDEX; Schema: auth; Owner: -
--

CREATE INDEX ix_users_role ON auth.users USING btree (role_id);


--
-- Name: ux_auth_users_username_normalized; Type: INDEX; Schema: auth; Owner: -
--

CREATE UNIQUE INDEX ux_auth_users_username_normalized ON auth.users USING btree (lower(TRIM(BOTH FROM username)));


--
-- Name: idx_note_attachments_note_id; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX idx_note_attachments_note_id ON core.note_attachments USING btree (note_id);


--
-- Name: ix_duplicate_incidents_classification_created; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_classification_created ON core.duplicate_incidents USING btree (classification, created_at DESC);


--
-- Name: ix_duplicate_incidents_company_created; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_company_created ON core.duplicate_incidents USING btree (company_id, created_at DESC);


--
-- Name: ix_duplicate_incidents_created_note; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_created_note ON core.duplicate_incidents USING btree (created_note_id);


--
-- Name: ix_duplicate_incidents_decided_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_decided_by ON core.duplicate_incidents USING btree (decided_by);


--
-- Name: ix_duplicate_incidents_factory; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_factory ON core.duplicate_incidents USING btree (factory_id);


--
-- Name: ix_duplicate_incidents_matched_note; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_matched_note ON core.duplicate_incidents USING btree (matched_note_id);


--
-- Name: ix_duplicate_incidents_note_type; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_note_type ON core.duplicate_incidents USING btree (note_type_id);


--
-- Name: ix_duplicate_incidents_production_line; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_duplicate_incidents_production_line ON core.duplicate_incidents USING btree (production_line_id);


--
-- Name: ix_factories_company_id; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_factories_company_id ON core.factories USING btree (company_id) WHERE (is_deleted = false);


--
-- Name: ix_note_create_operations_requester_created; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_create_operations_requester_created ON core.note_create_operations USING btree (requested_by, created_at DESC);


--
-- Name: ix_note_field_values_created_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_field_values_created_by ON core.note_field_values USING btree (created_by);


--
-- Name: ix_note_field_values_field; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_field_values_field ON core.note_field_values USING btree (field_id);


--
-- Name: ix_note_field_values_note; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_field_values_note ON core.note_field_values USING btree (note_id);


--
-- Name: ix_note_field_values_updated_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_field_values_updated_by ON core.note_field_values USING btree (updated_by);


--
-- Name: ix_note_links_related; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_links_related ON core.note_links USING btree (related_note_id);


--
-- Name: ix_note_links_source; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_links_source ON core.note_links USING btree (source_note_id);


--
-- Name: ix_note_review_history_note; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_review_history_note ON core.note_review_history USING btree (note_id, created_at DESC);


--
-- Name: ix_note_review_history_user; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_review_history_user ON core.note_review_history USING btree (user_id);


--
-- Name: ix_note_source_exit_refs_exit_note; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_source_exit_refs_exit_note ON core.note_source_exit_refs USING btree (exit_note_id);


--
-- Name: ix_note_source_invoices_source; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_source_invoices_source ON core.note_source_invoices USING btree (source_id, invoice_date, invoice_number);


--
-- Name: ix_note_sources_created_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_sources_created_by ON core.note_sources USING btree (created_by);


--
-- Name: ix_note_sources_type; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_note_sources_type ON core.note_sources USING btree (source_type);


--
-- Name: ix_notes_approved_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_approved_by ON core.notes USING btree (approved_by);


--
-- Name: ix_notes_arrival_at_active; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_arrival_at_active ON core.notes USING btree (arrival_at DESC) WHERE (is_deleted = false);


--
-- Name: ix_notes_company; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_company ON core.notes USING btree (company_id);


--
-- Name: ix_notes_company_writing_date_active; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_company_writing_date_active ON core.notes USING btree (company_id, writing_date) WHERE (is_deleted = false);


--
-- Name: ix_notes_created_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_created_by ON core.notes USING btree (created_by);


--
-- Name: ix_notes_deleted_at; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_deleted_at ON core.notes USING btree (deleted_at) WHERE (is_deleted = false);


--
-- Name: ix_notes_deleted_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_deleted_by ON core.notes USING btree (deleted_by) WHERE (is_deleted = true);


--
-- Name: ix_notes_duplicate_lookup; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_duplicate_lookup ON core.notes USING btree (company_id, note_type_id, note_number_normalized) WHERE (is_deleted = false);


--
-- Name: ix_notes_duplicate_numeric_key; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_duplicate_numeric_key ON core.notes USING btree (company_id, note_type_id, note_year, note_number_value) WHERE ((NOT is_deleted) AND (note_number_value IS NOT NULL) AND (note_year IS NOT NULL));


--
-- Name: ix_notes_factory_id; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_factory_id ON core.notes USING btree (factory_id) WHERE (is_deleted = false);


--
-- Name: ix_notes_invoice; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_invoice ON core.notes USING btree (company_id, is_invoice) WHERE ((NOT is_deleted) AND (is_invoice = true));


--
-- Name: ix_notes_loot; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_loot ON core.notes USING btree (company_id, is_loot) WHERE ((NOT is_deleted) AND (is_loot = true));


--
-- Name: ix_notes_production_line_id; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_production_line_id ON core.notes USING btree (production_line_id) WHERE (is_deleted = false);


--
-- Name: ix_notes_review_queue; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_review_queue ON core.notes USING btree (status, reviewer_id, arrival_at DESC) WHERE (is_deleted = false);


--
-- Name: ix_notes_reviewer; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_reviewer ON core.notes USING btree (reviewer_id);


--
-- Name: ix_notes_status; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_status ON core.notes USING btree (status);


--
-- Name: ix_notes_type; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_type ON core.notes USING btree (note_type_id);


--
-- Name: ix_notes_updated_by; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_updated_by ON core.notes USING btree (updated_by);


--
-- Name: ix_notes_writing_date; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_notes_writing_date ON core.notes USING btree (writing_date);


--
-- Name: ix_production_lines_factory_id; Type: INDEX; Schema: core; Owner: -
--

CREATE INDEX ix_production_lines_factory_id ON core.production_lines USING btree (factory_id) WHERE (is_deleted = false);


--
-- Name: ux_duplicate_incidents_company_duplicate_created_note; Type: INDEX; Schema: core; Owner: -
--

CREATE UNIQUE INDEX ux_duplicate_incidents_company_duplicate_created_note ON core.duplicate_incidents USING btree (created_note_id) WHERE ((classification)::text = 'COMPANY_DUPLICATE_NUMBER'::text);


--
-- Name: ux_factories_company_name_active; Type: INDEX; Schema: core; Owner: -
--

CREATE UNIQUE INDEX ux_factories_company_name_active ON core.factories USING btree (company_id, lower((name)::text)) WHERE (is_deleted = false);


--
-- Name: ux_note_links_pair; Type: INDEX; Schema: core; Owner: -
--

CREATE UNIQUE INDEX ux_note_links_pair ON core.note_links USING btree (LEAST(source_note_id, related_note_id), GREATEST(source_note_id, related_note_id));


--
-- Name: ux_production_lines_factory_name_active; Type: INDEX; Schema: core; Owner: -
--

CREATE UNIQUE INDEX ux_production_lines_factory_name_active ON core.production_lines USING btree (factory_id, lower((name)::text)) WHERE (is_deleted = false);


--
-- Name: ix_backup_history_created_by; Type: INDEX; Schema: system; Owner: -
--

CREATE INDEX ix_backup_history_created_by ON system.backup_history USING btree (created_by);


--
-- Name: ix_backup_history_started_at; Type: INDEX; Schema: system; Owner: -
--

CREATE INDEX ix_backup_history_started_at ON system.backup_history USING btree (started_at);


--
-- Name: ix_database_settings_updated_by; Type: INDEX; Schema: system; Owner: -
--

CREATE INDEX ix_database_settings_updated_by ON system.database_settings USING btree (last_updated_by);


--
-- Name: ix_report_identity_settings_updated_by; Type: INDEX; Schema: system; Owner: -
--

CREATE INDEX ix_report_identity_settings_updated_by ON system.report_identity_settings USING btree (updated_by);


--
-- Name: ix_updates_audit_log_release; Type: INDEX; Schema: updates; Owner: -
--

CREATE INDEX ix_updates_audit_log_release ON updates.audit_log USING btree (release_id);


--
-- Name: ix_updates_audit_log_user; Type: INDEX; Schema: updates; Owner: -
--

CREATE INDEX ix_updates_audit_log_user ON updates.audit_log USING btree (user_id);


--
-- Name: ix_updates_installations_checked; Type: INDEX; Schema: updates; Owner: -
--

CREATE INDEX ix_updates_installations_checked ON updates.installations USING btree (last_checked_at DESC);


--
-- Name: ix_updates_releases_created_by; Type: INDEX; Schema: updates; Owner: -
--

CREATE INDEX ix_updates_releases_created_by ON updates.releases USING btree (created_by);


--
-- Name: ix_updates_releases_published_by; Type: INDEX; Schema: updates; Owner: -
--

CREATE INDEX ix_updates_releases_published_by ON updates.releases USING btree (published_by);


--
-- Name: ix_updates_releases_status_published; Type: INDEX; Schema: updates; Owner: -
--

CREATE INDEX ix_updates_releases_status_published ON updates.releases USING btree (status, published_at DESC);


--
-- Name: vw_note_details _RETURN; Type: RULE; Schema: reporting; Owner: -
--

CREATE OR REPLACE VIEW reporting.vw_note_details AS
 SELECT n.id AS note_id,
    n.note_number,
    nt.id AS note_type_id,
    nt.name_ar AS note_type_name,
    c.id AS company_id,
    c.name AS company_name,
    n.factory_id,
    f.name AS factory_name,
    n.production_line_id,
    pl.name AS production_line_name,
    n.writing_date,
    n.arrival_at,
    n.status,
    n.image_path,
    n.created_at,
    n.created_by,
    creator.full_name AS created_by_name,
    n.review_started_at,
    n.approved_at,
    n.approved_by,
    approver.full_name AS approved_by_name,
    (count(ntf.field_id))::integer AS total_fields,
    (count(nfv.field_id) FILTER (WHERE (nfv.field_value = 1)))::integer AS active_count,
    (count(nfv.field_id) FILTER (WHERE ((nfv.field_value >= 2) AND (nfv.field_value <= 6))))::integer AS inactive_count,
    (count(nfv.field_id) FILTER (WHERE (nfv.field_value = 0)))::integer AS unknown_count,
    COALESCE(round((((count(nfv.field_id) FILTER (WHERE (nfv.field_value = 1)))::numeric * (100)::numeric) / (NULLIF(count(nfv.field_id) FILTER (WHERE (nfv.field_value <> 0)), 0))::numeric), 2), (0)::numeric) AS accuracy_percentage,
    COALESCE(round((((count(nfv.field_id) FILTER (WHERE ((nfv.field_value >= 2) AND (nfv.field_value <= 6))))::numeric * (100)::numeric) / (NULLIF(count(nfv.field_id) FILTER (WHERE (nfv.field_value <> 0)), 0))::numeric), 2), (0)::numeric) AS error_percentage
   FROM (((((((((core.notes n
     JOIN core.note_types nt ON ((nt.id = n.note_type_id)))
     JOIN core.companies c ON ((c.id = n.company_id)))
     LEFT JOIN core.factories f ON ((f.id = n.factory_id)))
     LEFT JOIN core.production_lines pl ON ((pl.id = n.production_line_id)))
     LEFT JOIN auth.users creator ON ((creator.id = n.created_by)))
     LEFT JOIN auth.users approver ON ((approver.id = n.approved_by)))
     JOIN core.note_type_fields ntf ON (((ntf.note_type_id = n.note_type_id) AND ntf.is_active AND (NOT ntf.is_deleted))))
     JOIN core.note_fields nf ON (((nf.id = ntf.field_id) AND nf.is_active AND (NOT nf.is_deleted))))
     LEFT JOIN core.note_field_values nfv ON (((nfv.note_id = n.id) AND (nfv.field_id = ntf.field_id))))
  WHERE (n.is_deleted = false)
  GROUP BY n.id, nt.id, nt.name_ar, c.id, c.name, f.id, f.name, pl.id, pl.name, creator.full_name, approver.full_name;


--
-- Name: roles trg_roles_set_updated_at; Type: TRIGGER; Schema: auth; Owner: -
--

CREATE TRIGGER trg_roles_set_updated_at BEFORE UPDATE ON auth.roles FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: users trg_users_set_updated_at; Type: TRIGGER; Schema: auth; Owner: -
--

CREATE TRIGGER trg_users_set_updated_at BEFORE UPDATE ON auth.users FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: companies trg_companies_protect_soft_delete; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_companies_protect_soft_delete BEFORE UPDATE OF is_deleted ON core.companies FOR EACH ROW EXECUTE FUNCTION core.protect_company_soft_delete();


--
-- Name: companies trg_companies_set_updated_at; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_companies_set_updated_at BEFORE UPDATE ON core.companies FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: factories trg_factories_protect_production_lines; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_factories_protect_production_lines BEFORE UPDATE OF company_id, is_deleted ON core.factories FOR EACH ROW EXECUTE FUNCTION core.protect_factory_with_production_lines();


--
-- Name: factories trg_factories_protect_used_relation; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_factories_protect_used_relation BEFORE UPDATE OF company_id, is_deleted ON core.factories FOR EACH ROW EXECUTE FUNCTION core.protect_used_factory_relation();


--
-- Name: note_links trg_legacy_note_link_to_source; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_legacy_note_link_to_source AFTER INSERT OR DELETE ON core.note_links FOR EACH ROW EXECUTE FUNCTION core.sync_legacy_note_link_to_source();


--
-- Name: note_field_values trg_note_field_values_set_updated_at; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_field_values_set_updated_at BEFORE UPDATE ON core.note_field_values FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: note_field_values trg_note_field_values_validate; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_field_values_validate BEFORE INSERT OR UPDATE ON core.note_field_values FOR EACH ROW EXECUTE FUNCTION core.validate_note_field_value();


--
-- Name: note_fields trg_note_fields_set_updated_at; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_fields_set_updated_at BEFORE UPDATE ON core.note_fields FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: note_links trg_note_links_validate; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_links_validate BEFORE INSERT OR UPDATE OF source_note_id, related_note_id ON core.note_links FOR EACH ROW EXECUTE FUNCTION core.validate_note_link();


--
-- Name: note_source_exit_refs trg_note_source_exit_refs_validate; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_source_exit_refs_validate BEFORE INSERT OR UPDATE OF source_id, exit_note_id ON core.note_source_exit_refs FOR EACH ROW EXECUTE FUNCTION core.validate_note_source_exit_ref();


--
-- Name: note_source_invoices trg_note_source_invoices_validate; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_source_invoices_validate BEFORE INSERT OR UPDATE OF source_id ON core.note_source_invoices FOR EACH ROW EXECUTE FUNCTION core.validate_note_source_payload();


--
-- Name: note_source_loot trg_note_source_loot_validate; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_source_loot_validate BEFORE INSERT OR UPDATE OF source_id ON core.note_source_loot FOR EACH ROW EXECUTE FUNCTION core.validate_note_source_payload();


--
-- Name: note_sources trg_note_sources_validate; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_sources_validate BEFORE INSERT OR UPDATE OF note_id, source_type, other_description ON core.note_sources FOR EACH ROW EXECUTE FUNCTION core.validate_note_source();


--
-- Name: note_type_fields trg_note_type_fields_set_updated_at; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_type_fields_set_updated_at BEFORE UPDATE ON core.note_type_fields FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: note_types trg_note_types_set_updated_at; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_note_types_set_updated_at BEFORE UPDATE ON core.note_types FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: notes trg_notes_set_updated_at; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_notes_set_updated_at BEFORE UPDATE ON core.notes FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: notes trg_notes_validate_factory_company; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_notes_validate_factory_company BEFORE INSERT OR UPDATE OF company_id, factory_id ON core.notes FOR EACH ROW EXECUTE FUNCTION core.validate_note_factory_company();


--
-- Name: notes trg_notes_validate_factory_production_line; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_notes_validate_factory_production_line BEFORE INSERT OR UPDATE OF factory_id, production_line_id ON core.notes FOR EACH ROW EXECUTE FUNCTION core.validate_note_factory_production_line();


--
-- Name: notes trg_notes_validate_loot_invoice; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_notes_validate_loot_invoice BEFORE INSERT OR UPDATE OF note_type_id, is_loot, is_invoice ON core.notes FOR EACH ROW EXECUTE FUNCTION core.validate_note_loot_invoice_flags();


--
-- Name: production_lines trg_production_lines_protect_used; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_production_lines_protect_used BEFORE UPDATE OF factory_id, is_deleted ON core.production_lines FOR EACH ROW EXECUTE FUNCTION core.protect_used_production_line();


--
-- Name: note_source_exit_refs trg_source_ref_to_legacy_note_link; Type: TRIGGER; Schema: core; Owner: -
--

CREATE TRIGGER trg_source_ref_to_legacy_note_link AFTER INSERT OR DELETE ON core.note_source_exit_refs FOR EACH ROW EXECUTE FUNCTION core.sync_source_ref_to_legacy_note_link();


--
-- Name: app_settings trg_app_settings_set_updated_at; Type: TRIGGER; Schema: system; Owner: -
--

CREATE TRIGGER trg_app_settings_set_updated_at BEFORE UPDATE ON system.app_settings FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: connection_profiles trg_connection_profiles_set_updated_at; Type: TRIGGER; Schema: system; Owner: -
--

CREATE TRIGGER trg_connection_profiles_set_updated_at BEFORE UPDATE ON system.connection_profiles FOR EACH ROW EXECUTE FUNCTION common.set_updated_at();


--
-- Name: logs logs_user_id_fkey; Type: FK CONSTRAINT; Schema: audit; Owner: -
--

ALTER TABLE ONLY audit.logs
    ADD CONSTRAINT logs_user_id_fkey FOREIGN KEY (user_id) REFERENCES auth.users(id);


--
-- Name: users users_role_id_fkey; Type: FK CONSTRAINT; Schema: auth; Owner: -
--

ALTER TABLE ONLY auth.users
    ADD CONSTRAINT users_role_id_fkey FOREIGN KEY (role_id) REFERENCES auth.roles(id);


--
-- Name: duplicate_incidents duplicate_incidents_company_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_company_id_fkey FOREIGN KEY (company_id) REFERENCES core.companies(id);


--
-- Name: duplicate_incidents duplicate_incidents_created_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_created_note_id_fkey FOREIGN KEY (created_note_id) REFERENCES core.notes(id);


--
-- Name: duplicate_incidents duplicate_incidents_decided_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_decided_by_fkey FOREIGN KEY (decided_by) REFERENCES auth.users(id);


--
-- Name: duplicate_incidents duplicate_incidents_factory_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_factory_id_fkey FOREIGN KEY (factory_id) REFERENCES core.factories(id);


--
-- Name: duplicate_incidents duplicate_incidents_matched_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_matched_note_id_fkey FOREIGN KEY (matched_note_id) REFERENCES core.notes(id);


--
-- Name: duplicate_incidents duplicate_incidents_note_type_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_note_type_id_fkey FOREIGN KEY (note_type_id) REFERENCES core.note_types(id);


--
-- Name: duplicate_incidents duplicate_incidents_production_line_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.duplicate_incidents
    ADD CONSTRAINT duplicate_incidents_production_line_id_fkey FOREIGN KEY (production_line_id) REFERENCES core.production_lines(id);


--
-- Name: factories factories_company_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.factories
    ADD CONSTRAINT factories_company_id_fkey FOREIGN KEY (company_id) REFERENCES core.companies(id);


--
-- Name: note_create_operations fk_note_create_operations_note; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_create_operations
    ADD CONSTRAINT fk_note_create_operations_note FOREIGN KEY (note_id) REFERENCES core.notes(id) DEFERRABLE INITIALLY DEFERRED;


--
-- Name: note_attachments note_attachments_created_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_attachments
    ADD CONSTRAINT note_attachments_created_by_fkey FOREIGN KEY (created_by) REFERENCES auth.users(id);


--
-- Name: note_attachments note_attachments_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_attachments
    ADD CONSTRAINT note_attachments_note_id_fkey FOREIGN KEY (note_id) REFERENCES core.notes(id) ON DELETE CASCADE;


--
-- Name: note_create_operations note_create_operations_requested_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_create_operations
    ADD CONSTRAINT note_create_operations_requested_by_fkey FOREIGN KEY (requested_by) REFERENCES auth.users(id);


--
-- Name: note_field_values note_field_values_created_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_field_values
    ADD CONSTRAINT note_field_values_created_by_fkey FOREIGN KEY (created_by) REFERENCES auth.users(id);


--
-- Name: note_field_values note_field_values_field_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_field_values
    ADD CONSTRAINT note_field_values_field_id_fkey FOREIGN KEY (field_id) REFERENCES core.note_fields(id);


--
-- Name: note_field_values note_field_values_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_field_values
    ADD CONSTRAINT note_field_values_note_id_fkey FOREIGN KEY (note_id) REFERENCES core.notes(id) ON DELETE CASCADE;


--
-- Name: note_field_values note_field_values_updated_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_field_values
    ADD CONSTRAINT note_field_values_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES auth.users(id);


--
-- Name: note_links note_links_created_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_links
    ADD CONSTRAINT note_links_created_by_fkey FOREIGN KEY (created_by) REFERENCES auth.users(id);


--
-- Name: note_links note_links_related_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_links
    ADD CONSTRAINT note_links_related_note_id_fkey FOREIGN KEY (related_note_id) REFERENCES core.notes(id) ON DELETE CASCADE;


--
-- Name: note_links note_links_source_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_links
    ADD CONSTRAINT note_links_source_note_id_fkey FOREIGN KEY (source_note_id) REFERENCES core.notes(id) ON DELETE CASCADE;


--
-- Name: note_review_history note_review_history_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_review_history
    ADD CONSTRAINT note_review_history_note_id_fkey FOREIGN KEY (note_id) REFERENCES core.notes(id) ON DELETE CASCADE;


--
-- Name: note_review_history note_review_history_user_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_review_history
    ADD CONSTRAINT note_review_history_user_id_fkey FOREIGN KEY (user_id) REFERENCES auth.users(id);


--
-- Name: note_source_exit_refs note_source_exit_refs_exit_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_exit_refs
    ADD CONSTRAINT note_source_exit_refs_exit_note_id_fkey FOREIGN KEY (exit_note_id) REFERENCES core.notes(id) ON DELETE RESTRICT;


--
-- Name: note_source_exit_refs note_source_exit_refs_source_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_exit_refs
    ADD CONSTRAINT note_source_exit_refs_source_id_fkey FOREIGN KEY (source_id) REFERENCES core.note_sources(id) ON DELETE CASCADE;


--
-- Name: note_source_invoices note_source_invoices_source_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_invoices
    ADD CONSTRAINT note_source_invoices_source_id_fkey FOREIGN KEY (source_id) REFERENCES core.note_sources(id) ON DELETE CASCADE;


--
-- Name: note_source_loot note_source_loot_source_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_source_loot
    ADD CONSTRAINT note_source_loot_source_id_fkey FOREIGN KEY (source_id) REFERENCES core.note_sources(id) ON DELETE CASCADE;


--
-- Name: note_sources note_sources_created_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_sources
    ADD CONSTRAINT note_sources_created_by_fkey FOREIGN KEY (created_by) REFERENCES auth.users(id);


--
-- Name: note_sources note_sources_note_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_sources
    ADD CONSTRAINT note_sources_note_id_fkey FOREIGN KEY (note_id) REFERENCES core.notes(id) ON DELETE CASCADE;


--
-- Name: note_type_fields note_type_fields_field_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_type_fields
    ADD CONSTRAINT note_type_fields_field_id_fkey FOREIGN KEY (field_id) REFERENCES core.note_fields(id);


--
-- Name: note_type_fields note_type_fields_note_type_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.note_type_fields
    ADD CONSTRAINT note_type_fields_note_type_id_fkey FOREIGN KEY (note_type_id) REFERENCES core.note_types(id);


--
-- Name: notes notes_approved_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_approved_by_fkey FOREIGN KEY (approved_by) REFERENCES auth.users(id);


--
-- Name: notes notes_company_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_company_id_fkey FOREIGN KEY (company_id) REFERENCES core.companies(id);


--
-- Name: notes notes_created_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_created_by_fkey FOREIGN KEY (created_by) REFERENCES auth.users(id);


--
-- Name: notes notes_deleted_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_deleted_by_fkey FOREIGN KEY (deleted_by) REFERENCES auth.users(id);


--
-- Name: notes notes_factory_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_factory_id_fkey FOREIGN KEY (factory_id) REFERENCES core.factories(id);


--
-- Name: notes notes_note_type_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_note_type_id_fkey FOREIGN KEY (note_type_id) REFERENCES core.note_types(id);


--
-- Name: notes notes_production_line_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_production_line_id_fkey FOREIGN KEY (production_line_id) REFERENCES core.production_lines(id);


--
-- Name: notes notes_reviewer_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_reviewer_id_fkey FOREIGN KEY (reviewer_id) REFERENCES auth.users(id);


--
-- Name: notes notes_updated_by_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.notes
    ADD CONSTRAINT notes_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES auth.users(id);


--
-- Name: production_lines production_lines_factory_id_fkey; Type: FK CONSTRAINT; Schema: core; Owner: -
--

ALTER TABLE ONLY core.production_lines
    ADD CONSTRAINT production_lines_factory_id_fkey FOREIGN KEY (factory_id) REFERENCES core.factories(id);


--
-- Name: backup_history backup_history_created_by_fkey; Type: FK CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.backup_history
    ADD CONSTRAINT backup_history_created_by_fkey FOREIGN KEY (created_by) REFERENCES auth.users(id);


--
-- Name: database_settings database_settings_last_updated_by_fkey; Type: FK CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.database_settings
    ADD CONSTRAINT database_settings_last_updated_by_fkey FOREIGN KEY (last_updated_by) REFERENCES auth.users(id);


--
-- Name: report_identity_settings report_identity_settings_updated_by_fkey; Type: FK CONSTRAINT; Schema: system; Owner: -
--

ALTER TABLE ONLY system.report_identity_settings
    ADD CONSTRAINT report_identity_settings_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES auth.users(id);


--
-- Name: artifacts artifacts_release_id_fkey; Type: FK CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.artifacts
    ADD CONSTRAINT artifacts_release_id_fkey FOREIGN KEY (release_id) REFERENCES updates.releases(id) ON DELETE CASCADE;


--
-- Name: audit_log audit_log_release_id_fkey; Type: FK CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.audit_log
    ADD CONSTRAINT audit_log_release_id_fkey FOREIGN KEY (release_id) REFERENCES updates.releases(id);


--
-- Name: audit_log audit_log_user_id_fkey; Type: FK CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.audit_log
    ADD CONSTRAINT audit_log_user_id_fkey FOREIGN KEY (user_id) REFERENCES auth.users(id);


--
-- Name: releases releases_created_by_fkey; Type: FK CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.releases
    ADD CONSTRAINT releases_created_by_fkey FOREIGN KEY (created_by) REFERENCES auth.users(id);


--
-- Name: releases releases_published_by_fkey; Type: FK CONSTRAINT; Schema: updates; Owner: -
--

ALTER TABLE ONLY updates.releases
    ADD CONSTRAINT releases_published_by_fkey FOREIGN KEY (published_by) REFERENCES auth.users(id);


--
-- PostgreSQL database dump complete
--

\unrestrict giPczMLaYjNZF6NizZJSGuSrWHVfnsNVmZpOAeQeUau7hrVhMNS02Gf9zNV9l1l

