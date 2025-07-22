# Service Bus Connection String
resource "azurerm_key_vault_secret" "service_bus_connection" {
  name         = "ServiceBusConnectionString"
  value        = azurerm_servicebus_namespace.main.default_primary_connection_string
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# Redis Connection String
resource "azurerm_key_vault_secret" "redis_connection" {
  name         = "RedisConnectionString"
  value        = azurerm_redis_cache.main.primary_connection_string
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# Storage Connection String
resource "azurerm_key_vault_secret" "storage_connection" {
  name         = "StorageConnectionString"
  value        = azurerm_storage_account.main.primary_connection_string
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# Cosmos DB Connection String
resource "azurerm_key_vault_secret" "cosmos_connection" {
  name         = "CosmosDbConnectionString"
  value        = azurerm_cosmosdb_account.main.primary_sql_connection_string
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# Speech Service Key
resource "azurerm_key_vault_secret" "speech_key" {
  name         = "SpeechServiceKey"
  value        = azurerm_cognitive_account.speech.primary_access_key
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# Claude API Key
resource "azurerm_key_vault_secret" "claude_api_key" {
  name         = "ClaudeApiKey"
  value        = var.claude_api_key
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# OpenAI API Key (optional)
resource "azurerm_key_vault_secret" "openai_api_key" {
  count = var.openai_api_key != "" ? 1 : 0
  
  name         = "OpenAIApiKey"
  value        = var.openai_api_key
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# Application Insights Instrumentation Key
resource "azurerm_key_vault_secret" "appinsights_key" {
  name         = "ApplicationInsightsInstrumentationKey"
  value        = azurerm_application_insights.main.instrumentation_key
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
}

# Container Registry Admin Password
resource "azurerm_key_vault_secret" "acr_admin_password" {
  name         = "ContainerRegistryAdminPassword"
  value        = azurerm_container_registry.main.admin_password
  key_vault_id = azurerm_key_vault.main.id
  
  depends_on = [azurerm_key_vault_access_policy.terraform]
  
  lifecycle {
    ignore_changes = [value]
  }
}