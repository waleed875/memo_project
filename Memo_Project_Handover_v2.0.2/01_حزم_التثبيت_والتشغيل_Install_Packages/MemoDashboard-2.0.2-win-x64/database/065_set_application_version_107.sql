-- Migration 065: align database compatibility metadata with the 1.0.7 release.
-- Additive metadata-only migration. Migrations 001-064 remain immutable.

update system.database_settings
   set compatible_application_version = '1.0.7',
       last_updated_at = now()
 where singleton = true;
