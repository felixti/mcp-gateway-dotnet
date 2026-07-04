#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$(realpath "$SCRIPT_DIR/../src/McpGateway.Persistence/McpGateway.Persistence.csproj")"
TIMESTAMP=$(date +%Y%m%d)
OUTPUT="$SCRIPT_DIR/migration-${TIMESTAMP}.sql"

dotnet ef migrations script \
  --project "$PROJECT" \
  --idempotent \
  --output "$OUTPUT"

echo "Generated idempotent migration script: $OUTPUT"
