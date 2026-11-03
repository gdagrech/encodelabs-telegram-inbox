#!/usr/bin/env bash
#
# Checks that only the assembled system can answer. Needs the stack running (docker compose up -d).
#
# Unit tests cannot reach these: they are properties of how the containers are wired, not of any
# single class. CR-024 is the reason this file exists — nginx cached the API's address at startup,
# so every request failed with 502 while every container reported healthy, and no amount of unit
# testing would have caught it.

set -uo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

readonly inbox_url='http://localhost:4200'
readonly api_url='http://localhost:8080'

checks_failed=0

check() {
  local description="$1"
  local actual="$2"
  local expected="$3"

  if [[ "$actual" == "$expected" ]]; then
    printf '  \033[32mOK\033[0m   %s\n' "$description"
  else
    printf '  \033[31mFALLA\033[0m %s (esperaba %s, obtuvo %s)\n' "$description" "$expected" "$actual"
    checks_failed=$((checks_failed + 1))
  fi
}

status_of() {
  curl --silent --output /dev/null --write-out '%{http_code}' --max-time 10 "$@"
}

# Polls until the expected status appears, for cases where recovery is correct but not instant.
status_within() {
  local expected="$1"
  local attempts="$2"
  shift 2

  local last_status=''
  for _ in $(seq 1 "$attempts"); do
    last_status="$(status_of "$@")"
    [[ "$last_status" == "$expected" ]] && break
    sleep 2
  done

  printf '%s' "$last_status"
}

printf '\033[1mPruebas de humo\033[0m\n'

printf '\nLa API responde directamente\n'
check 'GET /health' "$(status_of "$api_url/health")" '200'
check 'GET /api/contacts' "$(status_of "$api_url/api/contacts")" '200'

printf '\nLa bandeja y el proxy responden\n'
check 'GET / (la página)' "$(status_of "$inbox_url/")" '200'
check 'GET /api/contacts por nginx' "$(status_of "$inbox_url/api/contacts")" '200'
check 'POST /hubs/inbox/negotiate por nginx' \
  "$(status_of --request POST "$inbox_url/hubs/inbox/negotiate?negotiateVersion=1")" '200'

printf '\nEl webhook rechaza lo que no trae el secreto\n'
check 'POST /api/telegram/webhook sin cabecera' \
  "$(status_of --request POST --header 'Content-Type: application/json' --data '{}' \
    "$api_url/api/telegram/webhook")" '401'
check 'POST /api/telegram/webhook con secreto incorrecto' \
  "$(status_of --request POST --header 'Content-Type: application/json' \
    --header 'X-Telegram-Bot-Api-Secret-Token: no-es-el-secreto' --data '{}' \
    "$api_url/api/telegram/webhook")" '401'

printf '\nLos cuerpos malformados son culpa del cliente, no del servidor (CR-021)\n'
check 'POST /api/contacts/<id>/messages con JSON roto' \
  "$(status_of --request POST --header 'Content-Type: application/json' --data '{roto' \
    "$api_url/api/contacts/00000000-0000-0000-0000-000000000000/messages")" '400'

printf '\nUn contacto inexistente es 404, no 500\n'
check 'GET /api/contacts/<id>/messages inexistente' \
  "$(status_of "$api_url/api/contacts/00000000-0000-0000-0000-000000000000/messages")" '404'

# CR-024. nginx used to resolve "api" once, when its configuration loaded, and cache that address
# forever. Recreating the API container gave it a new one and the inbox broke entirely —with every
# container healthy— until nginx was restarted too.
#
# This is checked in two ways, because neither alone is enough. The first pins the mechanism and is
# deterministic. The second exercises the behaviour, but forcing a *different* address requires a
# user-configured subnet in the Compose network, which this project deliberately does not declare:
# production configuration should not change to make a test convenient. When Docker happens to hand
# the same address back, the second check says so rather than claiming a pass it did not earn.
printf '\nnginx vuelve a resolver la API cuando cambia de dirección (CR-024)\n'

