#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/project/scripts"
cp "$root/scripts/ci-smoke.sh" "$work/project/scripts/ci-smoke.sh"

# All external commands are local fakes: no Docker daemon, network or jq needed.
cat >"$work/bin/openssl" <<'MOCK'
#!/usr/bin/env bash
set -eu
[[ "$1 $2" == 'rand -hex' ]]
count=0
[[ ! -f "$STATE/random-count" ]] || read -r count <"$STATE/random-count"
count=$((count + 1))
echo "$count" >"$STATE/random-count"
printf '%0*x\n' "$(($3 * 2))" "$count" | tee -a "$STATE/secrets"
MOCK
cat >"$work/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -eu
if [[ "$SCENARIO" == prebuilt ]]; then
  [[ "$IMAGE_TAG" == "$PREBUILT_SHA" ]]
else
  [[ "$IMAGE_TAG" =~ ^ec-ci-[0-9]+-[0-9]+$ ]]
fi
if [[ "$1 $2" == 'image inspect' ]]; then
  [[ "$SCENARIO" == prebuilt && "$*" == "image inspect english-center-api:$PREBUILT_SHA" ]]
  echo 'image inspect' >>"$CALLS"
  exit 0
fi
if [[ "$1 $2" == 'image rm' ]]; then
  [[ "$#" == 3 && "$3" == "english-center-api:$IMAGE_TAG" ]]
  echo 'image rm' >>"$CALLS"
  exit 0
fi
if [[ "$1" == wait ]]; then
  [[ "$#" == 2 && "$2" == smoke-init-id && -f "$STATE/init-started" ]]
  echo 'wait init' >>"$CALLS"
  [[ "$SCENARIO" != init-wait-fails ]] || exit 44
  if [[ "$SCENARIO" == init-fails ]]; then echo 1; else touch "$STATE/init-done"; echo 0; fi
  exit 0
fi
[[ "$1 $2 $3 $4" == 'compose --env-file /dev/null --project-name' && "$5" =~ ^ec-ci-[0-9]+-[0-9]+$ && "$6 $7" == '-f compose.minio.yaml' ]]
shift 7
printf '%s\n' "$*" >>"$CALLS"
case "$*" in
  'config --quiet')
    [[ "$MEDIA_STORAGE_ACCESS_KEY" =~ ^[a-f0-9]{20}$ ]]
    [[ "$MEDIA_STORAGE_SECRET_KEY" =~ ^[a-f0-9]{48}$ ]]
    [[ "$MEDIA_STORAGE_ACCESS_KEY" != "$MINIO_ROOT_USER" && "$MEDIA_STORAGE_SECRET_KEY" != "$MINIO_ROOT_PASSWORD" ]]
    [[ "$API_PORT $MINIO_PORT $MINIO_CONSOLE_PORT" == '0 0 0' ]]
    [[ "$MEDIA_STORAGE_ENDPOINT" == 'http://minio:9000' ]]
    [[ "$MEDIA_STORAGE_PUBLIC_ENDPOINT" == 'http://localhost:9000' ]]
    [[ "$MEDIA_STORAGE_BUCKET $MEDIA_STORAGE_REGION" == 'english-center-media us-east-1' ]]
    [[ "$MEDIA_STORAGE_CORS_ORIGINS" == 'http://localhost:8088' ]]
    [[ "$SCENARIO" != config-fails ]] || exit 41 ;;
  'build api minio-init')
    [[ "$SCENARIO" != prebuilt ]]
    [[ "$SCENARIO" != build-fails ]] || exit 42
    touch "$STATE/built" ;;
  'build minio-init')
    [[ "$SCENARIO" == prebuilt ]]
    touch "$STATE/built" ;;
  'up -d --no-build --wait --wait-timeout 180 postgres minio')
    [[ -f "$STATE/built" ]]
    [[ "$SCENARIO" != infra-fails ]] || exit 43
    touch "$STATE/infra-ready" ;;
  'up -d --no-build --no-deps minio-init')
    [[ -f "$STATE/infra-ready" ]]
    touch "$STATE/init-started" ;;
  'ps -a -q minio-init')
    [[ -f "$STATE/init-started" ]]
    echo smoke-init-id ;;
  'port minio 9000')
    [[ -f "$STATE/init-done" && ! -f "$STATE/api-ready" ]]
    echo '127.0.0.1:19000' ;;
  'up -d --no-build --no-deps --pull never --wait --wait-timeout 180 api')
    [[ -f "$STATE/init-done" && "$MEDIA_STORAGE_PUBLIC_ENDPOINT" == 'http://127.0.0.1:19000' ]]
    [[ "$SCENARIO" != api-fails ]] || exit 45
    touch "$STATE/api-ready" ;;
  'port api 8080')
    [[ -f "$STATE/api-ready" ]]
    echo '127.0.0.1:15080' ;;
  'down --volumes --remove-orphans') ;;
  *) echo 'Unexpected Docker invocation.' >&2; exit 99 ;;
