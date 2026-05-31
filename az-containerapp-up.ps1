# イメージを再ビルド & プッシュ
az acr build `
  --registry ca477eeb51dcacr `
  --image opscontext:latest `
  --file Dockerfile `
  .

# Container Apps に反映
az containerapp update `
  --name ca-opscontext `
  --resource-group genai-grandit `
  --image ca477eeb51dcacr.azurecr.io/opscontext:latest