# El contenedor se resuelve por Compose en vez de nombrarlo a mano: el nombre depende del proyecto
# y hardcodearlo rompe el guion en cualquier entorno que no sea esta máquina.
api_container_id="$(docker compose ps --quiet api 2>/dev/null)"

compose_network=''
if [[ -n "$api_container_id" ]]; then
  compose_network="$(docker inspect --format '{{range $name, $_ := .NetworkSettings.Networks}}{{$name}}{{end}}' \
    "$api_container_id" 2>/dev/null)"
fi

if [[ -z "$compose_network" ]]; then
  printf '  \033[33mSALTEADA\033[0m el contenedor api no está corriendo\n'
else
  address_before="$(docker inspect --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' \
    "$api_container_id")"

  # The mechanism: nginx must name the upstream through a variable and have a resolver configured.
  # A literal host is resolved once and cached forever, which is the defect itself. Deterministic,
  # so it runs every time and is the check that actually guards the regression.
  effective_nginx_config="$(docker compose exec -T frontend cat /etc/nginx/conf.d/default.conf 2>/dev/null)"

  if grep -q 'resolver ' <<<"$effective_nginx_config" \
    && grep -q 'proxy_pass http://\$' <<<"$effective_nginx_config" \
    && ! grep -q 'proxy_pass http://api:' <<<"$effective_nginx_config"; then
    printf '  \033[32mOK\033[0m   nginx nombra la API por variable y tiene resolver configurado\n'
  else
    printf '  \033[31mFALLA\033[0m nginx volvió a un host literal: va a cachear la dirección\n'
    checks_failed=$((checks_failed + 1))
  fi

  docker compose stop api >/dev/null 2>&1
  docker compose rm --force api >/dev/null 2>&1

  # A throwaway container competes for the freed address. It may or may not win; see the note above.
  docker run --detach --rm --name 'encodelabs-address-squatter' \
    --network "$compose_network" alpine sleep 180 >/dev/null 2>&1

  docker compose up --detach api >/dev/null 2>&1

  for _ in $(seq 1 24); do
    [[ "$(status_of "$api_url/health")" == '200' ]] && break
    sleep 3
  done

  address_after="$(docker inspect --format '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' \
    "$(docker compose ps --quiet api)")"

  if [[ "$address_before" == "$address_after" ]]; then
    # Honest wording: Docker handed the same address back, so recreating the container did not
    # exercise re-resolution. The mechanism check above still ran; this one simply had no scenario.
    printf '  \033[33mSIN ESCENARIO\033[0m Docker devolvió la misma dirección (%s)\n' "$address_after"
    check 'GET /api/contacts por nginx tras recrear la API' \
      "$(status_within 200 10 "$inbox_url/api/contacts")" '200'
  else
    printf '  la API pasó de %s a %s, sin reiniciar nginx\n' "$address_before" "$address_after"

    # Recovery is correct but not instant: nginx keeps the previous answer until the resolver's TTL
    # expires, so there is a window of a few seconds where requests still fail. That window is the
    # point of the fix —before it, nginx never recovered at all— so the check polls instead of
    # demanding an immediate 200.
    check 'GET /api/contacts por nginx tras el cambio' \
      "$(status_within 200 10 "$inbox_url/api/contacts")" '200'
    check 'POST /hubs/inbox/negotiate por nginx tras el cambio' \
      "$(status_within 200 10 --request POST "$inbox_url/hubs/inbox/negotiate?negotiateVersion=1")" '200'
  fi

  docker stop 'encodelabs-address-squatter' >/dev/null 2>&1
fi

printf '\n'
if [[ "$checks_failed" -eq 0 ]]; then
  printf '\033[32mPruebas de humo: todo verde.\033[0m\n'
  exit 0
fi

printf '\033[31mPruebas de humo: %d comprobación(es) fallaron.\033[0m\n' "$checks_failed"
exit 1
