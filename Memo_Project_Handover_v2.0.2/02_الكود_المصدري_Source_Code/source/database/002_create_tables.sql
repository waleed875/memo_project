create table if not exists auth.roles (
    id uuid primary key default gen_random_uuid(),
    code varchar(50) unique not null,
    name_ar varchar(100) not null,
    is_active boolean not null default true,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create table if not exists auth.users (
    id uuid primary key default gen_random_uuid(),
    username varchar(100) unique not null,
    password_hash text not null,
    full_name varchar(255) not null,
    role_id uuid not null references auth.roles(id),
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create table if not exists core.companies (
    id uuid primary key default gen_random_uuid(),
    name varchar(255) not null,
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create table if not exists core.note_types (
    id uuid primary key default gen_random_uuid(),
    code varchar(50) unique not null,
    name_ar varchar(100) not null,
    description text,
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create table if not exists core.note_fields (
    id uuid primary key default gen_random_uuid(),
    code varchar(80) unique not null,
    name_ar varchar(150) not null,
    description text,
    default_sort_order int not null default 0,
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create table if not exists core.note_type_fields (
    id uuid primary key default gen_random_uuid(),
    note_type_id uuid not null references core.note_types(id),
    field_id uuid not null references core.note_fields(id),
    sort_order int not null,
    is_required boolean not null default true,
    is_active boolean not null default true,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now(),
    unique(note_type_id, field_id)
);

create table if not exists core.notes (
    id uuid primary key default gen_random_uuid(),
    note_number varchar(100) not null,
    note_type_id uuid not null references core.note_types(id),
    company_id uuid not null references core.companies(id),
    writing_date date not null,
    arrival_at timestamp not null default now(),
    image_path text,
    file_path text,
    notes text,
    status varchar(50) not null default 'DRAFT',
    created_by uuid references auth.users(id),
    updated_by uuid references auth.users(id),
    approved_by uuid references auth.users(id),
    approved_at timestamp,
    is_deleted boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create table if not exists core.note_field_values (
    id uuid primary key default gen_random_uuid(),
    note_id uuid not null references core.notes(id) on delete cascade,
    field_id uuid not null references core.note_fields(id),
    field_value smallint not null,
    inactive_reason text,
    created_by uuid references auth.users(id),
    updated_by uuid references auth.users(id),
    created_at timestamp not null default now(),
    updated_at timestamp not null default now(),
    unique(note_id, field_id)
);

create table if not exists audit.logs (
    id bigserial primary key,
    user_id uuid references auth.users(id),
    action_type varchar(100) not null,
    schema_name varchar(100),
    table_name varchar(100),
    record_id uuid,
    description text,
    old_value jsonb,
    new_value jsonb,
    created_at timestamp not null default now()
);

create table if not exists system.connection_profiles (
    id uuid primary key default gen_random_uuid(),
    name varchar(100) not null,
    profile_type varchar(20) not null,
    host varchar(255) not null,
    port int not null default 5432,
    database_name varchar(255) not null,
    username varchar(255) not null,
    encrypted_password text not null,
    is_active boolean not null default false,
    created_at timestamp not null default now(),
    updated_at timestamp not null default now()
);

create table if not exists system.app_settings (
    key varchar(100) primary key,
    value text,
    updated_at timestamp not null default now()
);

create table if not exists system.backup_history (
    id uuid primary key default gen_random_uuid(),
    backup_type varchar(50) not null,
    source_profile_name varchar(100),
    target_profile_name varchar(100),
    file_path text,
    status varchar(50) not null,
    started_at timestamp not null default now(),
    finished_at timestamp,
    error_message text,
    created_by uuid references auth.users(id)
);
