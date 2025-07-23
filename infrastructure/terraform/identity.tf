# Azure AD Applications for Services
resource "azuread_application" "services" {
  for_each = local.services
  
  display_name = "${var.project_name}-${var.environment}-${each.value}"
  
  api {
    requested_access_token_version = 2
    
    oauth2_permission_scope {
      admin_consent_description  = "Allow the application to access ${each.value} on behalf of the signed-in user."
      admin_consent_display_name = "Access ${each.value}"
      enabled                    = true
      id                         = random_uuid.oauth_scope_id[each.key].result
      type                       = "User"
      user_consent_description   = "Allow the application to access ${each.value} on your behalf."
      user_consent_display_name = "Access ${each.value}"
      value                      = "access_as_user"
    }
  }
  
  required_resource_access {
    resource_app_id = "00000003-0000-0000-c000-000000000000" # Microsoft Graph
    
    resource_access {
      id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read
      type = "Scope"
    }
  }
  
  web {
    homepage_url  = "https://${local.resource_prefix}-app-${each.value}.azurewebsites.net"
    redirect_uris = [
      "https://${local.resource_prefix}-app-${each.value}.azurewebsites.net/signin-oidc",
      "https://${local.resource_prefix}-app-${each.value}.azurewebsites.net/.auth/login/aad/callback"
    ]
    
    implicit_grant {
      access_token_issuance_enabled = false
      id_token_issuance_enabled     = true
    }
  }
}

# Service Principals for Services
resource "azuread_service_principal" "services" {
  for_each = local.services
  
  application_id               = azuread_application.services[each.key].application_id
  app_role_assignment_required = false
}

# Random UUIDs for OAuth scopes
resource "random_uuid" "oauth_scope_id" {
  for_each = local.services
}

# Azure AD Application for Static Web App (React Frontend)
resource "azuread_application" "webapp" {
  display_name = "${var.project_name}-${var.environment}-webapp"
  
  single_page_application {
    redirect_uris = var.domain_name != "" ? [
      "https://${var.domain_name}/authentication/login-callback",
      "https://placeholder-frontend.example.com/authentication/login-callback",
      "http://localhost:3000/authentication/login-callback"
    ] : [
      "https://placeholder-frontend.example.com/authentication/login-callback",
      "http://localhost:3000/authentication/login-callback"
    ]
  }
  
  required_resource_access {
    resource_app_id = "00000003-0000-0000-c000-000000000000" # Microsoft Graph
    
    resource_access {
      id   = "e1fe6dd8-ba31-4d61-89e7-88639da4683d" # User.Read
      type = "Scope"
    }
  }
  
  # Access to backend services
  dynamic "required_resource_access" {
    for_each = local.services
    
    content {
      resource_app_id = azuread_application.services[required_resource_access.key].application_id
      
      resource_access {
        id   = random_uuid.oauth_scope_id[required_resource_access.key].result
        type = "Scope"
      }
    }
  }
}

# Service Principal for Web App
resource "azuread_service_principal" "webapp" {
  application_id               = azuread_application.webapp.application_id
  app_role_assignment_required = false
}

# Grant admin consent for API permissions
resource "azuread_app_role_assignment" "admin_consent" {
  for_each = local.services
  
  app_role_id         = "00000000-0000-0000-0000-000000000000" # Default access
  principal_object_id = azuread_service_principal.services[each.key].object_id
  resource_object_id  = azuread_service_principal.services[each.key].object_id
}

# Key Vault Access Policies for Service Identities
resource "azurerm_key_vault_access_policy" "services" {
  for_each = local.services
  
  key_vault_id = azurerm_key_vault.main.id
  tenant_id    = data.azurerm_client_config.current.tenant_id
  object_id    = azurerm_user_assigned_identity.services[each.key].principal_id
  
  secret_permissions = ["Get", "List"]
}

# Role assignments for services to access resources
resource "azurerm_role_assignment" "service_bus_sender" {
  for_each = local.services
  
  scope                = azurerm_servicebus_namespace.main.id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = azurerm_user_assigned_identity.services[each.key].principal_id
}

resource "azurerm_role_assignment" "service_bus_receiver" {
  for_each = local.services
  
  scope                = azurerm_servicebus_namespace.main.id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azurerm_user_assigned_identity.services[each.key].principal_id
}

resource "azurerm_role_assignment" "storage_blob_contributor" {
  for_each = local.services
  
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.services[each.key].principal_id
}

resource "azurerm_role_assignment" "cosmos_contributor" {
  for_each = local.services
  
  scope                = azurerm_cosmosdb_account.main.id
  role_definition_name = "Cosmos DB Account Reader Role"
  principal_id         = azurerm_user_assigned_identity.services[each.key].principal_id
}