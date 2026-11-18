#!/usr/bin/env bash
set -euo pipefail

[[ ${IMAGE_TAG:-} =~ ^[a-f0-9]{40}$ ]] || { echo 'Thiếu IMAGE_TAG là SHA đầy đủ.' >&2; exit 1; }
[[ -n ${DEPLOY_ENV:-} && -r $DEPLOY_ENV ]] || { echo 'Không đọc được credential file deploy.' >&2; exit 1; }

compose=(docker compose --env-file "$DEPLOY_ENV" --project-name english-center)
if [[ ${ENGLISH_CENTER_VPS_COMPOSE_FILES:-} == 'compose.yaml:compose.web.yaml' ]]; then
  compose+=(-f compose.yaml -f compose.web.yaml)
elif [[ -n ${ENGLISH_CENTER_VPS_COMPOSE_FILES:-} ]]; then
  IFS=: read -r -a files <<<"$ENGLISH_CENTER_VPS_COMPOSE_FILES"
  for file in "${files[@]}"; do
    [[ $file == /* && -r $file ]] || {
      echo 'ENGLISH_CENTER_VPS_COMPOSE_FILES cần đường dẫn tuyệt đối đọc được.' >&2; exit 1;
    }
    compose+=(-f "$file")
  done
else
  echo 'Cần cấu hình ENGLISH_CENTER_VPS_COMPOSE_FILES theo stack VPS hiện tại.' >&2; exit 1
fi

run_compose() {
  # Compose có thể ghi nội dung dòng lỗi của secret file lên stderr.
  COMPOSE_REMOVE_ORPHANS=false "${compose[@]}" "$@" 2>/dev/null
}

if ! run_compose config --format json | jq -e --arg tag "$IMAGE_TAG" '
  def health_enabled: .healthcheck.disable != true and .healthcheck.test[0] != "NONE";
  .services.api.image == ("english-center-api:" + $tag) and
  .services.web.image == ("english-center-web:" + $tag) and
  (.services.api | health_enabled) and (.services.web | health_enabled) and
  .services.postgres != null
' >/dev/null 2>&1; then
  echo 'Compose không hợp lệ: kiểm tra secret file, image theo SHA và health check API/web.' >&2; exit 1
fi
if ! docker image inspect --format '{{json .Config.Healthcheck}}' "english-center-api:$IMAGE_TAG" "english-center-web:$IMAGE_TAG" 2>/dev/null |
  jq -se 'length == 2 and all(.[]; .Test[0] == "CMD" or .Test[0] == "CMD-SHELL")' >/dev/null 2>&1; then
  echo 'Thiếu image theo SHA hoặc image API/web không có health check.' >&2; exit 1
fi

# Không khởi tạo DB mới hoặc recreate PostgreSQL trong một lần cập nhật ứng dụng.
postgres="$(run_compose ps --status running --quiet postgres)" || {
  echo 'Không đọc được trạng thái PostgreSQL.' >&2; exit 1
}
if [[ -z $postgres || $(docker inspect --format '{{.State.Health.Status}} {{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}' "$postgres") != 'healthy english-center_postgres-data' ]]; then
  echo 'Không tìm thấy PostgreSQL healthy với volume english-center_postgres-data. Dừng deploy.' >&2
  exit 1
fi

if ! run_compose up -d --no-deps --no-build --pull never --wait --wait-timeout 180 api >/dev/null; then
  # Reload proxy hiện tại nếu API đã đổi IP trước khi health check thất bại.
  if ! run_compose exec -T web nginx -s reload >/dev/null; then
    echo 'Không reload được Nginx; kiểm tra proxy trên VPS.' >&2
  fi
  echo 'Deploy API thất bại; kiểm tra Docker trên VPS.' >&2; exit 1
fi
# Nginx cần resolve lại địa chỉ API sau khi container API được thay.
if ! run_compose up -d --no-deps --no-build --pull never --force-recreate --wait --wait-timeout 90 web >/dev/null; then
  echo 'Deploy web thất bại; kiểm tra Docker trên VPS.' >&2; exit 1
fi
if ! run_compose exec -T web wget -q -O - -T 10 http://127.0.0.1:8080/api/identity/ping |
  jq -e '.module == "Identity"' >/dev/null 2>&1; then
  echo 'Kiểm tra proxy web → API không đạt.' >&2; exit 1;
fi
echo "Deploy $IMAGE_TAG: API và web healthy, proxy /api hoạt động."
