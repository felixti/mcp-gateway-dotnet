@client-profiles
Feature: Client profile schema transformations
  As an admin
  I want to set the client profile per server so generated schemas match client limitations
  So that MCP clients (Claude, Cursor, universal) receive schemas they can parse

  Background:
    Given a valid admin request context

  Scenario: Universal profile inlines $ref and splits anyOf into oneOf
    Given an OpenAPI spec with a $ref and an anyOf response named "universal-api"
    And the admin registers the server via POST /admin/servers
    And the server uses client_profile "universal"
    And the admin approves the server via POST /admin/servers/{name}/approve
    When an MCP client sends a tools/list request
    Then the tool input schema does not contain "$ref"
    And the tool output schema contains a "oneOf" key

  Scenario: Claude profile keeps anyOf at the schema root
    Given an OpenAPI spec with an anyOf response named "claude-api"
    And the admin registers the server via POST /admin/servers
    And the server uses client_profile "claude"
    And the admin approves the server via POST /admin/servers/{name}/approve
    When an MCP client sends a tools/list request
    Then the tool output schema contains an "anyOf" key

  Scenario: Cursor profile truncates long titles to 60 characters
    Given an OpenAPI spec with a long schema title named "cursor-api"
    And the admin registers the server via POST /admin/servers
    And the server uses client_profile "cursor"
    And the admin approves the server via POST /admin/servers/{name}/approve
    When an MCP client sends a tools/list request
    Then every schema "title" value is at most 60 characters long
