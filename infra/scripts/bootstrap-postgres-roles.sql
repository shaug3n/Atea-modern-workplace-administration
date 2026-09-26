\set ON_ERROR_STOP on

\getenv migration_password MIGRATION_PASSWORD
\getenv runtime_password RUNTIME_PASSWORD
\getenv database_name PGDATABASE

-- Run as the PostgreSQL server administrator for the application database.
-- Supply both passwords through one-time environment variables; never store them in this file.
SELECT 'CREATE ROLE workplace_migrator LOGIN PASSWORD ' || quote_literal(:'migration_password')
WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'workplace_migrator')
\gexec
ALTER ROLE workplace_migrator PASSWORD :'migration_password';
SELECT 'CREATE ROLE workplace_runtime LOGIN PASSWORD ' || quote_literal(:'runtime_password')
WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'workplace_runtime')
\gexec
ALTER ROLE workplace_runtime PASSWORD :'runtime_password';

GRANT CONNECT ON DATABASE :"database_name" TO workplace_migrator, workplace_runtime;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO workplace_migrator;
GRANT USAGE ON SCHEMA public TO workplace_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO workplace_runtime;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO workplace_runtime;

\setenv PGUSER workplace_migrator
\setenv PGPASSWORD :migration_password
\connect :"database_name" workplace_migrator
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO workplace_runtime;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO workplace_runtime;
