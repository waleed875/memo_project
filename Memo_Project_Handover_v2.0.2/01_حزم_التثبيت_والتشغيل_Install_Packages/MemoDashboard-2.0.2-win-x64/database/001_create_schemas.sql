create extension if not exists pgcrypto;

create schema if not exists common;
create schema if not exists core;
create schema if not exists auth;
create schema if not exists audit;
create schema if not exists reporting;
create schema if not exists system;

create table if not exists system.schema_migrations (
    version varchar(30) primary key,
    script_name text not null,
    applied_at timestamp not null default now()
);
