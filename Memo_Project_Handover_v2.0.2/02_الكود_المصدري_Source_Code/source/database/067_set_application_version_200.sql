-- Migration 067: align database compatibility metadata with the 2.0.0 release.
-- Additive metadata-only migration. Migrations 001-066 remain immutable.

update system.database_settings
   set compatible_application_version = '2.0.0',
       last_updated_at = now()
 where singleton = true;
