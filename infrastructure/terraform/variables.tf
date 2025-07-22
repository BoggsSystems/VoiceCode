variable "project_name" {
  description = "Name of the project"
  type        = string
  default     = "voicecode"
}

variable "environment" {
  description = "Environment (dev, staging, prod)"
  type        = string
  default     = "dev"
  
  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod."
  }
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "eastus"
}

variable "location_short" {
  description = "Short form of Azure region"
  type        = string
  default     = "eus"
}

variable "admin_email" {
  description = "Admin email for notifications"
  type        = string
}

variable "domain_name" {
  description = "Custom domain name for the application"
  type        = string
  default     = ""
}

variable "tags" {
  description = "Common tags for all resources"
  type        = map(string)
  default = {
    ManagedBy = "Terraform"
    Project   = "VoiceCode"
  }
}

# Service Configuration
variable "service_plan_sku" {
  description = "SKU for App Service Plan"
  type        = string
  default     = "P1v3"
}

variable "service_plan_capacity" {
  description = "Number of instances for App Service Plan"
  type        = number
  default     = 2
}

# Speech Services
variable "speech_service_sku" {
  description = "SKU for Azure Speech Services"
  type        = string
  default     = "S0"
}

# Service Bus
variable "servicebus_sku" {
  description = "SKU for Service Bus"
  type        = string
  default     = "Standard"
}

# Redis
variable "redis_sku" {
  description = "SKU for Redis Cache"
  type = object({
    name     = string
    family   = string
    capacity = number
  })
  default = {
    name     = "Standard"
    family   = "C"
    capacity = 1
  }
}

# Cosmos DB
variable "cosmosdb_consistency_level" {
  description = "Cosmos DB consistency level"
  type        = string
  default     = "Session"
}

variable "cosmosdb_max_throughput" {
  description = "Maximum throughput for Cosmos DB autoscale"
  type        = number
  default     = 4000
}

# Storage
variable "storage_replication_type" {
  description = "Storage account replication type"
  type        = string
  default     = "LRS"
}

# Networking
variable "enable_private_endpoints" {
  description = "Enable private endpoints for services"
  type        = bool
  default     = false
}

variable "allowed_ip_ranges" {
  description = "IP ranges allowed to access services"
  type        = list(string)
  default     = []
}

# Monitoring
variable "log_retention_days" {
  description = "Log retention in days"
  type        = number
  default     = 30
}

variable "enable_diagnostics" {
  description = "Enable diagnostic settings"
  type        = bool
  default     = true
}

# Container Registry
variable "acr_sku" {
  description = "SKU for Container Registry"
  type        = string
  default     = "Standard"
}

# API Management
variable "apim_sku" {
  description = "SKU for API Management"
  type        = string
  default     = "Developer"
}

variable "apim_capacity" {
  description = "Capacity for API Management"
  type        = number
  default     = 1
}

# Azure AD B2C
variable "b2c_tenant_name" {
  description = "Azure AD B2C tenant name"
  type        = string
}

# Secrets (use Azure Key Vault in production)
variable "claude_api_key" {
  description = "Claude API key (store in Key Vault)"
  type        = string
  sensitive   = true
}

variable "openai_api_key" {
  description = "OpenAI API key for GPT models (store in Key Vault)"
  type        = string
  sensitive   = true
  default     = ""
}