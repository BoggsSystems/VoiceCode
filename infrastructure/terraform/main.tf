# Data sources
data "azurerm_client_config" "current" {}

# Resource Group
resource "azurerm_resource_group" "main" {
  name     = "${local.resource_prefix}-rg"
  location = var.location
  tags     = local.common_tags
}

# Key Vault
resource "azurerm_key_vault" "main" {
  name                = "${var.project_name}${var.environment}kv${var.location_short}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"
  
  purge_protection_enabled    = var.environment == "prod"
  soft_delete_retention_days  = var.environment == "prod" ? 90 : 7
  
  network_acls {
    default_action = "Deny"
    bypass         = "AzureServices"
    ip_rules       = var.allowed_ip_ranges
  }
  
  tags = local.common_tags
}

# Key Vault Access Policy for Terraform
resource "azurerm_key_vault_access_policy" "terraform" {
  key_vault_id = azurerm_key_vault.main.id
  tenant_id    = data.azurerm_client_config.current.tenant_id
  object_id    = data.azurerm_client_config.current.object_id
  
  secret_permissions = [
    "Get", "List", "Set", "Delete", "Recover", "Backup", "Restore", "Purge"
  ]
}

# Application Insights
resource "azurerm_log_analytics_workspace" "main" {
  name                = "${local.resource_prefix}-logs"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = local.common_tags
}

resource "azurerm_application_insights" "main" {
  name                = "${local.resource_prefix}-appinsights"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.common_tags
}

# Service Bus Namespace
resource "azurerm_servicebus_namespace" "main" {
  name                = "${local.resource_prefix}-sbus"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = var.servicebus_sku
  tags                = local.common_tags
}

# Service Bus Queues
resource "azurerm_servicebus_queue" "queues" {
  for_each = toset(local.service_bus_queues)
  
  name         = each.value
  namespace_id = azurerm_servicebus_namespace.main.id
  
  enable_partitioning = true
  default_message_ttl = "PT5M"
  
  dead_lettering_on_message_expiration = true
  max_delivery_count                   = 10
}

# Service Bus Topics
resource "azurerm_servicebus_topic" "topics" {
  for_each = { for topic in local.service_bus_topics : topic.name => topic }
  
  name         = each.value.name
  namespace_id = azurerm_servicebus_namespace.main.id
  
  enable_partitioning = true
  default_message_ttl = "PT5M"
}

# Service Bus Subscriptions
resource "azurerm_servicebus_subscription" "subscriptions" {
  for_each = { 
    for item in flatten([
      for topic in local.service_bus_topics : [
        for sub in topic.subscriptions : {
          key              = "${topic.name}-${sub}"
          topic_name       = topic.name
          subscription_name = sub
        }
      ]
    ]) : item.key => item
  }
  
  name     = each.value.subscription_name
  topic_id = azurerm_servicebus_topic.topics[each.value.topic_name].id
  
  max_delivery_count = 10
  default_message_ttl = "PT5M"
  
  dead_lettering_on_message_expiration = true
  dead_lettering_on_filter_evaluation_error = true
}

# Redis Cache
resource "azurerm_redis_cache" "main" {
  name                = "${local.resource_prefix}-redis"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku_name            = var.redis_sku.name
  family              = var.redis_sku.family
  capacity            = var.redis_sku.capacity
  
  enable_non_ssl_port = false
  minimum_tls_version = "1.2"
  
  redis_configuration {
    enable_authentication = true
  }
  
  tags = local.common_tags
}

# Storage Account
resource "azurerm_storage_account" "main" {
  name                     = "${var.project_name}${var.environment}stor${var.location_short}"
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = var.storage_replication_type
  
  min_tls_version = "TLS1_2"
  
  blob_properties {
    delete_retention_policy {
      days = 7
    }
    
    versioning_enabled = true
  }
  
  tags = local.common_tags
}

# Storage Containers
resource "azurerm_storage_container" "containers" {
  for_each = toset(local.storage_containers)
  
  name                  = each.value
  storage_account_name  = azurerm_storage_account.main.name
  container_access_type = "private"
}

# Cosmos DB Account
resource "azurerm_cosmosdb_account" "main" {
  name                = "${local.resource_prefix}-cosmos"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"
  
  consistency_policy {
    consistency_level = var.cosmosdb_consistency_level
  }
  
  geo_location {
    location          = azurerm_resource_group.main.location
    failover_priority = 0
  }
  
  capabilities {
    name = "EnableServerless"
  }
  
  tags = local.common_tags
}

# Cosmos DB Database
resource "azurerm_cosmosdb_sql_database" "main" {
  name                = "voicecode"
  resource_group_name = azurerm_resource_group.main.name
  account_name        = azurerm_cosmosdb_account.main.name
}

# Cosmos DB Containers
resource "azurerm_cosmosdb_sql_container" "containers" {
  for_each = { for container in local.cosmos_containers : container.name => container }
  
  name                = each.value.name
  resource_group_name = azurerm_resource_group.main.name
  account_name        = azurerm_cosmosdb_account.main.name
  database_name       = azurerm_cosmosdb_sql_database.main.name
  partition_key_path  = each.value.partition_key_path
}

# Azure Cognitive Services (Speech)
resource "azurerm_cognitive_account" "speech" {
  name                = "${local.resource_prefix}-speech"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  kind                = "SpeechServices"
  sku_name            = var.speech_service_sku
  
  tags = local.common_tags
}

# Container Registry
resource "azurerm_container_registry" "main" {
  name                = "${var.project_name}${var.environment}acr"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = var.acr_sku
  admin_enabled       = false
  
  tags = local.common_tags
}

# Static Site for React Frontend (commented out for initial deployment)
# resource "azurerm_static_site" "main" {
#   name                = "${local.resource_prefix}-swa"
#   resource_group_name = azurerm_resource_group.main.name
#   location            = "eastus2" # Limited regions for Static Sites
#   sku_tier            = var.environment == "prod" ? "Standard" : "Free"
#   sku_size            = var.environment == "prod" ? "Standard" : "Free"
#   
#   tags = local.common_tags
# }