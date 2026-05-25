#!/usr/bin/env bash
# Regression guard (POSIX). Requires ripgrep (rg).
# Usage: ./scripts/check-obsolete-api.sh
#        REPO_ROOT=/path/to/repo ./scripts/check-obsolete-api.sh

set -euo pipefail

REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "$0")/.." && pwd)}"
cd "$REPO_ROOT"

FAILED=0

fail() { echo "FAIL: $1" >&2; FAILED=1; }
pass() { echo "PASS: $1"; }

RG_OPTS=(--glob '*.cs' --glob '!**/obj/**' --glob '!**/bin/**' --glob '!**/*.backup')

echo ""
echo "=== Obsolete API call sites ==="

# .HasActiveBuffInGame( and .HasQuestInJournal( — exclude definition files
for api in HasActiveBuffInGame HasQuestInJournal; do
  mapfile -t hits < <(rg "${RG_OPTS[@]}" -n "\\.${api}\\s*\\(" . \
    -g '!Services/StateService.cs' -g '!Models/PlayerStressState.cs' 2>/dev/null || true)
  count=0
  for hit in "${hits[@]}"; do
    file="${hit%%:*}"
    rest="${hit#*:}"
    line="${rest%%:*}"
    text="${rest#*:}"
    fail "${api} at ${file}:${line} — ${text}"
    count=$((count + 1))
  done
  if [[ $count -eq 0 ]]; then
    pass "No production calls to ${api}"
  fi
done

echo ""
echo "=== ActiveTreatments direct mutation (baseline allowlist) ==="

# Baseline lines (file:line) — keep in sync with check-obsolete-api.ps1
ALLOW=(
  "Models/PlayerStressState.cs:261"
  "Models/PlayerStressState.cs:283"
  "Models/PlayerStressState.cs:306"
  "Models/PlayerStressState.cs:320"
  "Models/PlayerStressState.cs:329"
  "Models/PlayerStressState.cs:331"
)

is_allowed() {
  local key="$1"
  for a in "${ALLOW[@]}"; do
    [[ "$key" == "$a" ]] && return 0
  done
  return 1
}

mapfile -t muts < <(rg "${RG_OPTS[@]}" -n 'ActiveTreatments\s*(\[[^\]]+\]\s*=|\.Clear\s*\(|\.Add\s*\(|\.Remove\s*\()' . 2>/dev/null || true)
mut_fail=0
for hit in "${muts[@]}"; do
  file="${hit%%:*}"
  rest="${hit#*:}"
  line="${rest%%:*}"
  key="${file}:${line}"
  if is_allowed "$key"; then
    continue
  fi
  fail "ActiveTreatments mutation at ${key} — ${rest#*:}"
  mut_fail=1
done
if [[ $mut_fail -eq 0 ]]; then
  pass "No new ActiveTreatments direct mutations outside baseline allowlist"
fi

echo ""
echo "=== Debug explicit API (advisory) ==="
debug_hits=$(rg "${RG_OPTS[@]}" -n '\\.(HasActiveBuffInGame|HasQuestInJournal)\\s*\\(' Helpers/HsDebugReporter.cs Handlers/ConsoleCommandHandler.cs 2>/dev/null || true)
if [[ -z "$debug_hits" ]]; then
  pass "HsDebugReporter / ConsoleCommandHandler do not use obsolete API names"
else
  echo "WARN: obsolete API in debug files:"
  echo "$debug_hits"
fi

echo ""
if [[ $FAILED -ne 0 ]]; then
  echo "Regression check FAILED." >&2
  exit 1
fi
echo "Regression check PASSED."
exit 0
