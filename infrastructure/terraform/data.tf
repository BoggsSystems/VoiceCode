# Data sources for existing resources

data "azurerm_container_registry" "existing" {
  name                = "voicecodebuildsprod"
  resource_group_name = "voicecode-rg"
}