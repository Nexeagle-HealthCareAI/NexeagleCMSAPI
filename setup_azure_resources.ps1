# Azure Resource Setup Script
# Run this script to create the necessary Azure resources for the pipeline

$resourceGroup = "rg-cms-api-dev"
$location = "eastus"
$appServicePlan = "plan-cms-api-dev"
$webApp = "cms-api-dev"
$sku = "F1" # Free tier

Write-Host "Creating Resource Group '$resourceGroup'..."
az group create --name $resourceGroup --location $location

Write-Host "Creating App Service Plan '$appServicePlan'..."
az appservice plan create --name $appServicePlan --resource-group $resourceGroup --sku $sku --is-linux

Write-Host "Creating Web App '$webApp'..."
az webapp create --name $webApp --resource-group $resourceGroup --plan $appServicePlan --runtime "DOTNETCORE|10.0"

# Note: If DOTNETCORE|10.0 is not yet available in CLI, use DOTNETCORE|9.0 or 8.0 and upgrade later via portal or pipeline config
# configuration
az webapp config set --resource-group $resourceGroup --name $webApp --linux-fx-version "DOTNETCORE|8.0" 

Write-Host "Done! Resources created."
Write-Host "URL: https://$webApp.azurewebsites.net"
