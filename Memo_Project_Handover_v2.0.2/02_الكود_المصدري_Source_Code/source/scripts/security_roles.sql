-- TASK-06 controlled PostgreSQL role provisioning.
-- Run only from the maintenance/provisioning host, never from the Desktop client.
-- This file contains no credentials. Set/rotate passwords with the secret manager or
-- with psql's interactive \\password command. Database name is audit_memo.

DO $$
DECLARE
    role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY[
        'audit_memo_dashboard_reader',
        'audit_memo_update_writer',
        'audit_memo_runtime',
        'audit_memo_migrator',
        'audit_memo_backup'
    ] LOOP
        BEGIN
            EXECUTE format(
                'CREATE ROLE %I LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION',
                role_name);
        EXCEPTION WHEN duplicate_object THEN
            NULL;
        END;
        EXECUTE format(
            'ALTER ROLE %I LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION',
            role_name);
    END LOOP;
END
$$;

GRANT CONNECT ON DATABASE audit_memo
    TO audit_memo_dashboard_reader,
       audit_memo_update_writer,
       audit_memo_runtime,
       audit_memo_migrator,
       audit_memo_backup;

REVOKE CREATE ON DATABASE audit_memo
    FROM audit_memo_dashboard_reader,
         audit_memo_update_writer,
         audit_memo_runtime,
         audit_memo_backup;
REVOKE CREATE ON SCHEMA public
    FROM audit_memo_dashboard_reader,
         audit_memo_update_writer,
         audit_memo_runtime,
         audit_memo_backup;

-- Dashboard: read-only data and reporting routines. It has no updates/core writes.
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

-- Update service: writes only the updates schema, never core/auth/audit.
GRANT USAGE ON SCHEMA updates TO audit_memo_update_writer;
GRANT SELECT, INSERT, UPDATE ON TABLE
    updates.releases,
    updates.artifacts,
    updates.installations,
    updates.audit_log
    TO audit_memo_update_writer;
REVOKE DELETE, TRUNCATE, REFERENCES, TRIGGER
    ON TABLE updates.releases, updates.artifacts, updates.installations, updates.audit_log
    FROM audit_memo_update_writer;

-- Desktop daily runtime: operational CRUD plus audit append/read. No migration history
-- and no UPDATE/DELETE/TRUNCATE on audit.logs.
GRANT USAGE ON SCHEMA auth, core, audit, reporting, system TO audit_memo_runtime;
GRANT SELECT ON TABLE auth.roles TO audit_memo_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE auth.users TO audit_memo_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    core.companies,
    core.factories,
    core.production_lines,
    core.note_types,
    core.note_fields,
    core.note_type_fields,
    core.notes,
    core.note_field_values,
    core.note_links,
    core.note_review_history,
    core.note_create_operations,
    core.duplicate_incidents,
    core.duplicate_incidents_archive
    TO audit_memo_runtime;
GRANT SELECT, INSERT ON TABLE audit.logs TO audit_memo_runtime;
REVOKE UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER
    ON TABLE audit.logs FROM audit_memo_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE
    system.app_settings,
    system.connection_profiles,
    system.backup_history,
    system.report_identity_settings,
    system.database_settings
    TO audit_memo_runtime;

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
        EXECUTE format('GRANT SELECT ON TABLE %s TO audit_memo_runtime', object_name);
    END LOOP;

    FOR object_name IN
        SELECT p.oid::regprocedure::text
        FROM pg_proc p
        JOIN pg_namespace n ON n.oid = p.pronamespace
        WHERE n.nspname = 'reporting'
    LOOP
        EXECUTE format('GRANT EXECUTE ON FUNCTION %s TO audit_memo_runtime', object_name);
    END LOOP;

    FOR object_name IN
        SELECT format('%I.%I', n.nspname, c.relname)
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname IN ('auth', 'core', 'audit', 'system')
          AND c.relkind = 'S'
    LOOP
        EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE %s TO audit_memo_runtime', object_name);
    END LOOP;
END
$$;

-- Migrator: maintenance-only DDL/data rights. It is not a SUPERUSER and cannot create
-- databases or roles. The controlled runner must own the migration objects or be backed
-- by a DBA-approved ownership handoff.
GRANT USAGE, CREATE ON SCHEMA auth, core, audit, reporting, system, updates
    TO audit_memo_migrator;
GRANT CREATE ON DATABASE audit_memo TO audit_memo_migrator;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA auth, core, audit, reporting, system, updates
    TO audit_memo_migrator;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA auth, core, audit, reporting, system, updates
    TO audit_memo_migrator;
GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA auth, core, audit, reporting, system, updates
    TO audit_memo_migrator;

-- Backup operator: read-only extraction. Restore is deliberately performed with the
-- separate migrator/DBA credential, never with the daily runtime or dashboard secret.
GRANT USAGE ON SCHEMA auth, core, audit, reporting, system, updates
    TO audit_memo_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA auth, core, audit, reporting, system, updates
    TO audit_memo_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA auth, core, audit, reporting, system, updates
    TO audit_memo_backup;

-- New objects must be reviewed and granted explicitly in the next controlled migration.
-- Do not add a broad "GRANT ... ON ALL TABLES" to the runtime or dashboard roles.
