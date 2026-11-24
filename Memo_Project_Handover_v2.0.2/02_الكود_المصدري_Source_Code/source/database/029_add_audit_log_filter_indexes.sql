create index if not exists ix_audit_logs_created_at_desc on audit.logs(created_at desc);
create index if not exists ix_audit_logs_user_created_at on audit.logs(user_id, created_at desc);
create index if not exists ix_audit_logs_action_created_at on audit.logs(action_type, created_at desc);
create index if not exists ix_audit_logs_new_value_gin on audit.logs using gin(new_value);
