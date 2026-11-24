alter table system.backup_history
    drop constraint if exists ck_backup_history_status,
    add constraint ck_backup_history_status
        check (status in ('RUNNING', 'SUCCESS', 'FAILED', 'CANCELLED'));
