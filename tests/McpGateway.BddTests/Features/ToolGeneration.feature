@tool-generation
Feature: Tool generation from OpenAPI spec
  As an admin
  I want to register an OpenAPI spec and have the gateway generate MCP tools
  So that MCP clients can discover and invoke the underlying API operations

  Background:
    Given a valid admin request context
    And the upstream API stub responds with status 200

  Scenario: Admin registers a 12-operation spec and 12 tools are generated
    Given an OpenAPI spec with 12 operations named "twelve-op-api"
    When the admin registers the server via POST /admin/servers
    Then 12 MCP tools are stored for the server
    And each tool has a name, description, and input schema

  Scenario: Tool with missing operationId gets synthesized name
    Given an OpenAPI spec with operation "GET /users/{id}" without operationId named "synth-name-api"
    When the admin registers the server via POST /admin/servers
    Then the server has a tool named "get_users_id"

  Scenario: Tools are listed via the MCP endpoint after admin approval
    Given an OpenAPI spec with 2 operations named "mcp-list-api"
    When the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve
    And an MCP client sends a tools/list request
    Then the response contains 2 tools

  Scenario: Admin approval is required before tools are visible to MCP clients
    Given an OpenAPI spec with 1 operation named "pending-api"
    When the admin registers the server via POST /admin/servers
    And an MCP client sends a tools/call for "getmetrics" without admin approval
    Then the response is a JSON-RPC error with code -32005
