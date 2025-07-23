locals {
  # Naming convention
  resource_prefix = "${var.project_name}-${var.environment}-${var.location_short}"
  
  # Common tags
  common_tags = merge(var.tags, {
    Environment = var.environment
    Location    = var.location
  })
  
  # Service names
  services = {
    stt        = "stt"
    claude     = "claude"
    router     = "router"
    generator  = "generator"
    tts        = "tts"
    dispatcher = "dispatcher"
  }
  
  # Service Bus queues
  service_bus_queues = [
    "stt-processing",
    "claude-processing",
    "code-generation",
    "tts-processing",
    "dispatcher"
  ]
  
  # Service Bus topics
  service_bus_topics = [
    {
      name = "voice-events"
      subscriptions = [
        "web-app",
        "monitoring",
        "analytics"
      ]
    }
  ]
  
  # Cosmos DB containers
  cosmos_containers = [
    {
      name               = "sessions"
      partition_key_path = "/userId"
    },
    {
      name               = "conversations"
      partition_key_path = "/sessionId"
    },
    {
      name               = "projects"
      partition_key_path = "/userId"
    },
    {
      name               = "templates"
      partition_key_path = "/language"
    }
  ]
  
  # Storage containers
  storage_containers = [
    "audio-files",
    "generated-code",
    "templates",
    "logs"
  ]
  
  # App Service environment variables (common)
  common_app_settings = {
    "ASPNETCORE_ENVIRONMENT"                        = var.environment == "prod" ? "Production" : "Development"
    "ApplicationInsights__ConnectionString"         = azurerm_application_insights.main.connection_string
    "KeyVaultName"                                  = azurerm_key_vault.main.name
    "ConnectionStrings__ServiceBus"                 = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=ServiceBusConnectionString)"
    "ConnectionStrings__Redis"                      = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=RedisConnectionString)"
    "ConnectionStrings__Storage"                    = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=StorageConnectionString)"
    "ConnectionStrings__CosmosDb"                   = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=CosmosDbConnectionString)"
    "AzureAd__TenantId"                            = data.azurerm_client_config.current.tenant_id
    "AzureAd__Instance"                            = "https://login.microsoftonline.com/"
    "Cors__AllowedOrigins__0"                      = var.domain_name != "" ? "https://${var.domain_name}" : "https://placeholder-frontend.example.com"
    "Cors__AllowedOrigins__1"                      = "http://localhost:3000"
  }
  
  # Service-specific endpoints
  service_endpoints = {
    "ServiceEndpoints__STTService"       = "https://${local.resource_prefix}-app-stt.azurewebsites.net"
    "ServiceEndpoints__ClaudeService"    = "https://${local.resource_prefix}-app-claude.azurewebsites.net"
    "ServiceEndpoints__RouterService"    = "https://${local.resource_prefix}-app-router.azurewebsites.net"
    "ServiceEndpoints__GeneratorService" = "https://${local.resource_prefix}-app-generator.azurewebsites.net"
    "ServiceEndpoints__TTSService"       = "https://${local.resource_prefix}-app-tts.azurewebsites.net"
  }
}