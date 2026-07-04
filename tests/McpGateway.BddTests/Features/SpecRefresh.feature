@spec-refresh
Feature: Spec refresh detects changes and requires re-approval
  As an admin
  I want the gateway to detect when the OpenAPI spec changes and require re-approval
  So that I can review new or modified tool descriptions before exposing them

  Background:
    Given a valid admin request context

  Scenario: Spec change adds an endpoint and sets changes_pending
    Given an OpenAPI spec with 2 operations named "refresh-api"
    And the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve
    When the admin uploads a new spec with an added endpoint to the server
    Then the server approval status is "changes_pending"
    And the server has 3 tools in the database

  Scenario: Spec change replaces a tool description
    Given an OpenAPI spec with 1 operation named "description-api"
    And the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve
    When the admin uploads a new spec with a changed tool description to the server
    Then the tool description in the database matches the new spec

  Scenario: Unchanged spec refresh does not change approval status
    Given an OpenAPI spec with 2 operations named "stable-api"
    And the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve
    When the admin uploads the same spec again to the server
    Then the server approval status is "approved"

  Scenario: tools/call returns -32005 while changes are pending
    Given an OpenAPI spec with 1 operation named "paused-api"
    And the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve
    When the admin uploads a new spec with an added endpoint to the server
    And an MCP client sends a tools/call for "getmetrics" with arguments {}
    Then the response is a JSON-RPC error with code -32005

  Scenario: Admin re-approval restores tool calls after changes_pending
    Given an OpenAPI spec with 1 operation named "restore-api"
    And the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve
    And the admin uploads a new spec with a changed tool description to the server
    When the admin re-approves the server via POST /admin/servers/{name}/approve
    Then the server approval status is "approved"
