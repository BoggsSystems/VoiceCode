# Azure Container Instances for VoiceCode Build Environments

# Container Instance for on-demand builds
resource "azurerm_container_group" "build_instance" {
  name                = "voicecode-build-instance-${var.environment}"
  location           = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  ip_address_type    = "None"
  os_type            = "Linux"
  restart_policy     = "Never"
  
  # Main build coordinator container
  container {
    name   = "build-coordinator"
    image  = "${azurerm_container_registry.build_registry.login_server}/voicecode-build-coordinator:latest"
    cpu    = "2"
    memory = "4"
    
    ports {
      port     = 8080
      protocol = "TCP"
    }
    
    environment_variables = {
      AZURE_SUBSCRIPTION_ID = data.azurerm_client_config.current.subscription_id
      RESOURCE_GROUP_NAME   = data.azurerm_resource_group.main.name
      REGISTRY_NAME         = azurerm_container_registry.build_registry.name
      STORAGE_ACCOUNT_NAME  = azurerm_storage_account.build_storage.name
      BUILD_TIMEOUT_MINUTES = "30"
      MAX_CONCURRENT_BUILDS = "5"
      ENVIRONMENT          = var.environment
    }
    
    secure_environment_variables = {
      AZURE_CLIENT_ID     = azurerm_user_assigned_identity.build_identity.client_id
      AZURE_CLIENT_SECRET = random_password.build_secret.result
      AZURE_TENANT_ID     = data.azurerm_client_config.current.tenant_id
      GITHUB_TOKEN        = var.github_token
      REGISTRY_USERNAME   = azurerm_container_registry.build_registry.admin_username
      REGISTRY_PASSWORD   = azurerm_container_registry.build_registry.admin_password
    }
    
    volume {
      name                 = "build-cache"
      mount_path          = "/cache"
      storage_account_name = azurerm_storage_account.build_storage.name
      storage_account_key  = azurerm_storage_account.build_storage.primary_access_key
      share_name          = azurerm_storage_share.build_cache.name
    }
    
    volume {
      name       = "docker-socket"
      mount_path = "/var/run/docker.sock"
      empty_dir  = true
    }
  }
  
  # Docker-in-Docker sidecar for building containers
  container {
    name   = "docker-dind"
    image  = "docker:24-dind"
    cpu    = "1"
    memory = "2"
    
    environment_variables = {
      DOCKER_TLS_CERTDIR = ""
    }
    
    volume {
      name       = "docker-socket"
      mount_path = "/var/run/docker.sock"
      empty_dir  = true
    }
  }
  
  # Resource limits
  diagnostics {
    log_analytics {
      workspace_id  = azurerm_log_analytics_workspace.build_workspace.workspace_id
      workspace_key = azurerm_log_analytics_workspace.build_workspace.primary_shared_key
    }
  }
  
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.build_identity.id]
  }
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
    Type        = "Coordinator"
  }
}

# Storage share for build cache
resource "azurerm_storage_share" "build_cache" {
  name                 = "build-cache"
  storage_account_name = azurerm_storage_account.build_storage.name
  quota                = 100 # GB
  
  metadata = {
    purpose = "build-cache"
  }
}

# Log Analytics workspace for monitoring
resource "azurerm_log_analytics_workspace" "build_workspace" {
  name                = "voicecode-build-logs-${var.environment}"
  location           = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  sku                = "PerGB2018"
  retention_in_days   = 30
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

# Container Instance Template for .NET builds
resource "azurerm_container_group" "dotnet_build_template" {
  name                = "voicecode-dotnet-template-${var.environment}"
  location           = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  ip_address_type    = "None"
  os_type            = "Linux"
  restart_policy     = "Never"
  
  container {
    name   = "dotnet-builder"
    image  = "${azurerm_container_registry.build_registry.login_server}/voicecode-build-dotnet:latest"
    cpu    = "2"
    memory = "4"
    
    environment_variables = {
      BUILD_TYPE           = "dotnet"
      DOTNET_CLI_TELEMETRY_OPTOUT = "1"
      DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
      NUGET_PACKAGES      = "/cache/nuget"
    }
    
    volume {
      name                 = "nuget-cache"
      mount_path          = "/cache/nuget"
      storage_account_name = azurerm_storage_account.build_storage.name
      storage_account_key  = azurerm_storage_account.build_storage.primary_access_key
      share_name          = azurerm_storage_share.nuget_cache.name
    }
  }
  
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.build_identity.id]
  }
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
    Type        = "DotNetTemplate"
  }
}

# Container Instance Template for Node.js builds
resource "azurerm_container_group" "nodejs_build_template" {
  name                = "voicecode-nodejs-template-${var.environment}"
  location           = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  ip_address_type    = "None"
  os_type            = "Linux"
  restart_policy     = "Never"
  
  container {
    name   = "nodejs-builder"
    image  = "${azurerm_container_registry.build_registry.login_server}/voicecode-build-nodejs:latest"
    cpu    = "2"
    memory = "4"
    
    environment_variables = {
      BUILD_TYPE               = "nodejs"
      NODE_ENV                = "production"
      NPM_CONFIG_CACHE        = "/cache/npm"
      YARN_CACHE_FOLDER       = "/cache/yarn"
      NPM_CONFIG_AUDIT        = "false"
      NPM_CONFIG_FUND         = "false"
    }
    
    volume {
      name                 = "npm-cache"
      mount_path          = "/cache/npm"
      storage_account_name = azurerm_storage_account.build_storage.name
      storage_account_key  = azurerm_storage_account.build_storage.primary_access_key
      share_name          = azurerm_storage_share.npm_cache.name
    }
    
    volume {
      name                 = "yarn-cache"
      mount_path          = "/cache/yarn"
      storage_account_name = azurerm_storage_account.build_storage.name
      storage_account_key  = azurerm_storage_account.build_storage.primary_access_key
      share_name          = azurerm_storage_share.yarn_cache.name
    }
  }
  
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.build_identity.id]
  }
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
    Type        = "NodeJSTemplate"
  }
}

