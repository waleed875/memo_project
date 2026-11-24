-- Migration 053: bump the application compatibility version to 1.0.3.1.
-- The desktop app release is now 1.0.3.1 (see src/AuditMemoSystem.App);
-- the DB metadata must track the app version so older clients (1.0.3 and
-- below) hit the "database is newer than the program" gate and update first.
-- Additive: updates a single settings row; does not touch migrations 001-052.

update system.database_settings
   set compatible_application_version = '1.0.3.1',
       last_updated_at = now()
 where singleton = true;