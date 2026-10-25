#!/usr/bin/env bash
# Fail-closed verification of the configured production DynamicsQuoteSync API key.
# Called before Docker images are built or running Pricing containers are touched.
set -euo pipefail
umask 077
envfile="${1:?usage: check-dynamics-pricing-credential.sh PREPARED_ENV_FILE}"
test -r "$envfile" || { echo "::error::Prepared production environment is unreadable."; exit 1; }
key="$(sed -n 's/^DynamicsQuoteSync__ApiKey=//p' "$envfile" | tail -n 1 | tr -d '\r')"
key="${key#\"}"
key="${key%\"}"
if [[ ! "$key" =~ ^dkw1_[A-Za-z0-9_-]{76}$ ]]; then
  echo "::error::DynamicsQuoteSync__ApiKey is absent or not a generated Dynamics key in the production host env. Re-deploying alone will NOT rotate the credential."
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
  echo "::error::The installed Pricing API key was rejected by the active Dynamics template schema (HTTP $schema_status). Check whether /opt/dhole/.env contains the NEW key for the new active template version."
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
