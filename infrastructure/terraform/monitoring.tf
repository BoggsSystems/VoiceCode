# Monitoring and Alerting Configuration for VoiceCode

# Action Group for alerts
resource "azurerm_monitor_action_group" "main" {
  name                = "${local.resource_prefix}-alerts"
  resource_group_name = azurerm_resource_group.main.name
  short_name          = "voicecode"

  email_receiver {
    name          = "admin-email"
    email_address = var.admin_email
  }

  tags = local.common_tags
}

# Application Insights Smart Detection
resource "azurerm_application_insights_smart_detection_rule" "failure_anomalies" {
  name                    = "Abnormal rise in exception volume"
  application_insights_id = azurerm_application_insights.main.id
  enabled                 = true
}

# Redis Memory Usage Alert
resource "azurerm_monitor_metric_alert" "redis_memory" {
  name                = "${local.resource_prefix}-redis-memory-alert"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_redis_cache.main.id]
  description         = "Alert when Redis memory usage is high"
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"

  criteria {
    metric_namespace = "Microsoft.Cache/redis"
    metric_name      = "usedmemorypercentage"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 80
  }

  action {
    action_group_id = azurerm_monitor_action_group.main.id
  }

  tags = local.common_tags
}

# Cosmos DB RU Consumption Alert
resource "azurerm_monitor_metric_alert" "cosmos_ru_consumption" {
  name                = "${local.resource_prefix}-cosmos-ru-alert"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_cosmosdb_account.main.id]
  description         = "Alert when Cosmos DB RU consumption is high"
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"

  criteria {
    metric_namespace = "Microsoft.DocumentDB/databaseAccounts"
    metric_name      = "TotalRequestUnits"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = 1000
  }

  action {
    action_group_id = azurerm_monitor_action_group.main.id
  }

  tags = local.common_tags
}

# Storage Account Availability Alert
resource "azurerm_monitor_metric_alert" "storage_availability" {
  name                = "${local.resource_prefix}-storage-availability-alert"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_storage_account.main.id]
  description         = "Alert when storage account availability is low"
  severity            = 1
  frequency           = "PT1M"
  window_size         = "PT5M"

  criteria {
    metric_namespace = "Microsoft.Storage/storageAccounts"
    metric_name      = "Availability"
    aggregation      = "Average"
    operator         = "LessThan"
    threshold        = 99
  }

  action {
    action_group_id = azurerm_monitor_action_group.main.id
  }

  tags = local.common_tags
}