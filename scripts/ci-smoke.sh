#!/usr/bin/env bash
set -euo pipefail

# Chi dung stack tam, khong truyen project hay secret cua may chu vao script nay.
project="ec-ci-$(date +%s)-${RANDOM}"
export IMAGE_TAG="$project"
export POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export JWT_SIGNING_KEY="$(openssl rand -hex 48)"
export MINIO_ROOT_USER="ci-storage"
export MINIO_ROOT_PASSWORD="$(openssl rand -hex 24)"
export MEDIA_STORAGE_ACCESS_KEY="$(openssl rand -hex 10)"
export MEDIA_STORAGE_SECRET_KEY="$(openssl rand -hex 24)"
export MEDIA_STORAGE_ENDPOINT=http://minio:9000
export MEDIA_STORAGE_PUBLIC_ENDPOINT=http://localhost:9000
export MEDIA_STORAGE_BUCKET=english-center-media
export MEDIA_STORAGE_REGION=us-east-1
export MEDIA_STORAGE_CORS_ORIGINS=http://localhost:8088
export BOOTSTRAP_ADMIN_EMAIL="ci-admin@example.test"
export BOOTSTRAP_ADMIN_PASSWORD="$(openssl rand -hex 24)"
export API_PORT=0 MINIO_PORT=0 MINIO_CONSOLE_PORT=0
# Explicit env-file prevents loading .env deployment secrets, even during cleanup.
compose=(docker compose --env-file /dev/null --project-name "$project" -f compose.minio.yaml)
cleanup() {
  # Volume duoc xoa chi thuoc project ec-ci vua tao, khong phai stack trien khai.
  "${compose[@]}" down --volumes --remove-orphans
  docker image rm "english-center-api:$IMAGE_TAG" >/dev/null || true
}
trap cleanup EXIT
"${compose[@]}" config --quiet
"${compose[@]}" build api minio-init
"${compose[@]}" up -d --no-build --wait --wait-timeout 180 postgres minio
# One-shot init exits successfully; do not ask up --wait to keep it running.
"${compose[@]}" up -d --no-build --no-deps minio-init
init_container="$("${compose[@]}" ps -a -q minio-init)"
test -n "$init_container"
init_status="$(docker wait "$init_container")"
test "$init_status" = 0
storage="$("${compose[@]}" port minio 9000)"
export MEDIA_STORAGE_PUBLIC_ENDPOINT="http://$storage"
"${compose[@]}" up -d --no-build --no-deps --wait --wait-timeout 180 api
address="$("${compose[@]}" port api 8080)"
curl --fail --silent --retry 12 --retry-connrefused --retry-delay 2 "http://$address/health" >/dev/null
curl --fail --silent --retry 12 --retry-connrefused --retry-delay 2 "http://$storage/minio/health/live" >/dev/null
login="$(jq -n --arg email "$BOOTSTRAP_ADMIN_EMAIL" --arg password "$BOOTSTRAP_ADMIN_PASSWORD" '{email:$email,password:$password}')"
tokens="$(curl --fail --silent --show-error -H 'Content-Type: application/json' -d "$login" "http://$address/api/identity/auth/login")"
access="$(jq -er '.accessToken' <<<"$tokens")"
refresh="$(jq -er '.refreshToken' <<<"$tokens")"
student="$(jq -n --arg password "$BOOTSTRAP_ADMIN_PASSWORD" '{email:"ci-student@example.test",fullName:"CI Student",password:$password,role:"Student"}')"
status="$(curl --silent --show-error -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $access" -H 'Content-Type: application/json' -d "$student" "http://$address/api/identity/users")"
test "$status" = 201
payload="$(jq -n --arg token "$refresh" '{refreshToken:$token}')"
status="$(curl --silent --show-error -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $access" -H 'Content-Type: application/json' -d "$payload" "http://$address/api/identity/auth/logout")"
test "$status" = 204
status="$(curl --silent --show-error -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d "$payload" "http://$address/api/identity/auth/refresh")"
test "$status" = 401
echo 'Dat: migration PostgreSQL, bootstrap admin, login, tao hoc vien, logout, thu hoi token va MinIO.'
