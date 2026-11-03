#!/usr/bin/env bash
#
# Builds the solution and runs every test suite. Exits non-zero if anything fails.
#
# This is the gate: no change is considered reviewed, and no review is reported as passing, until
# this script exits 0. Every regression test it runs exists because a real defect reached the
# running system first; see docs/code-review.md.
#
# The frontend suite runs inside Docker on purpose. The Angular CLI requires Node >= 24.15 and the
# host may carry an older one, so running it in a pinned container is what makes the result the same
# everywhere, including CI.
#
# Usage:  scripts/verify.sh            build + all unit tests
#         scripts/verify.sh --smoke    also exercise the running stack (needs docker compose up)

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

run_smoke_tests=false
if [[ "${1:-}" == "--smoke" ]]; then
  run_smoke_tests=true
fi

failed_stages=()

announce() {
  printf '\n\033[1m==> %s\033[0m\n' "$1"
}

record_failure() {
  failed_stages+=("$1")
  printf '\033[31m    FALLÓ: %s\033[0m\n' "$1"
}

announce 'Compilando el backend (TreatWarningsAsErrors)'
if dotnet build src/backend --nologo; then
  printf '    OK\n'
else
  record_failure 'build del backend'
fi

for test_project in Domain Application Infrastructure Api; do
  announce "Tests de $test_project"
  # No --nologo here: it would be forwarded to the self-hosting test runner, which rejects it.
  if dotnet run --project "tests/EncodeLabs.$test_project.UnitTests" -c Release; then
    printf '    OK\n'
  else
    record_failure "tests de $test_project"
  fi
done

announce 'Tests del frontend (en Docker, Node fijado)'
# Se corren como una etapa de la imagen, no con un volumen montado: así las dependencias viven
# dentro de la imagen y el resultado es el mismo en cualquier máquina. Si los tests fallan, la
# construcción de la etapa falla.
frontend_test_log="$(mktemp)"
# TEST_RUN_ID busts the cache for the test layer only, so the suite really runs every time.
if docker build \
  --target tests \
  --progress plain \
  --build-arg "TEST_RUN_ID=$(date +%s)" \
  "$repository_root/src/frontend" \
  >"$frontend_test_log" 2>&1; then
  grep -E 'Test Files|Tests +[0-9]+ (passed|failed)' "$frontend_test_log" \
    | sed 's/^#[0-9]* //' || true
  printf '    OK\n'
else
  tail -40 "$frontend_test_log"
  record_failure 'tests del frontend'
fi
rm -f "$frontend_test_log"

if [[ "$run_smoke_tests" == true ]]; then
  announce 'Pruebas de humo contra el stack levantado'
  if "$repository_root/scripts/smoke-test.sh"; then
    printf '    OK\n'
  else
    record_failure 'pruebas de humo'
  fi
fi

printf '\n'
if [[ ${#failed_stages[@]} -eq 0 ]]; then
  printf '\033[32mTodo verde.\033[0m\n'
  exit 0
fi

printf '\033[31mFallaron %d etapa(s):\033[0m\n' "${#failed_stages[@]}"
for stage in "${failed_stages[@]}"; do
  printf '  - %s\n' "$stage"
done
exit 1
