#!/usr/bin/env bash
# Offline tests: no actual API key, network request, Docker or Dataverse write.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
check="$root/scripts/check-dynamics-pricing-credential.sh"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin"
cat > "$tmp/bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
route=''
output=''
while (($#)); do
  case "$1" in
    --output) output="$2"; shift 2 ;;
    *'/schema?connectionCode=principal'*) route=schema; shift ;;
    *'/write-readiness?connectionCode=principal'*) route=readiness; shift ;;
    *) shift ;;
  esac
done
test -n "$output"
case "$route" in
  schema)
    printf '{"name":"crear_oferta","version":7,"fields":[{"name":"transactioncurrencyid","target":"transactioncurrencyid@odata.bind"}],"operation":"%s","entityName":"quote","connectionCode":"principal"}\n' "${MOCK_OPERATION:-create}" > "$output"
    printf '%s' "${MOCK_SCHEMA_STATUS:-200}" ;;
  readiness)
    printf '%s\n' '{"ready":true,"code":"DYNAMICS_QUOTE_WRITES_READY"}' > "$output"
    printf '%s' "${MOCK_READINESS_STATUS:-200}" ;;
  *) echo 'Unexpected mocked endpoint' >&2; exit 99 ;;
esac
MOCK
chmod +x "$tmp/bin/curl"
key="dkw1_0123456789abcdef0123456789abcdef_$(printf 'A%.0s' {1..43})"
printf 'DynamicsQuoteSync__ApiKey=%s\nDynamicsQuoteSync__Enabled=true\n' "$key" > "$tmp/canonical.env"
cp "$tmp/canonical.env" "$tmp/runtime.env"
run_case() {
  local label="$1" expected_status="$2" expected_text="$3"
  shift 3
  local result status
  set +e
  result="$(env PATH="$tmp/bin:$PATH" "$@" bash "$check" "$tmp/canonical.env" "$tmp/runtime.env" 2>&1)"
  status=$?
  set -e
  if [[ "$status" != "$expected_status" || "$result" != *"$expected_text"* ]]; then
    printf 'FAILED: %s (exit %s, expected %s)\n%s\n' "$label" "$status" "$expected_status" "$result" >&2
    exit 1
  fi
  echo "PASS: $label"
}
run_case 'valid schema and grants' 0 'Verified current Pricing key: active crear_oferta v7'
run_case 'upsert schema and grants are accepted' 0 'Verified current Pricing key: active crear_oferta v7' MOCK_OPERATION=upsert
run_case 'unsupported update operation remains blocked' 1 'valid active crear_oferta schema' MOCK_OPERATION=update
run_case 'schema HTTP 401 pauses QUO without blocking deployment' 0 'delivery stays PAUSED' MOCK_SCHEMA_STATUS=401
run_case 'readiness HTTP 401 pauses QUO without blocking deployment' 0 'delivery stays PAUSED' MOCK_READINESS_STATUS=401
run_case 'strict mode still blocks unauthorized key' 1 '::error::Dynamics returned HTTP 401' MOCK_SCHEMA_STATUS=401 DYNAMICS_PREFLIGHT_STRICT=1
run_case 'schema HTTP 500 still fails' 1 'HTTP 500' MOCK_SCHEMA_STATUS=500
run_case 'readiness HTTP 403 still fails' 1 'HTTP 403' MOCK_READINESS_STATUS=403
printf 'DynamicsQuoteSync__ApiKey=dkw1_wrong\n' > "$tmp/runtime.env"
run_case 'canonical/runtime mismatch remains fatal' 1 'does not match the canonical'
printf 'DynamicsQuoteSync__ApiKey=invalid\n' > "$tmp/canonical.env"
cp "$tmp/canonical.env" "$tmp/runtime.env"
run_case 'invalid key format remains fatal' 1 'invalid Dynamics key format'
echo 'All offline Dynamics Pricing credential checks passed.'
