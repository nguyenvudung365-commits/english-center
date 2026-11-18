#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export PATH="$work:$PATH" CALLS="$work/calls"
export IMAGE_TAG=0123456789012345678901234567890123456789
export PACKAGE_ROOT=ghcr.io/nguyenvudung365-commits/english-center
export API_DIGEST=sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
export WEB_DIGEST=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
export GH_TOKEN=mock-actions-token

cat >"$work/git" <<'MOCK'
#!/usr/bin/env bash
set -eu
[[ $* == 'ls-remote --exit-code https://github.com/nguyenvudung365-commits/english-center.git refs/heads/main' ]]
case $SCENARIO in
  stale) echo -e 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\trefs/heads/main' ;;
  git-error) exit 128 ;;
  *) printf '%s\trefs/heads/main\n' "$IMAGE_TAG" ;;
esac
MOCK
cat >"$work/gh" <<'MOCK'
#!/usr/bin/env bash
set -eu
[[ $GH_TOKEN == mock-actions-token && $GH_PROMPT_DISABLED == 1 ]]
if [[ $1 == run ]]; then
  [[ ${*:1:8} == 'run download 981 --repo nguyenvudung365-commits/english-center --name release-digests-2 --dir' ]]
  echo manifest >>"$CALLS"
  [[ $SCENARIO != missing-manifest ]]
  sha=$IMAGE_TAG web=$WEB_DIGEST
  [[ $SCENARIO != bad-digest ]] || web=invalid
  [[ $SCENARIO != manifest-wrong-sha ]] || sha=other
  printf '{"sha":"%s","api":"%s","web":"%s"}\n' "$sha" "$API_DIGEST" "$web" >"$9/release.json"
  exit
fi
[[ $1 == api && $2 == "repos/nguyenvudung365-commits/english-center/actions/workflows/ci.yml/runs?branch=main&event=push&head_sha=$IMAGE_TAG&status=success&per_page=1" ]]
echo probe >>"$CALLS"
case $SCENARIO in
  api-error) exit 22 ;;
  bad-json) echo 'malformed'; exit ;;
  pending) if [[ $(grep -c '^probe$' "$CALLS") == 1 ]]; then echo '{"workflow_runs":[]}'; exit; fi ;;
esac
event=push conclusion=success sha=$IMAGE_TAG repo=nguyenvudung365-commits/english-center
case $SCENARIO in
  wrong-event) event=pull_request ;;
  wrong-sha) sha=other ;;
  wrong-repo) repo=some-fork/english-center ;;
  failed-ci) conclusion=failure ;;
esac
printf '{"workflow_runs":[{"id":981,"run_attempt":2,"event":"%s","head_branch":"main","head_sha":"%s","status":"completed","conclusion":"%s","head_repository":{"full_name":"%s"}}]}\n' "$event" "$sha" "$conclusion" "$repo"
MOCK
cat >"$work/docker" <<'MOCK'
#!/usr/bin/env bash
set -eu
[[ -z ${GH_TOKEN:-} ]]
case $* in
  "pull $PACKAGE_ROOT-api@$API_DIGEST") echo 'pull api' >>"$CALLS" ;;
  "pull $PACKAGE_ROOT-web@$WEB_DIGEST")
    echo 'pull web' >>"$CALLS"
    [[ $SCENARIO != partial-pair ]] ;;
  *) exit 99 ;;
esac
MOCK
cat >"$work/sleep" <<'MOCK'
#!/usr/bin/env bash
[[ $* == 60 ]] && echo wait >>"$CALLS"
MOCK
chmod +x "$work/"{git,gh,docker,sleep}

run_case() {
  : >"$CALLS"
  local status=0
  SCENARIO="$1" bash scripts/verified-release.sh >"$work/result" 2>"$work/log" || status=$?
  if [[ $2 == pass && $status == 0 ]] || [[ $2 == fail && $status != 0 ]]; then return; fi
  cat "$work/log" >&2
  echo "Unexpected result: $1 ($status)" >&2
  exit 1
}

run_case success pass
printf '%s\n' "$PACKAGE_ROOT-api@$API_DIGEST" "$PACKAGE_ROOT-web@$WEB_DIGEST" | diff -u - "$work/result"
run_case pending pass
printf '%s\n' probe wait probe manifest 'pull api' 'pull web' | diff -u - "$CALLS"
for scenario in stale git-error api-error bad-json wrong-event wrong-sha wrong-repo failed-ci missing-manifest bad-digest manifest-wrong-sha; do
  run_case "$scenario" fail
  [[ ! -s $work/result ]]
  if grep -q '^pull ' "$CALLS"; then echo "Unexpected image pull: $scenario" >&2; exit 1; fi
done
run_case partial-pair fail
[[ ! -s $work/result ]]
echo 'PASS: chỉ lấy cặp image của đúng lần CI main thành công.'
