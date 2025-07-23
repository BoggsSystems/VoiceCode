terraform {
  required_version = ">= 1.0"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
    github = {
      source  = "integrations/github"
      version = "~> 5.0"
    }
  }
}

provider "azurerm" {
  features {}
}

provider "github" {
  token = var.github_token
  owner = var.github_owner
}

# Variables
variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
  default     = "voicecode-rg"
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "East US"
}

variable "environment" {
  description = "Environment name"
  type        = string
  default     = "prod"
}

variable "github_token" {
  description = "GitHub personal access token"
  type        = string
  sensitive   = true
}

variable "github_owner" {
  description = "GitHub repository owner"
  type        = string
}

variable "github_repository" {
  description = "GitHub repository name"
  type        = string
  default     = "VoiceCode"
}

# Data sources
data "azurerm_resource_group" "main" {
  name = var.resource_group_name
}

data "azurerm_client_config" "current" {}

# Container Registry for build images
resource "azurerm_container_registry" "build_registry" {
  name                = "voicecodebuilds${var.environment}"
  resource_group_name = data.azurerm_resource_group.main.name
  location           = data.azurerm_resource_group.main.location
  sku                = "Basic"
  admin_enabled      = true

  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

# Container Instance for builds
resource "azurerm_container_group" "build_agents" {
  name                = "voicecode-build-agents-${var.environment}"
  location           = data.azurerm_resource_group.main.location
  resource_group_name = data.azurerm_resource_group.main.name
  ip_address_type    = "None"
  os_type            = "Linux"
  restart_policy     = "Never"

  container {
    name   = "build-coordinator"
    image  = "${azurerm_container_registry.build_registry.login_server}/voicecode-build-coordinator:latest"
    cpu    = "1"
    memory = "2"

    environment_variables = {
      AZURE_SUBSCRIPTION_ID = data.azurerm_client_config.current.subscription_id
      RESOURCE_GROUP_NAME   = data.azurerm_resource_group.main.name
      REGISTRY_NAME         = azurerm_container_registry.build_registry.name
    }

    secure_environment_variables = {
      AZURE_CLIENT_ID     = azurerm_user_assigned_identity.build_identity.client_id
      AZURE_CLIENT_SECRET = random_password.build_secret.result
    }
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.build_identity.id]
  }

  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

# Managed Identity for build operations
resource "azurerm_user_assigned_identity" "build_identity" {
  name                = "voicecode-build-identity-${var.environment}"
  resource_group_name = data.azurerm_resource_group.main.name
  location           = data.azurerm_resource_group.main.location

  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

# Random password for secure variables
resource "random_password" "build_secret" {
  length  = 32
  special = true
}

# Role assignments for build identity
resource "azurerm_role_assignment" "build_acr_pull" {
  scope                = azurerm_container_registry.build_registry.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.build_identity.principal_id
}

resource "azurerm_role_assignment" "build_acr_push" {
  scope                = azurerm_container_registry.build_registry.id
  role_definition_name = "AcrPush"
  principal_id         = azurerm_user_assigned_identity.build_identity.principal_id
}

resource "azurerm_role_assignment" "build_container_contributor" {
  scope                = data.azurerm_resource_group.main.id
  role_definition_name = "Container Instances Contributor"
  principal_id         = azurerm_user_assigned_identity.build_identity.principal_id
}

# Storage Account for build artifacts
resource "azurerm_storage_account" "build_storage" {
  name                     = "voicecodebuild${var.environment}"
  resource_group_name      = data.azurerm_resource_group.main.name
  location                = data.azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  
  blob_properties {
    delete_retention_policy {
      days = 7
    }
  }

  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

# Storage Container for build artifacts
resource "azurerm_storage_container" "build_artifacts" {
  name                  = "build-artifacts"
  storage_account_name  = azurerm_storage_account.build_storage.name
  container_access_type = "private"
}

# Storage Container for build cache
resource "azurerm_storage_container" "build_cache" {
  name                  = "build-cache"
  storage_account_name  = azurerm_storage_account.build_storage.name
  container_access_type = "private"
}

# Key Vault for secrets
resource "azurerm_key_vault" "build_vault" {
  name                       = "voicecode-build-kv-${var.environment}"
  location                  = data.azurerm_resource_group.main.location
  resource_group_name       = data.azurerm_resource_group.main.name
  tenant_id                 = data.azurerm_client_config.current.tenant_id
  sku_name                  = "standard"
  soft_delete_retention_days = 7

  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = azurerm_user_assigned_identity.build_identity.principal_id

    secret_permissions = [
      "Get",
      "List",
    ]
  }

  tags = {
    Environment = var.environment
    Purpose     = "CloudBuilds"
  }
}

# Store Container Registry credentials in Key Vault
resource "azurerm_key_vault_secret" "acr_username" {
  name         = "acr-username"
  value        = azurerm_container_registry.build_registry.admin_username
  key_vault_id = azurerm_key_vault.build_vault.id
}

resource "azurerm_key_vault_secret" "acr_password" {
  name         = "acr-password"
  value        = azurerm_container_registry.build_registry.admin_password
  key_vault_id = azurerm_key_vault.build_vault.id
}

# GitHub repository secrets
resource "github_actions_secret" "azure_client_id" {
  repository      = var.github_repository
  secret_name     = "AZURE_CLIENT_ID"
  plaintext_value = azurerm_user_assigned_identity.build_identity.client_id
}

resource "github_actions_secret" "azure_client_secret" {
  repository      = var.github_repository
  secret_name     = "AZURE_CLIENT_SECRET"
  plaintext_value = random_password.build_secret.result
}

resource "github_actions_secret" "azure_subscription_id" {
  repository      = var.github_repository
  secret_name     = "AZURE_SUBSCRIPTION_ID"
  plaintext_value = data.azurerm_client_config.current.subscription_id
}

resource "github_actions_secret" "azure_tenant_id" {
  repository      = var.github_repository
  secret_name     = "AZURE_TENANT_ID"
  plaintext_value = data.azurerm_client_config.current.tenant_id
}

resource "github_actions_secret" "acr_login_server" {
  repository      = var.github_repository
  secret_name     = "ACR_LOGIN_SERVER"
  plaintext_value = azurerm_container_registry.build_registry.login_server
}

resource "github_actions_secret" "build_storage_account" {
  repository      = var.github_repository
  secret_name     = "BUILD_STORAGE_ACCOUNT"
  plaintext_value = azurerm_storage_account.build_storage.name
}

# Outputs
output "container_registry_login_server" {
  value       = azurerm_container_registry.build_registry.login_server
  description = "Container registry login server"
}

output "build_storage_account_name" {
  value       = azurerm_storage_account.build_storage.name
  description = "Build storage account name"
}

output "build_identity_client_id" {
  value       = azurerm_user_assigned_identity.build_identity.client_id
  description = "Build identity client ID"
}

output "key_vault_uri" {
  value       = azurerm_key_vault.build_vault.vault_uri
  description = "Key vault URI"
}