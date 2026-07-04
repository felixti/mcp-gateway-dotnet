#!/usr/bin/env bash
set -euo pipefail

HOST="http://localhost:5121"
ADMIN_UPN="admin@example.com"
SERVER_NAME="petstore-smoke-$(date +%s)"

echo "=== Registering $SERVER_NAME ==="
curl -s -X POST "$HOST/admin/servers" \
  -H "X-Dev-Admin: $ADMIN_UPN" \
  -H "Content-Type: application/json" \
  -d "{
    \"name\": \"$SERVER_NAME\",
    \"displayName\": \"Petstore Smoke\",
    \"description\": \"Swagger Petstore v3 smoke test\",
    \"specSourceUrl\": \"https://petstore3.swagger.io/api/v3/openapi.json\",
    \"baseUrl\": \"https://petstore3.swagger.io/api/v3\",
    \"authStrategy\": \"passthrough\",
    \"authConfig\": {},
    \"toolMode\": \"all\",
    \"clientProfile\": \"universal\",
    \"pollIntervalMinutes\": 1440,
    \"createdBy\": \"$ADMIN_UPN\"
  }" | jq .

echo "=== Approving ==="
curl -s -X POST "$HOST/admin/servers/$SERVER_NAME/approve" \
  -H "X-Dev-Admin: $ADMIN_UPN" | jq .

echo "=== Creating API key ==="
KEY_RESPONSE=$(curl -s -X POST "$HOST/admin/servers/$SERVER_NAME/api-keys" \
  -H "X-Dev-Admin: $ADMIN_UPN" \
  -H "Content-Type: application/json" \
  -d '{"name":"smoke-test","scopes":["'"$SERVER_NAME"'"]}')
echo "$KEY_RESPONSE" | jq .
FULL_KEY=$(echo "$KEY_RESPONSE" | jq -r '.fullKey')

echo "=== Listing tools ==="
curl -s -X POST "$HOST/mcp/$SERVER_NAME" \
  -H "X-Gateway-Key: $FULL_KEY" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' | head -5

echo ""
echo "=== Calling findpetsbystatus ==="
curl -s -X POST "$HOST/mcp/$SERVER_NAME" \
  -H "X-Gateway-Key: $FULL_KEY" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"findpetsbystatus","arguments":{"status":"available"}}}' | head -5

echo ""
