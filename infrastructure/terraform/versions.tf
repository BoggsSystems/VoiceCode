terraform {
  required_version = ">= 1.5.0"
  
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.85.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.47.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6.0"
    }
  }
  
  backend "azurerm" {
    resource_group_name  = "voicecode-terraform-state"
    storage_account_name = "voicecodeterraform"
    container_name       = "tfstate"
    key                  = "voicecode.tfstate"
  }
}