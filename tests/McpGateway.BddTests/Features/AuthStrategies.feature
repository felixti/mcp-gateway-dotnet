@auth-strategies
Feature: Auth strategies (OBO, passthrough, static)
  As a security officer
  I want the gateway to authenticate to the underlying API using the configured strategy
  So that per-user attribution is preserved (OBO), tokens are forwarded transparently (passthrough), or simple API keys are used (static)

  Background:
    Given a valid admin request context
    And an OpenAPI spec with 1 operation named "auth-api"
    And the admin registers the server via POST /admin/servers
    And the admin approves the server via POST /admin/servers/{name}/approve

  Scenario: Static strategy stores the configured API key in the database
    Given the server uses auth_strategy "static" with api_key "secret-12345"
    Then the server has auth_strategy "static" in the database
    And the server auth_config contains "secret-12345" in the database

  Scenario: OBO strategy stores the resource in auth_config
    Given the server uses auth_strategy "obo" with resource "api://target-api/.default"
    Then the server has auth_strategy "obo" in the database
    And the server auth_config contains "api://target-api/.default" in the database

  Scenario: Passthrough strategy stores empty auth_config
    Given the server uses auth_strategy "passthrough"
    Then the server has auth_strategy "passthrough" in the database

