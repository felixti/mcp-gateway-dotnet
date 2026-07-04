@tool-call-proxy
Feature: Tool call proxy
  As an MCP client
  I want the gateway to proxy my tool calls to the underlying API
  So that I can invoke the underlying API through the MCP protocol

  Background:
    Given a valid admin request context
    And an OpenAPI spec with 3 operations named "proxy-api"
    And the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve
    And the upstream API stub responds with status 200 and body "{\"items\":[1,2,3]}"

  Scenario: GET tool call substitutes path parameter and returns response
    When an MCP client sends a tools/call for "getitem" with arguments {"id": "abc"}
    Then the response status is 200
    And the response body contains "items"
    And the upstream API received a GET request to "/items/abc"

  Scenario: Query parameters are forwarded to the underlying API
    When an MCP client sends a tools/call for "listitems" with arguments {"limit": 5, "offset": 10}
    Then the response status is 200
    And the upstream API received a GET request with query "limit=5&offset=10"

  Scenario: POST tool call serializes body as JSON
    Given the upstream API stub captures the request body
    When an MCP client sends a tools/call for "createitem" with arguments {"body": {"id": 42}}
    Then the upstream API received a JSON body with "id" equal to 42

  Scenario: 5xx upstream response is wrapped with isError
    Given the upstream API stub responds with status 503 and body "service down"
    When an MCP client sends a tools/call for "listitems" with arguments {}
    Then the response is a tool result with isError true
    And the response content contains "[HTTP 503]"

  Scenario: Large upstream response is truncated to 10KB
    Given the upstream API stub responds with status 200 and a 20KB body
    When an MCP client sends a tools/call for "listitems" with arguments {}
    Then the response content length is at most 10240 characters
