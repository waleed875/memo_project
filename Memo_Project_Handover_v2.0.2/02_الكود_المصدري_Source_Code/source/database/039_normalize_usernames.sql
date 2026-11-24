do $$
begin
    if exists (
        select 1
        from auth.users
        group by lower(trim(username))
        having count(*) > 1
    ) then
        raise exception 'Cannot enforce normalized username uniqueness until duplicate usernames are resolved.';
    end if;
end $$;

update auth.users
set username = lower(trim(username))
where username <> lower(trim(username));

create unique index if not exists ux_auth_users_username_normalized
    on auth.users (lower(trim(username)));
