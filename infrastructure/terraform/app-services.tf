# App Service Plan
resource "azurerm_service_plan" "main" {
  name                = "${local.resource_prefix}-asp"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  os_type             = "Linux"
  sku_name            = var.service_plan_sku
  
  tags = local.common_tags
}

# Managed Identities for Services
resource "azurerm_user_assigned_identity" "services" {
  for_each = local.services
  
  name                = "${local.resource_prefix}-id-${each.value}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.common_tags
}

# App Services
resource "azurerm_linux_web_app" "services" {
  for_each = local.services
  
  name                = "${local.resource_prefix}-app-${each.value}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  service_plan_id     = azurerm_service_plan.main.id
  
  https_only = true
  
  site_config {
    always_on        = true
    linux_fx_version = "DOTNETCORE|8.0"
    
    health_check_path = "/health"
    
    app_command_line = ""
    
    cors {
      allowed_origins = var.domain_name != "" ? [
        "https://${var.domain_name}",
        "https://${azurerm_static_web_app.main.default_hostname}",
        "http://localhost:3000"
      ] : [
        "https://${azurerm_static_web_app.main.default_hostname}",
        "http://localhost:3000"
      ]
      support_credentials = true
    }
    
    application_stack {
      dotnet_version = "8.0"
    }
  }
  
  app_settings = merge(
    local.common_app_settings,
    each.key == "dispatcher" ? local.service_endpoints : {},
    {
      "AzureAd__ClientId" = azuread_application.services[each.key].application_id
      "ServiceName"       = each.value
    },
    # Service-specific settings
    each.key == "stt" || each.key == "tts" ? {
      "AzureSpeech__Key"    = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=SpeechServiceKey)"
      "AzureSpeech__Region" = azurerm_cognitive_account.speech.location
    } : {},
    each.key == "claude" ? {
      "Anthropic__ApiKey" = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=ClaudeApiKey)"
      "OpenAI__ApiKey"    = "@Microsoft.KeyVault(VaultName=${azurerm_key_vault.main.name};SecretName=OpenAIApiKey)"
    } : {}
  )
  
  identity {
    type = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.services[each.key].id]
  }
  
  tags = local.common_tags
}

# Deployment slots for production
resource "azurerm_linux_web_app_slot" "staging" {
  for_each = var.environment == "prod" ? local.services : {}
  
  name           = "staging"
  app_service_id = azurerm_linux_web_app.services[each.key].id
  
  https_only = true
  
  site_config {
    always_on        = true
    linux_fx_version = "DOTNETCORE|8.0"
    
    health_check_path = "/health"
    
    cors {
      allowed_origins = var.domain_name != "" ? [
        "https://${var.domain_name}",
        "https://${azurerm_static_web_app.main.default_hostname}",
        "http://localhost:3000"
      ] : [
        "https://${azurerm_static_web_app.main.default_hostname}",
        "http://localhost:3000"
      ]
      support_credentials = true
    }
    
    application_stack {
      dotnet_version = "8.0"
    }
  }
  
  app_settings = azurerm_linux_web_app.services[each.key].app_settings
  
  identity {
    type = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.services[each.key].id]
  }
  
  tags = local.common_tags
}

# Enable auto-scaling for production
resource "azurerm_monitor_autoscale_setting" "app_service" {
  count = var.environment == "prod" ? 1 : 0
  
  name                = "${local.resource_prefix}-asp-autoscale"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  target_resource_id  = azurerm_service_plan.main.id
  
  profile {
    name = "default"
    
    capacity {
      default = var.service_plan_capacity
      minimum = 2
      maximum = 10
    }
    
    rule {
      metric_trigger {
        metric_name        = "CpuPercentage"
        metric_resource_id = azurerm_service_plan.main.id
        time_grain         = "PT1M"
        statistic          = "Average"
        time_window        = "PT5M"
        time_aggregation   = "Average"
        operator           = "GreaterThan"
        threshold          = 75
      }
      
      scale_action {
        direction = "Increase"
        type      = "ChangeCount"
        value     = "1"
        cooldown  = "PT5M"
      }
    }
    
    rule {
      metric_trigger {
        metric_name        = "CpuPercentage"
        metric_resource_id = azurerm_service_plan.main.id
        time_grain         = "PT1M"
        statistic          = "Average"
        time_window        = "PT5M"
        time_aggregation   = "Average"
        operator           = "LessThan"
        threshold          = 25
      }
      
      scale_action {
        direction = "Decrease"
        type      = "ChangeCount"
        value     = "1"
        cooldown  = "PT5M"
      }
    }
    
    rule {
      metric_trigger {
        metric_name        = "MemoryPercentage"
        metric_resource_id = azurerm_service_plan.main.id
        time_grain         = "PT1M"
        statistic          = "Average"
        time_window        = "PT5M"
        time_aggregation   = "Average"
        operator           = "GreaterThan"
        threshold          = 80
      }
      
      scale_action {
        direction = "Increase"
        type      = "ChangeCount"
        value     = "1"
        cooldown  = "PT5M"
      }
    }
  }
  
  notification {
    email {
      send_to_subscription_administrator    = true
      send_to_subscription_co_administrators = true
      custom_emails                          = [var.admin_email]
    }
  }
}