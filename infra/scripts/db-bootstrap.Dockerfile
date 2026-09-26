FROM postgres:16-alpine
COPY infra/scripts/bootstrap-postgres-roles.sql /usr/local/share/bootstrap-postgres-roles.sql
ENTRYPOINT ["psql", "--no-psqlrc", "-v", "ON_ERROR_STOP=1", "-f", "/usr/local/share/bootstrap-postgres-roles.sql"]
