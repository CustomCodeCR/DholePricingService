#!/usr/bin/env bash
# Fail-closed verification of the configured production DynamicsQuoteSync API key.
# Called before Docker images are built or running Pricing containers are touched.
set -euo pipefail
umask 077
# Production secrets always originate from the canonical host file.
# The second argument is the prepared Compose env, only used for parity checks.
envfile="${1:-/opt/dhole/.env}"
runtime_envfile="${2:-}"
test -r "$envfile" || { echo "::error::Cannot read the canonical Pricing environment: $envfile."; exit 1; }
read_key() {
  local source="$1" count
  count="$(grep -c '^DynamicsQuoteSync__ApiKey=' "$source" || true)"
  if [[ "$count" != "1" ]]; then
    echo "::error::Expected one DynamicsQuoteSync__ApiKey entry in $source (found $count). Remove duplicates; no key values are logged." >&2
    return 1
  fi
  local value
  value="$(sed -n 's/^DynamicsQuoteSync__ApiKey=//p' "$source" | tr -d '\r')"
  value="${value#\"}"
  value="${value%\"}"
  value="${value#\'}"
  value="${value%\'}"
  printf '%s' "$value"
}
key="$(read_key "$envfile")"
if [[ -n "$runtime_envfile" ]]; then
  test -r "$runtime_envfile" || { echo "::error::Prepared Compose environment is unreadable: $runtime_envfile."; exit 1; }
  runtime_key="$(read_key "$runtime_envfile")"
  if [[ "$key" != "$runtime_key" ]]; then
    echo "::error::DynamicsQuoteSync__ApiKey in $runtime_envfile does not match the canonical $envfile. Pricing was not deployed." >&2
    exit 1
  fi
  unset runtime_key
fi
if [[ ! "$key" =~ ^dkw1_[A-Za-z0-9_-]{76}$ ]]; then
  echo "::error::DynamicsQuoteSync__ApiKey in $envfile is missing or has an invalid Dynamics key format. No secret has been logged."
  exit 1
fi
if grep -Eiq '^DynamicsQuoteSync__Enabled=(false|0)$' "$envfile"; then
  echo "::error::DynamicsQuoteSync is disabled in the production environment."
  exit 1
fi
workspace="$(mktemp -d)"
trap 'rm -rf "$workspace"; unset key' EXIT
base="https://api.logisticacastrofallas.com/api/dynamics-connector/keyed/templates/crear_oferta"
read_status() {
  local path="$1" output="$2"
  curl --silent --show-error --connect-timeout 10 --max-time 35 \
    --retry 1 --retry-delay 2 \
    -H "X-Dhole-Api-Key: $key" \
    -H "Accept: application/json" \
    --output "$output" --write-out "%{http_code}" "$base/$path?connectionCode=principal"
}
schema_status="$(read_status schema "$workspace/schema.json")"
if [[ "$schema_status" != "200" ]]; then
  if [[ "$schema_status" == "401" ]]; then
    echo "::error::Dynamics rejected the Pricing key for principal/crear_oferta (HTTP 401). The credential may be tied to an older template version, revoked, expired or different from the installed secret. In Dynamics > API CRUD > crear_oferta, use 'Reutilizar clave seleccionada' for a still-valid key; only if it is expired/revoked, create a replacement and securely update DynamicsQuoteSync__ApiKey in /opt/dhole/.env. No containers were changed."
  else
    echo "::error::Dynamics could not validate the installed Pricing key against the active principal/crear_oferta schema (HTTP $schema_status). No containers were changed."
  fi
  exit 1
fi
ready_status="$(read_status write-readiness "$workspace/readiness.json")"
if [[ "$ready_status" != "200" ]]; then
  echo "::error::Dynamics write-readiness could not validate the installed Pricing API key (HTTP $ready_status). No containers have been changed."
  exit 1
fi
python3 - "$workspace/schema.json" "$workspace/readiness.json" <<'PY'
import json
import sys
try:
    with open(sys.argv[1], encoding="utf-8") as f:
        schema = json.load(f)
    with open(sys.argv[2], encoding="utf-8") as f:
        readiness = json.load(f)
    version = schema["version"]
    fields = schema["fields"]
    if (schema.get("name") != "crear_oferta" or schema.get("entityName") != "quote"
            or schema.get("operation") != "create" or schema.get("connectionCode") != "principal"
            or not isinstance(version, int)):
        raise ValueError("The key does not resolve to a valid active crear_oferta schema.")
    currency = [field for field in fields
                if field.get("name", "").lower() == "transactioncurrencyid"
                and field.get("target", "").endswith("@odata.bind")]
    if len(currency) != 1:
        raise ValueError("The active quote template does not map transactioncurrencyid to a Dynamics @odata.bind relationship.")
    if readiness.get("ready") is not True or readiness.get("code") != "DYNAMICS_QUOTE_WRITES_READY":
        missing = readiness.get("missingGrants") or []
        safe_fields = [x for x in missing if isinstance(x, str) and len(x) < 128]
        # A missing grant is not a missing/incorrect API key. The Pricing
        # worker independently checks write-readiness every two minutes and
        # will not dispatch any QUO while Dynamics reports not-ready.
        # Allow a valid credential's production container refresh so the
        # new secret from /opt/dhole/.env is actually loaded. Other errors
        # (authorization failures, production policy, unexpected payloads)
        # remain deployment blockers.
        if (readiness.get("code") == "quote_grants_incomplete"
                and readiness.get("noPostExecuted") is True
                and isinstance(missing, list) and safe_fields):
            print("::warning::The active Dynamics key and crear_oferta schema are valid,"
                  " but approved field grants are incomplete: "
                  + ", ".join(safe_fields[:20])
                  + ". Pricing will deploy with QUO delivery PAUSED until"
                  " Dynamics reports DYNAMICS_QUOTE_WRITES_READY.")
            sys.exit(0)
        raise ValueError("Dynamics write-readiness denied ("
                         + str(readiness.get("code", "unknown"))
                         + "). Deployment stopped before changing containers.")
    print(f"Verified current Pricing key: active crear_oferta v{version}, currency relationship and write grants ready. No Dataverse write performed.")
except (OSError, KeyError, ValueError, TypeError, json.JSONDecodeError) as exc:
    print(f"::error::Dynamics key preflight failed: {exc}", file=sys.stderr)
    sys.exit(1)
PY
