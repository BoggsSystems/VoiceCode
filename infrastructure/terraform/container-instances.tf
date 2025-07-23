# Azure Container Instances for VoiceCode Services
# Alternative to App Service Plan due to quota limitations

# Managed Identities for Services
resource "azurerm_user_assigned_identity" "services" {
  for_each = local.services
  
  name                = "${local.resource_prefix}-id-${each.value}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  
  tags = local.common_tags
}

# Virtual Network for Container Instances
resource "azurerm_virtual_network" "container_vnet" {
  name                = "${local.resource_prefix}-vnet"
  address_space       = ["10.0.0.0/16"]
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  
  tags = local.common_tags
}

resource "azurerm_subnet" "container_subnet" {
  name                 = "container-subnet"
  resource_group_name  = azurerm_resource_group.main.name
  virtual_network_name = azurerm_virtual_network.container_vnet.name
  address_prefixes     = ["10.0.1.0/24"]
  
  delegation {
    name = "delegation"
    service_delegation {
      name    = "Microsoft.ContainerInstance/containerGroups"
      actions = ["Microsoft.Network/virtualNetworks/subnets/action"]
    }
  }
}

# Network Security Group for container subnet
resource "azurerm_network_security_group" "container_nsg" {
  name                = "${local.resource_prefix}-container-nsg"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  security_rule {
    name                       = "AllowHTTPS"
    priority                   = 1001
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }

  security_rule {
    name                       = "AllowHTTP"
    priority                   = 1002
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "80"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }

  tags = local.common_tags
}

resource "azurerm_subnet_network_security_group_association" "container_nsg_association" {
  subnet_id                 = azurerm_subnet.container_subnet.id
  network_security_group_id = azurerm_network_security_group.container_nsg.id
}

# Container Groups for each service
resource "azurerm_container_group" "services" {
  for_each = local.services

  name                = "${local.resource_prefix}-${each.key}-ci"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  ip_address_type     = "Public"
  dns_name_label      = "${local.resource_prefix}-${each.key}"
  os_type             = "Linux"
  restart_policy      = "Always"
  # subnet_ids          = [azurerm_subnet.container_subnet.id] # Can't use with dns_name_label

  container {
    name   = each.key
    image  = "voicecodebuildsprod.azurecr.io/voicecode/${each.key}-service:v3"
    cpu    = each.key == "claude" || each.key == "generator" ? "1.0" : "0.5"
    memory = each.key == "claude" || each.key == "generator" ? "2.0" : "1.0"

    ports {
      port     = 80
      protocol = "TCP"
    }

    ports {
      port     = 443
      protocol = "TCP"
    }

    # Environment variables from app settings
    environment_variables = merge(
      {
        "ASPNETCORE_ENVIRONMENT"                = "Production"
        "ASPNETCORE_URLS"                       = "http://+:80"
        "SERVICE_NAME"                          = each.key
        "CONTAINER_MODE"                        = "true"
        "KeyVaultName"                          = azurerm_key_vault.main.name
        "AzureAd__TenantId"                     = data.azurerm_client_config.current.tenant_id
        "AzureAd__Instance"                     = "https://login.microsoftonline.com/"
        "Cors__AllowedOrigins__0"               = var.domain_name != "" ? "https://${var.domain_name}" : "https://placeholder-frontend.example.com"
        "Cors__AllowedOrigins__1"               = "http://localhost:3000"
      },
      each.key == "dispatcher" ? local.service_endpoints : {}
    )

    # Secure environment variables (secrets)
    secure_environment_variables = {
      "ApplicationInsights__ConnectionString" = azurerm_application_insights.main.connection_string
      "ConnectionStrings__ServiceBus"         = azurerm_servicebus_namespace.main.default_primary_connection_string
      "ConnectionStrings__Redis"              = azurerm_redis_cache.main.primary_connection_string
      "ConnectionStrings__Storage"            = azurerm_storage_account.main.primary_connection_string
      "ConnectionStrings__CosmosDb"           = azurerm_cosmosdb_account.main.primary_sql_connection_string
      "AzureSpeech__Key"                      = azurerm_cognitive_account.speech.primary_access_key
      # Add Claude API key reference here when available
    }

    # Health probe
    liveness_probe {
      http_get {
        path   = "/health"
        port   = 80
        scheme = "Http"
      }
      initial_delay_seconds = 30
      period_seconds        = 30
      timeout_seconds       = 5
      failure_threshold     = 3
    }

    readiness_probe {
      http_get {
        path   = "/health/ready"
        port   = 80
        scheme = "Http"
      }
      initial_delay_seconds = 15
      period_seconds        = 10
      timeout_seconds       = 3
      failure_threshold     = 3
    }
  }

  # Configure diagnostics
  diagnostics {
    log_analytics {
      workspace_id  = azurerm_log_analytics_workspace.main.workspace_id
      workspace_key = azurerm_log_analytics_workspace.main.primary_shared_key
    }
  }

  # Use managed identity
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.services[each.key].id]
  }

  # Registry credentials
  image_registry_credential {
    server   = "voicecodebuildsprod.azurecr.io"
    username = data.azurerm_container_registry.existing.admin_username
    password = data.azurerm_container_registry.existing.admin_password
  }

  # Depends on Redis being ready
  depends_on = [azurerm_redis_cache.main]

  tags = merge(local.common_tags, {
    Service = each.key
    Type    = "ContainerInstance"
  })
}

