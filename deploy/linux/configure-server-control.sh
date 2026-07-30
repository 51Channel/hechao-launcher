#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 || $# -gt 36 ]]; then
  echo "usage: $0 <environment-file> <true|false> <freshness-seconds> <claim-lease-seconds> [agent-id=sha256 ...]" >&2
  exit 64
fi

environment_file="$1"
enabled="${2,,}"
freshness_seconds="$3"
claim_lease_seconds="$4"
shift 4

agent_pattern='^[a-z0-9][a-z0-9._-]{1,63}$'
sha_pattern='^[0-9a-fA-F]{64}$'
if [[ "$enabled" != "true" && "$enabled" != "false" ]] ||
   [[ ! "$freshness_seconds" =~ ^[0-9]+$ ]] ||
   [[ ! "$claim_lease_seconds" =~ ^[0-9]+$ ]] ||
   (( freshness_seconds < 10 || freshness_seconds > 300 )) ||
   (( claim_lease_seconds < 30 || claim_lease_seconds > 600 )); then
  echo "server control configuration is invalid" >&2
  exit 65
fi

if [[ "$enabled" == "true" && $# -lt 1 ]]; then
  echo "enabled server control requires at least one agent digest" >&2
  exit 65
fi

if [[ ! -f "$environment_file" ]]; then
  echo "environment file does not exist: $environment_file" >&2
  exit 66
fi

declare -A seen_agents=()
agent_lines=()
for pair in "$@"; do
  if [[ "$pair" != *=* ]]; then
    echo "agent digest must use agent-id=sha256 syntax" >&2
    exit 65
  fi
  agent_id="${pair%%=*}"
  digest="${pair#*=}"
  if [[ ! "$agent_id" =~ $agent_pattern ||
        ! "$digest" =~ $sha_pattern ||
        -n "${seen_agents[$agent_id]:-}" ]]; then
    echo "agent digest is invalid or duplicated: $agent_id" >&2
    exit 65
  fi
  seen_agents["$agent_id"]=1
  agent_lines+=(
    "ServerControl__AgentTokenSha256__${agent_id}=${digest,,}"
  )
done

backup="${environment_file}.server-control.$(date -u +%Y%m%dT%H%M%SZ).bak"
cp --preserve=mode,ownership,timestamps -- "$environment_file" "$backup"
temporary="$(mktemp "${environment_file}.tmp.XXXXXX")"
trap 'rm -f -- "$temporary"' EXIT

grep -v -E '^ServerControl__' "$environment_file" > "$temporary"
cat >> "$temporary" <<EOF
ServerControl__Enabled=$enabled
ServerControl__AgentFreshnessSeconds=$freshness_seconds
ServerControl__ClaimLeaseSeconds=$claim_lease_seconds
EOF
printf '%s\n' "${agent_lines[@]}" >> "$temporary"

chown --reference="$environment_file" "$temporary"
chmod --reference="$environment_file" "$temporary"
mv -f -- "$temporary" "$environment_file"
trap - EXIT

echo "backup=$backup"
echo "environment=$environment_file"
echo "enabled=$enabled"
echo "agents=${#agent_lines[@]}"