# Storage shares for package caches
resource "azurerm_storage_share" "nuget_cache" {
  name                 = "nuget-cache"
  storage_account_name = azurerm_storage_account.build_storage.name
  quota                = 50 # GB
  
  metadata = {
    purpose = "nuget-package-cache"
  }
}

resource "azurerm_storage_share" "npm_cache" {
  name                 = "npm-cache"
  storage_account_name = azurerm_storage_account.build_storage.name
  quota                = 50 # GB
  
  metadata = {
    purpose = "npm-package-cache"
  }
}

resource "azurerm_storage_share" "yarn_cache" {
  name                 = "yarn-cache"
  storage_account_name = azurerm_storage_account.build_storage.name
  quota                = 25 # GB
  
  metadata = {
    purpose = "yarn-package-cache"
  }
}

# Auto-scaling Container Instance Group
resource "azurerm_container_group" "build_pool" {
  count = 3 # Create a pool of 3 build instances
  
  name                = "voicecode-build-pool-${count.index}-${var.environment}"
  location           = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  ip_address_type    = "None"
  os_type            = "Linux"
  restart_policy     = "Never"
  
  container {
    name   = "build-agent"
    image  = "${azurerm_container_registry.build_registry.login_server}/voicecode-build-base:latest"
    cpu    = "1"
    memory = "2"
    
    environment_variables = {
      AGENT_ID            = "agent-${count.index}"
      POOL_SIZE           = "3"
      BUILD_QUEUE_NAME    = "build-requests"
      HEARTBEAT_INTERVAL  = "30"
    }
    
    secure_environment_variables = {
      AZURE_CLIENT_ID     = azurerm_user_assigned_identity.build_identity.client_id
      AZURE_CLIENT_SECRET = random_password.build_secret.result
      AZURE_TENANT_ID     = data.azurerm_client_config.current.tenant_id
    }
  }
  
  diagnostics {
    log_analytics {
      workspace_id  = azurerm_log_analytics_workspace.build_workspace.workspace_id
      workspace_key = azurerm_log_analytics_workspace.build_workspace.primary_shared_key
    }
  }
  
  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.build_identity.id]
  }
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
    Type        = "BuildPool"
    AgentId     = "agent-${count.index}"
  }
}

# Azure Service Bus for build queue
resource "azurerm_servicebus_namespace" "build_queue" {
  name                = "voicecode-builds-${var.environment}"
  location           = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  sku                = "Standard"
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

resource "azurerm_servicebus_queue" "build_requests" {
  name         = "build-requests"
  namespace_id = azurerm_servicebus_namespace.build_queue.id
  
  partitioning_enabled = true
  max_size_in_megabytes = 1024
  default_message_ttl = "PT30M" # 30 minutes
  
  dead_lettering_on_message_expiration = true
  max_delivery_count = 3
}

resource "azurerm_servicebus_queue" "build_results" {
  name         = "build-results"
  namespace_id = azurerm_servicebus_namespace.build_queue.id
  
  partitioning_enabled = true
  max_size_in_megabytes = 1024
  default_message_ttl = "PT1H" # 1 hour
}

# Service Bus access policies
resource "azurerm_servicebus_namespace_authorization_rule" "build_access" {
  name         = "BuildAccess"
  namespace_id = azurerm_servicebus_namespace.build_queue.id
  
  listen = true
  send   = true
  manage = false
}

# Azure Monitor alerts
resource "azurerm_monitor_metric_alert" "build_queue_length" {
  name                = "build-queue-length-alert-${var.environment}"
  resource_group_name = data.azurerm_resource_group.main.name
  scopes              = [azurerm_servicebus_queue.build_requests.id]
  description         = "Alert when build queue length is high"
  
  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "ActiveMessages"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 10
    
    dimension {
      name     = "EntityName"
      operator = "Include"
      values   = [azurerm_servicebus_queue.build_requests.name]
    }
  }
  
  action {
    action_group_id = azurerm_monitor_action_group.build_alerts.id
  }
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

resource "azurerm_monitor_action_group" "build_alerts" {
  name                = "build-alerts-${var.environment}"
  resource_group_name = data.azurerm_resource_group.main.name
  short_name          = "builds"
  
  webhook_receiver {
    name        = "build-webhook"
    service_uri = "https://api.voicecode.example.com/alerts/builds"
  }
  
  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

# Outputs for container instances
output "build_coordinator_fqdn" {
  value       = azurerm_container_group.build_instance.fqdn
  description = "FQDN of the build coordinator instance"
}

output "service_bus_connection_string" {
  value       = azurerm_servicebus_namespace_authorization_rule.build_access.primary_connection_string
  description = "Service Bus connection string for build queue"
  sensitive   = true
}

output "log_analytics_workspace_id" {
  value       = azurerm_log_analytics_workspace.build_workspace.workspace_id
  description = "Log Analytics workspace ID for build monitoring"
}

output "build_agent_pool_names" {
  value       = azurerm_container_group.build_pool[*].name
  description = "Names of build agent pool instances"
}