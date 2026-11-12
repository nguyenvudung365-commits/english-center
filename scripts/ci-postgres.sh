#!/usr/bin/env bash
set -euo pipefail

# Database rieng cho test; khong nhan connection string hay secret trien khai.
cd "$(dirname "${BASH_SOURCE[0]}")/.."
for tool in docker dotnet openssl; do
  command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 1; }
done
docker info >/dev/null
run_id="$(openssl rand -hex 12)"
container="ec-pg-tests-$run_id"
results="TestResults/Postgres/$run_id"
export POSTGRES_PASSWORD="$(openssl rand -hex 32)"
cleanup() {
  status=$?
  trap - EXIT
  if ! docker rm --force --volumes "$container" >/dev/null 2>&1; then
    echo "Could not remove temporary test container: $container" >&2
    if ((status == 0)); then status=1; fi
  fi
  exit "$status"
}
trap cleanup EXIT
docker run -d --name "$container" --publish 127.0.0.1::5432 \
  --env POSTGRES_PASSWORD --env POSTGRES_USER=english_center \
  --env POSTGRES_DB=postgres postgres:17-alpine >/dev/null

ready=false
for ((attempt=1; attempt<=30; attempt++)); do
  if docker exec "$container" pg_isready -U english_center -d postgres >/dev/null 2>&1; then
    ready=true
    break
  fi
  if ((attempt < 30)); then sleep 2; fi
done
if [[ "$ready" != true ]]; then
  echo 'Temporary PostgreSQL did not become ready within 30 probes.' >&2
  exit 1
fi
address="$(docker port "$container" 5432/tcp)"
if [[ ! "$address" =~ ^127\.0\.0\.1:([0-9]+)$ ]]; then
  echo 'Unexpected PostgreSQL test port binding.' >&2
  exit 1
fi
export ENGLISHCENTER_TEST_POSTGRES="Host=127.0.0.1;Port=${BASH_REMATCH[1]};Database=postgres;Username=english_center;Password=$POSTGRES_PASSWORD"
dotnet test tests/EnglishCenter.IntegrationTests/EnglishCenter.IntegrationTests.csproj \
  -c Release --filter 'FullyQualifiedName~IdentityPostgresTests|FullyQualifiedName~MediaFilePostgresTests' \
  --logger 'trx;LogFileName=identity-postgres.trx' --results-directory "$results"

# dotnet test co the exit 0 khi filter khong khop hoac tat ca test bi skip.
report="$results/identity-postgres.trx"
if [[ ! -f "$report" ]]; then
  echo 'PostgreSQL verification did not produce a TRX report.' >&2
  exit 1
fi
counters="$(grep -o '<Counters [^>]*' "$report")"
counter() { sed -n "s/.* $1=\"\([0-9]*\)\".*/\1/p" <<<"$counters"; }
total="$(counter total)"
if [[ ! "$total" =~ ^[0-9]+$ ]] || ((total < 6)) || \
   [[ "$(counter executed)" != "$total" || "$(counter passed)" != "$total" || "$(counter notExecuted)" != 0 ]]; then
  echo 'PostgreSQL verification requires at least 6 executed, passing tests and no skipped tests.' >&2
  exit 1
fi
echo "PASS: $total Identity/Media tests ran on isolated PostgreSQL 17."
