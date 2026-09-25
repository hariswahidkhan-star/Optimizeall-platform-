#!/usr/bin/env bash
# Upgrades a MySQL database created by an earlier release, whose InitialCreate baseline had a different migration id
# (docs/DATABASE.md § Baseline upgrade), to this checkout's schema without losing data. The API refuses to start on
# such a database (BaselineUpgradeException); SQLite databases are upgraded automatically at startup instead.
#
# Usage (from a checkout of the release you are deploying; stop the API first):
#   DB_HOST=… DB_PORT=3306 DB_NAME=optimizeall DB_USER=root DB_PASSWORD=… scripts/upgrade-baseline-mysql.sh [options]
#
#   (no options)          dry run: builds the current schema in `<DB_NAME>__upgrade`, copies and verifies the data,
#                         reports, and leaves the live database untouched (the staging database stays for inspection)
#   --apply               then swaps: moves every live table into `<DB_NAME>__bak_<utc>` and the upgraded tables into
#                         `<DB_NAME>` with one atomic RENAME TABLE (the old tables stay in the backup database)
#   --schema FILE         the current schema as SQL (`dotnet ef migrations script`); generated with dotnet-ef when omitted
#   --accept-type-defaults
#                         new NOT NULL columns without a database default (and columns that became NOT NULL) are
#                         filled with a type default (0, '', '0001-01-01 00:00:00', '[]', the empty GUID); without this
#                         flag the script lists them and stops so you can check each value makes sense for the column
#
# The user needs CREATE, DROP, ALTER, INSERT, SELECT on `<DB_NAME>`, `<DB_NAME>__upgrade` and `<DB_NAME>__bak_*`
# (e.g. root; on Render: MYSQL_ROOT_PASSWORD of optimizeall-mysql). Take a mysqldump first as well.
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

apply=0; schema=""; accept_defaults=0
while [ $# -gt 0 ]; do
  case "$1" in
    --apply) apply=1 ;;
    --schema) schema="${2:?--schema needs a file}"; shift ;;
    --accept-type-defaults) accept_defaults=1 ;;
    -h|--help) sed -n '2,23p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) die "Unknown option: $1 (see --help)" ;;
  esac
  shift
done

have mysql || die "the mysql client is required"
case "$DB_NAME" in *[!A-Za-z0-9_]*) die "DB_NAME may only contain letters, digits and underscores: $DB_NAME" ;; esac
live="$DB_NAME"; stage="${DB_NAME}__upgrade"; backup="${DB_NAME}__bak_$(date -u +%Y%m%dT%H%M%SZ)"