esac
MOCK
cat >"$work/bin/jq" <<'MOCK'
#!/usr/bin/env bash
set -eu
case "$*" in
  '-er .accessToken') [[ "$(cat)" == tokens ]]; echo smoke-access ;;
  '-er .refreshToken') [[ "$(cat)" == tokens ]]; echo smoke-refresh ;;
  *)
    [[ "$1" == -n ]]
    case "${!#}" in
      '{email:$email,password:$password}')
        [[ "$2 $3 $4 $5 $6 $7" == "--arg email $BOOTSTRAP_ADMIN_EMAIL --arg password $BOOTSTRAP_ADMIN_PASSWORD" ]]
        echo login-payload ;;
      '{email:"ci-student@example.test",fullName:"CI Student",password:$password,role:"Student"}')
        [[ "$2 $3 $4" == "--arg password $BOOTSTRAP_ADMIN_PASSWORD" ]]
        echo student-payload ;;
      '{refreshToken:$token}') [[ "$2 $3 $4" == '--arg token smoke-refresh' ]]; echo refresh-payload ;;
      *) exit 99 ;;
    esac ;;
esac
MOCK
cat >"$work/bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -eu
[[ -f "$STATE/api-ready" ]]
url="${!#}"
printf '%s\n' "$url" >>"$CALLS"
case "$url" in
  http://127.0.0.1:15080/health|http://127.0.0.1:19000/minio/health/live)
    [[ "$*" == *'--fail --silent --retry 12 --retry-connrefused --retry-delay 2'* ]]
    [[ "$SCENARIO" != health-fails ]] || exit 22 ;;
  http://127.0.0.1:15080/api/identity/auth/login)
    [[ "$*" == *'-d login-payload'* ]]
    [[ "$SCENARIO" != login-fails ]] || exit 22
    echo tokens ;;
  http://127.0.0.1:15080/api/identity/users)
    [[ "$*" == *'Authorization: Bearer smoke-access'* && "$*" == *'-d student-payload'* ]]
    if [[ "$SCENARIO" == student-fails ]]; then echo 403; else echo 201; fi ;;
  http://127.0.0.1:15080/api/identity/auth/logout)
    [[ "$*" == *'Authorization: Bearer smoke-access'* && "$*" == *'-d refresh-payload'* ]]
    if [[ "$SCENARIO" == logout-fails ]]; then echo 400; else echo 204; fi ;;
  http://127.0.0.1:15080/api/identity/auth/refresh)
    [[ "$*" == *'-d refresh-payload'* ]]
    if [[ "$SCENARIO" == refresh-fails ]]; then echo 200; else echo 401; fi ;;
  *) exit 99 ;;
esac
MOCK
chmod +x "$work/bin/"*
export PATH="$work/bin:$PATH"
# Deliberately seed deployment values; the runner must replace them, not reuse them.
export MEDIA_STORAGE_ACCESS_KEY=deployment-access MEDIA_STORAGE_SECRET_KEY=deployment-secret
export MEDIA_STORAGE_PUBLIC_ENDPOINT=https://deployment.invalid
export MEDIA_STORAGE_ENDPOINT=https://deployment.invalid
export MEDIA_STORAGE_BUCKET=deployment-bucket MEDIA_STORAGE_REGION=auto
export MEDIA_STORAGE_CORS_ORIGINS=https://deployment.invalid
export PREBUILT_SHA=0123456789012345678901234567890123456789
for SCENARIO in success prebuilt config-fails build-fails infra-fails init-fails init-wait-fails api-fails health-fails login-fails student-fails logout-fails refresh-fails; do
  export SCENARIO
  export STATE="$work/$SCENARIO" CALLS="$work/$SCENARIO/calls"
  mkdir -p "$STATE"
  : >"$CALLS"
  status=0
  args=()
  [[ "$SCENARIO" != prebuilt ]] || args+=("$PREBUILT_SHA")
  (cd "$work/project" && bash scripts/ci-smoke.sh "${args[@]}") >"$STATE/output" 2>&1 || status=$?
  case "$SCENARIO" in
    success|prebuilt) expected=0 ;;
    config-fails) expected=41 ;;
    build-fails) expected=42 ;;
    infra-fails) expected=43 ;;
    init-wait-fails) expected=44 ;;
    api-fails) expected=45 ;;
    health-fails|login-fails) expected=22 ;;
    *) expected=1 ;;
  esac
  if [[ "$status" != "$expected" ]]; then
    echo "FAIL: $SCENARIO (expected exit $expected, got $status)" >&2
    exit 1
  fi
  if [[ "$SCENARIO" == prebuilt ]]; then
    grep -Fxq 'image inspect' "$CALLS"
    [[ "$(tail -n 1 "$CALLS")" == 'down --volumes --remove-orphans' ]]
  else
    [[ "$(tail -n 2 "$CALLS")" == $'down --volumes --remove-orphans\nimage rm' ]]
  fi
  if grep -Fqf "$STATE/secrets" "$STATE/output" ||
     grep -Eq 'deployment-access|deployment-secret|smoke-access|smoke-refresh' "$STATE/output"; then
    echo "FAIL: $SCENARIO leaked a test secret" >&2
    exit 1
  fi
  if [[ "$SCENARIO" == success || "$SCENARIO" == prebuilt ]]; then
    test "$(grep -c '^http://' "$CALLS")" = 6
    grep -q 'Dat: migration PostgreSQL' "$STATE/output"
  elif grep -q 'Dat: migration PostgreSQL' "$STATE/output"; then
    echo "FAIL: $SCENARIO logged success" >&2
    exit 1
  fi
  echo "PASS: $SCENARIO"
done
