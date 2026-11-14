#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
runner="$root/scripts/ci-postgres.sh"
if [[ ! -f "$runner" ]]; then
  echo 'FAIL: PostgreSQL CI runner is missing.' >&2
  exit 1
fi
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/project/scripts"
cp "$runner" "$work/project/scripts/ci-postgres.sh"
runner="$work/project/scripts/ci-postgres.sh"
cat >"$work/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$*" >>"$CALLS"
case "$1" in
  info) exit 0 ;;
  run) test -n "$POSTGRES_PASSWORD"; exit 0 ;;
  exec) test "$SCENARIO" != not-ready ;;
  port)
    if [[ "$SCENARIO" == invalid-port ]]; then echo '0.0.0.0:15432'; else echo '127.0.0.1:15432'; fi ;;
  rm) test "$SCENARIO" != cleanup-fails ;;
  *) exit 99 ;;
esac
MOCK
cat >"$work/bin/dotnet" <<'MOCK'
#!/usr/bin/env bash
set -eu
[[ "$ENGLISHCENTER_TEST_POSTGRES" == *'Port=15432;'* ]]
[[ "$ENGLISHCENTER_TEST_POSTGRES" == *"Password=$POSTGRES_PASSWORD"* ]]
printf 'dotnet\n' >>"$CALLS"
[[ "$SCENARIO" != test-fails ]] || exit 7
results=''
filter=''
while (($#)); do
  if [[ "$1" == --results-directory ]]; then results="$2"; shift; fi
  if [[ "$1" == --filter ]]; then filter="$2"; shift; fi
  shift
done
[[ "$filter" == 'FullyQualifiedName~IdentityPostgresTests|FullyQualifiedName~MediaFilePostgresTests' ]]
mkdir -p "$results"
[[ "$SCENARIO" != missing-report ]] || exit 0
case "$SCENARIO" in
  skipped) counters='total="6" executed="0" passed="0" notExecuted="6"' ;;
  zero) counters='total="0" executed="0" passed="0" notExecuted="0"' ;;
  insufficient) counters='total="5" executed="5" passed="5" notExecuted="0"' ;;
  malformed) counters='total="6"' ;;
  *) counters='total="6" executed="6" passed="6" notExecuted="0"' ;;
esac
printf '<TestRun><ResultSummary><Counters %s /></ResultSummary></TestRun>\n' "$counters" >"$results/identity-postgres.trx"
MOCK
cat >"$work/bin/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$work/bin/"*
export PATH="$work/bin:$PATH"
export CALLS="$work/calls"
for SCENARIO in success test-fails not-ready skipped zero cleanup-fails invalid-port missing-report insufficient malformed; do
  export SCENARIO
  : >"$CALLS"
  status=0
  bash "$runner" >"$work/output" 2>&1 || status=$?
  case "$SCENARIO" in
    success)
      test "$status" = 0
      grep -q 'PASS: 6 Identity/Media tests ran on isolated PostgreSQL 17.' "$work/output" ;;
    test-fails) test "$status" = 7 ;;
    *) test "$status" != 0 ;;
  esac
  grep -Eq '^rm --force --volumes ec-pg-tests-[a-f0-9]+$' "$CALLS"
  grep -q -- '--publish 127.0.0.1::5432' "$CALLS"
  grep -q -- '--env POSTGRES_PASSWORD ' "$CALLS"
  if [[ "$SCENARIO" == not-ready ]]; then
    test "$(grep -c '^exec ' "$CALLS")" = 30
    ! grep -q '^dotnet$' "$CALLS"
  fi
  ! grep -q 'Password=' "$work/output"
  echo "PASS: $SCENARIO"
done
