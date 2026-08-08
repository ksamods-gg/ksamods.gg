#!/bin/sh
# Applies pending migrations, then exits. Runs on every deploy, so it must be a no-op when the
# schema is already present.
#
# Everything is echoed, because this runs unattended and a deploy that fails here gives you the
# container log and nothing else.

set -eu

: "${PGHOST:=postgres}"
: "${PGUSER:=ksamods}"
: "${PGDATABASE:=ksamods}"

export PGHOST PGUSER PGDATABASE

echo "migrate: host=$PGHOST db=$PGDATABASE user=$PGUSER"

if [ -z "${PGPASSWORD:-}" ]; then
  echo "migrate: FATAL - PGPASSWORD is empty. Check SERVICE_PASSWORD_POSTGRES is set." >&2
  exit 1
fi

if [ ! -d /migrations ]; then
  echo "migrate: FATAL - /migrations is missing from the image." >&2
  exit 1
fi

# Postgres reports healthy once it accepts connections, which can still be a moment before it
# answers queries. A short retry here is cheaper than a flaky deploy.
attempt=1
until psql -tAc 'select 1' >/dev/null 2>&1; do
  if [ "$attempt" -ge 30 ]; then
    echo "migrate: FATAL - could not reach the database after $attempt attempts." >&2
    psql -tAc 'select 1' || true
    exit 1
  fi
  echo "migrate: waiting for the database (attempt $attempt)"
  attempt=$((attempt + 1))
  sleep 2
done

# A record of what has been applied, so adding 0002 later does not mean re-running 0001. Created
# before anything else, and harmless if it already exists.
psql -v ON_ERROR_STOP=1 -q -c "
  create table if not exists schema_migration (
    filename    text        primary key,
    applied_at  timestamptz not null default now()
  );"

applied=0
for file in /migrations/*.sql; do
  [ -e "$file" ] || continue
  name=$(basename "$file")

  if [ "$(psql -tAc "select exists (select 1 from schema_migration where filename = '$name')")" = "t" ]; then
    echo "migrate: $name already applied"
    continue
  fi

  echo "migrate: applying $name"
  # The migration and the record of it land in one transaction, so a failure half way through
  # cannot leave the database claiming a migration it did not finish. psql runs -f and -c in the
  # order given, and --single-transaction wraps the lot - which is why the .sql files carry no
  # BEGIN/COMMIT of their own.
  psql -v ON_ERROR_STOP=1 --single-transaction \
    -f "$file" \
    -c "insert into schema_migration (filename) values ('$name');"

  applied=$((applied + 1))
done

echo "migrate: done, $applied migration(s) applied"
