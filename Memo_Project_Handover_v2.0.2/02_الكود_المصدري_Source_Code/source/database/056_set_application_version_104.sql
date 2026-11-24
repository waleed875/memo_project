-- Migration 056: align database compatibility metadata with the 1.0.4 SemVer release.
-- Additive metadata-only migration. Migrations 001-055 remain immutable.

update system.database_settings
   set compatible_application_version = '1.0.4',
       last_updated_at = now()
 where singleton = true;