# sql [DATABASE] QUERY — runs a query and prints tab-separated rows.
sql() {
  if [ $# -eq 2 ]; then mysql_app -D "$1" -e "$2"; else mysql_app -e "$1"; fi
}

current=$(basename "$(compgen -G "$BACKEND_DIR/src/OptimizeAll.Infrastructure/Persistence/Migrations/*_InitialCreate.cs" | head -1)" .cs)
[ -n "$current" ] || die "No MySQL InitialCreate migration in this checkout"
applied=$(sql "$live" "SELECT MigrationId FROM \`__EFMigrationsHistory\` ORDER BY MigrationId") \
  || die "Cannot read $live.__EFMigrationsHistory (wrong database, or no access)"
foreign=$(printf '%s\n' "$applied" | grep '_InitialCreate$' | grep -vx "$current" | tail -1 || true)
if [ -z "$foreign" ]; then ok "$live has this release's baseline ($current): nothing to upgrade"; exit 0; fi
log "$live has baseline $foreign; this release's baseline is $current"

if [ -z "$schema" ]; then
  if have dotnet-ef; then ef=(dotnet-ef); elif [ -x "$HOME/.dotnet/tools/dotnet-ef" ]; then ef=("$HOME/.dotnet/tools/dotnet-ef")
  else die "dotnet-ef not found: install it (dotnet tool install --global dotnet-ef --version 8.0.10) or pass --schema FILE"; fi
  schema="$(mktemp)"; trap 'rm -f "$schema"' EXIT
  log "Generating the current MySQL schema (dotnet ef migrations script)"
  (cd "$BACKEND_DIR" && ASPNETCORE_ENVIRONMENT=Development Database__Provider=MySql \
    ConnectionStrings__Default="Server=127.0.0.1;Port=3306;Database=design;User=design;Password=design;" \
    "${ef[@]}" migrations script -p src/OptimizeAll.Infrastructure -s src/OptimizeAll.Api -o "$schema" >/dev/null)
fi
[ -s "$schema" ] || die "Schema file $schema is empty"

charset=$(sql "SELECT DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = '$live'")
read -r cs coll <<< "$charset"
log "Creating $stage with the current schema ($cs / $coll)"
sql "DROP DATABASE IF EXISTS \`$stage\`; CREATE DATABASE \`$stage\` CHARACTER SET $cs COLLATE $coll"
mysql_app -D "$stage" < "$schema"

# Column metadata of both databases: table, column, nullable, has default/auto value, data type, column type.
columns() {
  sql "SELECT TABLE_NAME, COLUMN_NAME, IS_NULLABLE, IF(COLUMN_DEFAULT IS NULL AND EXTRA NOT LIKE '%auto_increment%', 'none', 'yes'),
              DATA_TYPE, COLUMN_TYPE
       FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = '$1' AND EXTRA NOT LIKE '%GENERATED%'
       ORDER BY TABLE_NAME, ORDINAL_POSITION"
}
old_columns=$(columns "$live"); new_columns=$(columns "$stage")
old_tables=$(sql "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = '$live' AND TABLE_TYPE = 'BASE TABLE' ORDER BY 1")
new_tables=$(sql "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = '$stage' AND TABLE_TYPE = 'BASE TABLE' AND TABLE_NAME <> '__EFMigrationsHistory' ORDER BY 1")

type_default() { # DATA_TYPE COLUMN_TYPE
  case "$1" in
    tinyint|smallint|mediumint|int|bigint|decimal|float|double|bit) echo "0" ;;
    datetime|timestamp) echo "'0001-01-01 00:00:00'" ;;
    date) echo "'0001-01-01'" ;;
    time) echo "'00:00:00'" ;;
    json) echo "JSON_ARRAY()" ;;
    char) [ "$2" = "char(36)" ] && echo "'00000000-0000-0000-0000-000000000000'" || echo "''" ;;
    binary|varbinary|blob|tinyblob|mediumblob|longblob) echo "X''" ;;
    *) echo "''" ;;
  esac
}

statements=(); defaulted=(); copied=0; created=()
for table in $new_tables; do
  if ! grep -qx "$table" <<< "$old_tables"; then created+=("$table"); continue; fi
  insert=(); select=()
  while IFS=$'\t' read -r t col nullable has_default data_type column_type; do
    [ "$t" = "$table" ] || continue
    old=$(awk -F'\t' -v t="$table" -v c="$col" '$1 == t && tolower($2) == tolower(c) { print $3; exit }' <<< "$old_columns")
    if [ -n "$old" ]; then
      insert+=("\`$col\`")
      if [ "$nullable" = NO ] && [ "$old" = YES ]; then
        select+=("COALESCE(\`$col\`, $(type_default "$data_type" "$column_type"))"); defaulted+=("$table.$col (was nullable)")
      else
        select+=("\`$col\`")
      fi
    elif [ "$nullable" = NO ] && [ "$has_default" = none ]; then
      insert+=("\`$col\`"); select+=("$(type_default "$data_type" "$column_type")"); defaulted+=("$table.$col (new, $column_type)")
    fi
  done <<< "$new_columns"
  statements+=("INSERT INTO \`$stage\`.\`$table\` ($(IFS=,; echo "${insert[*]}")) SELECT $(IFS=,; echo "${select[*]}") FROM \`$live\`.\`$table\`;")
  copied=$((copied + 1))
done
dropped=$(comm -23 <(sort <<< "$old_tables" | grep -vx '__EFMigrationsHistory') <(sort <<< "$new_tables") | tr '\n' ' ')

if [ ${#defaulted[@]} -gt 0 ]; then
  warn "Columns that get a type default:"; printf '    %s\n' "${defaulted[@]}" >&2
  [ "$accept_defaults" = 1 ] || die "Check these values, then re-run with --accept-type-defaults (the live database is untouched)"
fi

log "Copying $copied tables into $stage (foreign key checks off during the copy)"
{ echo "SET SESSION FOREIGN_KEY_CHECKS = 0;"; printf '%s\n' "${statements[@]}"; echo "SET SESSION FOREIGN_KEY_CHECKS = 1;"; } | mysql_app

log "Verifying row counts"
for table in $new_tables; do
  grep -qx "$table" <<< "$old_tables" || continue
  a=$(sql "SELECT COUNT(*) FROM \`$live\`.\`$table\`"); b=$(sql "SELECT COUNT(*) FROM \`$stage\`.\`$table\`")
  [ "$a" = "$b" ] || die "$table: copied $b of $a rows ($stage kept for inspection; the live database is untouched)"
done

log "Verifying foreign keys"
fks=$(sql "SELECT k.CONSTRAINT_NAME, k.TABLE_NAME, k.REFERENCED_TABLE_NAME,
                  GROUP_CONCAT(CONCAT('c.\`', k.COLUMN_NAME, '\` = p.\`', k.REFERENCED_COLUMN_NAME, '\`') ORDER BY k.ORDINAL_POSITION SEPARATOR ' AND '),
                  GROUP_CONCAT(CONCAT('c.\`', k.COLUMN_NAME, '\` IS NOT NULL') ORDER BY k.ORDINAL_POSITION SEPARATOR ' AND '),
                  MIN(CONCAT('p.\`', k.REFERENCED_COLUMN_NAME, '\`'))
           FROM information_schema.KEY_COLUMN_USAGE k
           WHERE k.TABLE_SCHEMA = '$stage' AND k.REFERENCED_TABLE_NAME IS NOT NULL
           GROUP BY k.CONSTRAINT_NAME, k.TABLE_NAME, k.REFERENCED_TABLE_NAME")
violations=0
while IFS=$'\t' read -r name child parent join notnull parent_col; do
  [ -n "$name" ] || continue
  n=$(sql "$stage" "SELECT COUNT(*) FROM \`$child\` c LEFT JOIN \`$parent\` p ON $join WHERE $notnull AND $parent_col IS NULL")
  if [ "$n" != 0 ]; then warn "$name: $n row(s) of $child reference missing $parent rows"; violations=$((violations + n)); fi
done <<< "$fks"
[ "$violations" = 0 ] || die "Foreign key violations after the copy ($stage kept for inspection; the live database is untouched)"

ok "Upgrade staged in $stage: $copied tables copied, ${#created[@]} new (${created[*]:-none}), dropped: ${dropped:-none}"
if [ "$apply" != 1 ]; then
  log "Dry run: nothing changed in $live. Re-run with --apply to swap (stop the API first)."
  exit 0
fi

log "Swapping: $live tables → $backup, $stage tables → $live (one atomic RENAME TABLE)"
sql "CREATE DATABASE \`$backup\` CHARACTER SET $cs COLLATE $coll"
renames=()
for table in $old_tables; do renames+=("\`$live\`.\`$table\` TO \`$backup\`.\`$table\`"); done
for table in $(sql "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = '$stage' ORDER BY 1"); do
  renames+=("\`$stage\`.\`$table\` TO \`$live\`.\`$table\`")
done
sql "RENAME TABLE $(IFS=,; echo "${renames[*]}")"
sql "DROP DATABASE \`$stage\`"
ok "$live now has baseline $current; the previous tables are in $backup (drop it once the new release is verified)"
