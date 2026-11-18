#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export PATH="$work:$PATH" DEPLOY_ENV="$work/deploy.env" CALLS="$work/calls"
export IMAGE_TAG=0123456789012345678901234567890123456789
touch "$DEPLOY_ENV" "$work/compose.vps.yaml" "$work/compose.release.yaml"
export ENGLISH_CENTER_VPS_COMPOSE_FILES=compose.yaml:compose.web.yaml

cat >"$work/docker" <<'MOCK'
#!/usr/bin/env bash
set -eu
if [[ $1 == inspect ]]; then
  [[ ${!#} == postgres-id ]]
  case $SCENARIO in
    wrong-volume) echo 'healthy other-data' ;;
    unhealthy-db) echo 'unhealthy english-center_postgres-data' ;;
    *) echo 'healthy english-center_postgres-data' ;;
  esac
  exit 0
fi
if [[ "$1 $2" == 'image inspect' ]]; then
  [[ $3 == --format && $5 == "english-center-api:$IMAGE_TAG" && $6 == "english-center-web:$IMAGE_TAG" ]]
  [[ $SCENARIO != missing-image ]]
  if [[ $SCENARIO == missing-health ]]; then echo null; else echo '{"Test":["CMD-SHELL","probe"]}'; fi
  echo '{"Test":["CMD-SHELL","probe"]}'
  exit 0
fi
[[ $COMPOSE_REMOVE_ORPHANS == false ]]
[[ "${1} ${2} ${4} ${5}" == 'compose --env-file --project-name english-center' && $3 == "$DEPLOY_ENV" ]]
shift 5
if [[ $SCENARIO == existing-vps ]]; then
  [[ $1 == -f && $2 == "${ENGLISH_CENTER_VPS_COMPOSE_FILES%:*}" && $3 == -f && $4 == "${ENGLISH_CENTER_VPS_COMPOSE_FILES#*:}" ]]
else
  [[ "$1 $2 $3 $4" == '-f compose.yaml -f compose.web.yaml' ]]
fi
shift 4
echo "$*" >>"$CALLS"
case "$*" in
  'config --format json')
    tag=$IMAGE_TAG
    if [[ $SCENARIO == wrong-image ]]; then tag=local; fi
    if [[ $SCENARIO == invalid-config ]]; then echo 'test-secret-value' >&2; exit 42; fi
    printf '{"services":{"api":{"image":"english-center-api:%s"},"web":{"image":"english-center-web:%s"},"postgres":{}}}\n' "$tag" "$IMAGE_TAG" ;;
  'ps --status running --quiet postgres')
    [[ $SCENARIO == missing-db ]] || echo postgres-id ;;
  'up -d --no-deps --no-build --pull never --wait --wait-timeout 180 api')
    [[ $SCENARIO != api-fails ]] ;;
  'up -d --no-deps --no-build --pull never --force-recreate --wait --wait-timeout 90 web')
    [[ $SCENARIO != web-fails ]] ;;
  'exec -T web nginx -s reload') [[ $SCENARIO == api-fails ]] ;;
  'exec -T web wget -q -O - -T 10 http://127.0.0.1:8080/api/identity/ping')
    if [[ $SCENARIO == proxy-fails ]]; then exit 1; fi
    echo '{"module":"Identity"}' ;;
  *) exit 99 ;;
esac
MOCK
chmod +x "$work/docker"

run_case() {
  : >"$CALLS"
  local status=0
  SCENARIO="$1" bash scripts/deploy.sh >"$work/output" 2>&1 || status=$?
  if [[ $2 == pass && $status -eq 0 ]] || [[ $2 == fail && $status -ne 0 ]]; then
    return
  fi
  cat "$work/output" >&2
  echo "Unexpected deploy result: $1 ($status)" >&2
  exit 1
}

run_case ok pass
cat >"$work/expected" <<'EXPECTED'
config --format json
ps --status running --quiet postgres
up -d --no-deps --no-build --pull never --wait --wait-timeout 180 api
up -d --no-deps --no-build --pull never --force-recreate --wait --wait-timeout 90 web
exec -T web wget -q -O - -T 10 http://127.0.0.1:8080/api/identity/ping
EXPECTED
diff -u "$work/expected" "$CALLS"
ENGLISH_CENTER_VPS_COMPOSE_FILES="$work/compose.vps.yaml:$work/compose.release.yaml" run_case existing-vps pass
ENGLISH_CENTER_VPS_COMPOSE_FILES= run_case missing-compose fail
[[ ! -s $CALLS ]]
for scenario in wrong-image missing-image missing-health missing-db wrong-volume unhealthy-db invalid-config; do
  run_case "$scenario" fail
  if grep -q '^up ' "$CALLS"; then echo "Unexpected rollout: $scenario" >&2; exit 1; fi
done
if grep -q 'test-secret-value' "$work/output"; then echo 'Compose leaked a secret.' >&2; exit 1; fi
grep -q 'Compose không hợp lệ' "$work/output"
run_case api-fails fail
{ head -n 3 "$work/expected"; echo 'exec -T web nginx -s reload'; } | diff -u - "$CALLS"
run_case web-fails fail
head -n 4 "$work/expected" | diff -u - "$CALLS"
run_case proxy-fails fail
diff -u "$work/expected" "$CALLS"
echo 'PASS: deploy chạy đúng commit, giữ PostgreSQL và báo lỗi API/proxy.'
