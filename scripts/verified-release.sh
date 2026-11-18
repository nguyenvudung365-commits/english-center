#!/usr/bin/env bash
set -euo pipefail

repo=nguyenvudung365-commits/english-center
[[ ${IMAGE_TAG:-} =~ ^[a-f0-9]{40}$ ]] || { echo 'Thiếu IMAGE_TAG là SHA đầy đủ.' >&2; exit 1; }

check_main() {
  local tip
  if ! tip="$(GIT_TERMINAL_PROMPT=0 timeout 20s git ls-remote --exit-code "https://github.com/$repo.git" refs/heads/main)"; then
    echo 'Không xác minh được nhánh main trên GitHub.' >&2; return 1
  fi
  if [[ $tip != "$IMAGE_TAG"$'\trefs/heads/main' ]]; then
    echo 'Commit không còn là đầu nhánh main; bỏ qua deploy.' >&2; return 1
  fi
}

if [[ ${1:-} == --check-main ]]; then
  check_main
  exit
fi
[[ -n ${GH_TOKEN:-} ]] || { echo 'Thiếu credential Actions: read.' >&2; exit 1; }
export GH_PROMPT_DISABLED=1

run=''
for ((attempt=1; attempt<=60; attempt++)); do
  check_main
  # A branch scan schedules once per commit; wait here if publication is still in progress.
  if ! run="$(timeout 15s gh api \
    "repos/$repo/actions/workflows/ci.yml/runs?branch=main&event=push&head_sha=$IMAGE_TAG&status=success&per_page=1" |
    jq -r --arg sha "$IMAGE_TAG" --arg repo "$repo" '
      .workflow_runs | if . == [] then "" else .[0] |
        if .head_sha == $sha and .head_branch == "main" and .event == "push" and
          .head_repository.full_name == $repo and .status == "completed" and .conclusion == "success"
        then "\(.id)-\(.run_attempt)" else error("Unexpected CI run") end end
    ')"; then
    echo 'Không truy vấn được kết quả CI trên GitHub.' >&2; exit 1
  fi
  if [[ -n $run ]]; then break; fi
  if (( attempt == 1 )); then echo "Đang chờ CI main và GHCR cho $IMAGE_TAG…" >&2; fi
  if (( attempt < 60 )); then sleep 60; fi
done
[[ $run =~ ^[1-9][0-9]*-[1-9][0-9]*$ ]] || { echo 'Không tìm thấy lượt CI main đã xuất bản thành công.' >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
if ! timeout 60s gh run download "${run%-*}" --repo "$repo" \
    --name "release-digests-${run#*-}" --dir "$work" >&2; then
  echo 'Không tải được manifest phát hành; kiểm tra CI và quyền Actions: read.' >&2; exit 1
fi
unset GH_TOKEN
if ! digests="$(jq -ers --arg sha "$IMAGE_TAG" '
  def digest: type == "string" and test("^sha256:[a-f0-9]{64}$");
  select(length == 1) | .[0] | select(.sha == $sha and (.api | digest) and (.web | digest)) |
  [.api, .web] | join(" ")
' "$work/release.json")"; then
  echo 'Manifest phát hành thiếu digest hợp lệ cho đúng commit.' >&2; exit 1
fi
read -r api web <<<"$digests"
api="ghcr.io/$repo-api@$api"
web="ghcr.io/$repo-web@$web"

check_main
echo "Đang tải API/web cho $IMAGE_TAG (CI $run)." >&2
if ! docker pull "$api" >&2 || ! docker pull "$web" >&2; then
  echo 'Không tải được cặp image GHCR; kiểm tra CI và quyền đọc package.' >&2; exit 1
fi
printf '%s\n' "$api" "$web"