# Container Registry with admin enabled for Container Instances
resource "azurerm_role_assignment" "acr_pull" {
  for_each = local.services

  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.services[each.key].principal_id
}

# Load Balancer for high availability (optional)
resource "azurerm_public_ip" "lb_public_ip" {
  name                = "${local.resource_prefix}-lb-ip"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  allocation_method   = "Static"
  sku                 = "Standard"
  
  tags = local.common_tags
}

resource "azurerm_lb" "main" {
  name                = "${local.resource_prefix}-lb"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Standard"

  frontend_ip_configuration {
    name                 = "primary"
    public_ip_address_id = azurerm_public_ip.lb_public_ip.id
  }

  tags = local.common_tags
}

# Backend pools for each service
resource "azurerm_lb_backend_address_pool" "services" {
  for_each = local.services

  loadbalancer_id = azurerm_lb.main.id
  name            = "${each.key}-pool"
}

# Health probes
resource "azurerm_lb_probe" "services" {
  for_each = local.services

  loadbalancer_id = azurerm_lb.main.id
  name            = "${each.key}-health"
  port            = 80
  protocol        = "Http"
  request_path    = "/health"
}

# Load balancing rules
resource "azurerm_lb_rule" "services" {
  for_each = local.services

  loadbalancer_id                = azurerm_lb.main.id
  name                           = "${each.key}-rule"
  protocol                       = "Tcp"
  frontend_port                  = 8000 + index(keys(local.services), each.key)
  backend_port                   = 80
  frontend_ip_configuration_name = "primary"
  backend_address_pool_ids       = [azurerm_lb_backend_address_pool.services[each.key].id]
  probe_id                       = azurerm_lb_probe.services[each.key].id
}

# Auto-scaling with Azure Monitor (Container Instances don't support auto-scaling directly)
# We'll implement this with Azure Logic Apps or Functions to manage instance count

# Monitoring alerts for Container Instances
resource "azurerm_monitor_metric_alert" "container_cpu" {
  for_each = local.services

  name                = "${local.resource_prefix}-${each.key}-cpu-alert"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_container_group.services[each.key].id]
  description         = "Alert when CPU usage is high for ${each.key} container"
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"

  criteria {
    metric_namespace = "Microsoft.ContainerInstance/containerGroups"
    metric_name      = "CpuUsage"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 80
  }

  action {
    action_group_id = azurerm_monitor_action_group.main.id
  }

  tags = local.common_tags
}

resource "azurerm_monitor_metric_alert" "container_memory" {
  for_each = local.services

  name                = "${local.resource_prefix}-${each.key}-memory-alert"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_container_group.services[each.key].id]
  description         = "Alert when memory usage is high for ${each.key} container"
  severity            = 2
  frequency           = "PT1M"
  window_size         = "PT5M"

  criteria {
    metric_namespace = "Microsoft.ContainerInstance/containerGroups"
    metric_name      = "MemoryUsage"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 1500000000 # 1.5GB in bytes
  }

  action {
    action_group_id = azurerm_monitor_action_group.main.id
  }

  tags = local.common_tags
}