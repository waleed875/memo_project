-- Read-only Dashboard credential for PostgreSQL.
-- Run this script against the audit_memo database from a controlled provisioning host.
-- No password is stored in this file. Set it with a secret manager or answer the
-- interactive \\password prompt after creating the role.

\\set ON_ERROR_STOP on

DO $$
BEGIN
    BEGIN
        CREATE ROLE audit_memo_dashboard_reader
            LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    EXCEPTION WHEN duplicate_object THEN
        NULL;
    END;
    ALTER ROLE audit_memo_dashboard_reader
        LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
END
$$;

GRANT CONNECT ON DATABASE audit_memo TO audit_memo_dashboard_reader;
GRANT USAGE ON SCHEMA auth, core, reporting TO audit_memo_dashboard_reader;
GRANT SELECT ON TABLE
    auth.users,
    auth.roles,
    core.companies,
    core.factories,
    core.production_lines,
    core.note_types,
    core.note_fields,
    core.note_type_fields,
    core.notes,
    core.note_field_values,
    core.note_links,
    core.duplicate_incidents
    TO audit_memo_dashboard_reader;

DO $$
DECLARE
    object_name text;
BEGIN
    FOR object_name IN
        SELECT format('%I.%I', n.nspname, c.relname)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'reporting'
          AND c.relkind IN ('v', 'm')
    LOOP
        EXECUTE format('GRANT SELECT ON TABLE %s TO audit_memo_dashboard_reader', object_name);
    END LOOP;

    FOR object_name IN
        SELECT p.oid::regprocedure::text
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'reporting'
    LOOP
        EXECUTE format('GRANT EXECUTE ON FUNCTION %s TO audit_memo_dashboard_reader', object_name);
    END LOOP;
END
$$;

REVOKE INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER
    ON ALL TABLES IN SCHEMA auth, core, reporting
    FROM audit_memo_dashboard_reader;

-- Provision the password outside this file, for example:
--   \\password audit_memo_dashboard_reader
