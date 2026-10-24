#!/bin/sh
set -eu
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=ON_ERROR_STOP=1 \
  --set=command_password="$COMMAND_DB_PASSWORD" \
  --set=query_password="$QUERY_DB_PASSWORD" \
  --file=/schema/001-init.sql
