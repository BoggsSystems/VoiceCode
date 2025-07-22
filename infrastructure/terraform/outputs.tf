output "resource_group_name" {
  description = "Name of the resource group"
  value       = azurerm_resource_group.main.name
}

output "key_vault_name" {
  description = "Name of the Key Vault"
  value       = azurerm_key_vault.main.name
}

output "key_vault_uri" {
  description = "URI of the Key Vault"
  value       = azurerm_key_vault.main.vault_uri
}

output "app_service_urls" {
  description = "URLs of the deployed app services"
  value = {
    for k, v in azurerm_linux_web_app.services : k => "https://${v.default_hostname}"
  }
}

output "static_web_app_url" {
  description = "URL of the static web app"
  value       = "https://${azurerm_static_web_app.main.default_hostname}"
}

output "api_management_gateway_url" {
  description = "API Management gateway URL"
  value       = azurerm_api_management.main.gateway_url
}

output "api_management_portal_url" {
  description = "API Management developer portal URL"
  value       = azurerm_api_management.main.developer_portal_url
}

output "service_bus_namespace" {
  description = "Service Bus namespace name"
  value       = azurerm_servicebus_namespace.main.name
}

output "redis_hostname" {
  description = "Redis cache hostname"
  value       = azurerm_redis_cache.main.hostname
  sensitive   = true
}

output "cosmos_endpoint" {
  description = "Cosmos DB endpoint"
  value       = azurerm_cosmosdb_account.main.endpoint
}

output "storage_account_name" {
  description = "Storage account name"
  value       = azurerm_storage_account.main.name
}

output "container_registry_url" {
  description = "Container Registry login server"
  value       = azurerm_container_registry.main.login_server
}

output "application_insights_instrumentation_key" {
  description = "Application Insights instrumentation key"
  value       = azurerm_application_insights.main.instrumentation_key
  sensitive   = true
}

output "application_insights_connection_string" {
  description = "Application Insights connection string"
  value       = azurerm_application_insights.main.connection_string
  sensitive   = true
}

output "speech_service_endpoint" {
  description = "Speech Services endpoint"
  value       = azurerm_cognitive_account.speech.endpoint
}

output "azure_ad_app_ids" {
  description = "Azure AD application IDs"
  value = {
    webapp = azuread_application.webapp.application_id
    services = {
      for k, v in azuread_application.services : k => v.application_id
    }
  }
}

output "managed_identity_ids" {
  description = "Managed identity IDs for services"
  value = {
    for k, v in azurerm_user_assigned_identity.services : k => {
      client_id    = v.client_id
      principal_id = v.principal_id
      resource_id  = v.id
    }
  }
}

output "deployment_instructions" {
  description = "Next steps for deployment"
  value = <<-EOT
    Deployment completed! Next steps:
    
    1. Configure Azure AD B2C:
       - Create user flows for sign-up/sign-in
       - Configure the web app with B2C settings
    
    2. Deploy services:
       - Build and push Docker images to ${azurerm_container_registry.main.login_server}
       - Deploy to App Services using CI/CD pipelines
    
    3. Configure Static Web App:
       - Set environment variables for API endpoints
       - Deploy React application
    
    4. Configure custom domain (if applicable):
       - Add custom domain to API Management
       - Configure SSL certificates
    
    5. Test the deployment:
       - Access the web app at ${azurerm_static_web_app.main.default_hostname}
       - Test API endpoints through ${azurerm_api_management.main.gateway_url}
  EOT
}