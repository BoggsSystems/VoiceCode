provider "azurerm" {
  skip_provider_registration = true
  
  features {
    key_vault {
      purge_soft_delete_on_destroy    = var.environment != "prod"
      recover_soft_deleted_key_vaults = true
    }
    
    app_configuration {
      purge_soft_delete_on_destroy = var.environment != "prod"
      recover_soft_deleted         = true
    }
  }
}

provider "azuread" {
  # Configuration options
}

provider "random" {
  # Configuration options
}