# API Management Additional Configuration

# Import OpenAPI Specification
resource "azurerm_api_management_api_version_set" "voicecode" {
  name                = "voicecode-api-version-set"
  resource_group_name = azurerm_resource_group.main.name
  api_management_name = azurerm_api_management.main.name
  display_name        = "VoiceCode API"
  versioning_scheme   = "Header"
  version_header_name = "X-API-Version"
}

# Create API from OpenAPI spec
resource "azurerm_api_management_api" "voicecode_v1" {
  name                  = "voicecode-api-v1"
  resource_group_name   = azurerm_resource_group.main.name
  api_management_name   = azurerm_api_management.main.name
  revision              = "1"
  version               = "v1"
  display_name          = "VoiceCode API v1"
  path                  = "v1"
  protocols             = ["https"]
  api_version_set_id    = azurerm_api_management_api_version_set.voicecode.id
  subscription_required = true
  
  import {
    content_format = "openapi"
    content_value  = file("${path.module}/../openapi/voicecode-api.yaml")
  }
}

# Create Named Values (for policy expressions)
resource "azurerm_api_management_named_value" "tenant_id" {
  name                = "tenant-id"
  resource_group_name = azurerm_resource_group.main.name
  api_management_name = azurerm_api_management.main.name
  display_name        = "Azure AD Tenant ID"
  value               = data.azurerm_client_config.current.tenant_id
}

resource "azurerm_api_management_named_value" "client_id" {
  name                = "client-id"
  resource_group_name = azurerm_resource_group.main.name
  api_management_name = azurerm_api_management.main.name
  display_name        = "Azure AD Client ID"
  value               = azuread_application.webapp.application_id
}

# Custom domains (if provided)
resource "azurerm_api_management_custom_domain" "main" {
  count               = var.domain_name != "" ? 1 : 0
  api_management_id   = azurerm_api_management.main.id
  
  gateway {
    host_name    = "api.${var.domain_name}"
    key_vault_id = azurerm_key_vault_certificate.api[0].secret_id
  }
  
  developer_portal {
    host_name    = "developer.${var.domain_name}"
    key_vault_id = azurerm_key_vault_certificate.developer[0].secret_id
  }
}

# Subscription Templates
resource "azurerm_api_management_subscription" "internal" {
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  display_name        = "Internal Services"
  product_id          = azurerm_api_management_product.main.id
  state               = "active"
  allow_tracing       = var.environment != "prod"
}

# API Diagnostics
resource "azurerm_api_management_api_diagnostic" "voicecode" {
  identifier               = "applicationinsights"
  resource_group_name      = azurerm_resource_group.main.name
  api_management_name      = azurerm_api_management.main.name
  api_name                 = azurerm_api_management_api.voicecode_v1.name
  api_management_logger_id = azurerm_api_management_logger.appinsights.id
  
  sampling_percentage       = var.environment == "prod" ? 10.0 : 100.0
  always_log_errors         = true
  log_client_ip             = true
  verbosity                 = var.environment == "prod" ? "error" : "information"
  http_correlation_protocol = "W3C"
  
  frontend_request {
    body_bytes = 1024
    headers_to_log = [
      "content-type",
      "accept",
      "origin",
      "x-correlation-id"
    ]
  }
  
  frontend_response {
    body_bytes = 1024
    headers_to_log = [
      "content-type",
      "x-correlation-id",
      "x-ratelimit-remaining"
    ]
  }
  
  backend_request {
    body_bytes = 1024
    headers_to_log = [
      "content-type",
      "x-correlation-id",
      "x-service-name"
    ]
  }
  
  backend_response {
    body_bytes = 1024
    headers_to_log = [
      "content-type",
      "x-correlation-id",
      "x-response-time"
    ]
  }
}

# User Groups
resource "azurerm_api_management_group" "developers" {
  name                = "voicecode-developers"
  resource_group_name = azurerm_resource_group.main.name
  api_management_name = azurerm_api_management.main.name
  display_name        = "VoiceCode Developers"
  description         = "External developers using VoiceCode APIs"
  type                = "custom"
}

resource "azurerm_api_management_group" "internal" {
  name                = "voicecode-internal"
  resource_group_name = azurerm_resource_group.main.name
  api_management_name = azurerm_api_management.main.name
  display_name        = "VoiceCode Internal"
  description         = "Internal services and applications"
  type                = "custom"
}

# Email Templates
resource "azurerm_api_management_email_template" "welcome" {
  template_name       = "NewIssueNotificationMessage"
  api_management_name = azurerm_api_management.main.name
  resource_group_name = azurerm_resource_group.main.name
  subject             = "Welcome to VoiceCode API"
  body                = file("${path.module}/../templates/welcome-email.html")
}

# Cache configuration
resource "azurerm_api_management_redis_cache" "external" {
  name                = "${local.resource_prefix}-apim-cache"
  api_management_id   = azurerm_api_management.main.id
  connection_string   = azurerm_redis_cache.main.primary_connection_string
  description         = "External Redis cache for API responses"
  redis_cache_id      = azurerm_redis_cache.main.id
  cache_location      = var.location
}

# Developer Portal Content
resource "null_resource" "developer_portal_content" {
  depends_on = [azurerm_api_management.main]
  
  provisioner "local-exec" {
    command = <<EOT
      az apim api import \
        --path "v1" \
        --api-id "voicecode-api-v1" \
        --resource-group "${azurerm_resource_group.main.name}" \
        --service-name "${azurerm_api_management.main.name}" \
        --specification-path "${path.module}/../openapi/voicecode-api.yaml" \
        --specification-format "OpenApiJson"
    EOT
  }
}