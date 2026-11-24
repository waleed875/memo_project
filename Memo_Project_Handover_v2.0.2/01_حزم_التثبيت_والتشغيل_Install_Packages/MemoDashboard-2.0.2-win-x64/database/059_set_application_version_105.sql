-- Migration 059: align database compatibility metadata with the 1.0.5 release.
-- Additive metadata-only migration. Migrations 001-058 remain immutable.

update system.database_settings
   set compatible_application_version = '1.0.5',
       last_updated_at = now()
 where singleton = true;
