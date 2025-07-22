# VoiceCode API Management Configuration

This directory contains configuration files for Azure API Management (APIM) which serves as the API gateway for all VoiceCode services.

## Architecture

```
Internet → API Management → Backend Services
                ↓
          - Rate Limiting
          - Authentication
          - CORS
          - Caching
          - Monitoring
```

## Directory Structure

```
api-management/
├── policies/              # API policies (rate limiting, auth, etc.)
├── products/             # API product definitions
├── subscriptions/        # Subscription tier configurations
├── openapi/              # OpenAPI specifications
├── templates/            # Email and notification templates
└── terraform/            # Additional Terraform config
```

## Features

### 1. Global Policies
- **Authentication**: OAuth 2.0 with Azure AD
- **Rate Limiting**: Subscription-based limits
- **CORS**: Configured for web app origins
- **Security Headers**: HSTS, CSP, X-Frame-Options
- **Request Correlation**: X-Correlation-ID tracking

### 2. Service-Specific Policies

#### STT Service
- Audio content type validation
- 10MB file size limit
- Lower rate limits for audio processing
- Response caching for repeated requests

#### Claude Service
- Token limit validation
- Extended timeouts (120s)
- Lower rate limits due to API costs
- Non-personalized response caching

#### Dispatcher Service
- WebSocket support for SignalR
- Extended timeout for long-polling
- Negotiation endpoint caching

### 3. Subscription Tiers

| Tier | Calls/Day | Audio Min/Day | Claude Req/Day | Price |
|------|-----------|---------------|----------------|-------|
| Developer | 10,000 | 60 | 100 | Free |
| Starter | 50,000 | 300 | 500 | $99/mo |
| Professional | 200,000 | 1,200 | 2,000 | $499/mo |
| Enterprise | 1,000,000 | 6,000 | 10,000 | Custom |

### 4. API Versioning
- Header-based versioning: `X-API-Version`
- Current version: v1
- Version set: `/v1/*` paths

## Deployment

### Via Terraform
The main Terraform configuration in `/infrastructure/terraform` includes basic APIM setup. Additional configuration:

```bash
cd infrastructure/terraform
terraform apply -var-file="api-management-config.tfvars"
```

### Manual Configuration

1. **Import OpenAPI Spec**:
```bash
az apim api import \
  --path "v1" \
  --api-id "voicecode-api-v1" \
  --resource-group "voicecode-prod-rg" \
  --service-name "voicecode-prod-apim" \
  --specification-path "./openapi/voicecode-api.yaml" \
  --specification-format "OpenApiJson"
```

2. **Apply Policies**:
```bash
# Global policy
az apim api policy create \
  --resource-group "voicecode-prod-rg" \
  --service-name "voicecode-prod-apim" \
  --policy-file "./policies/global-policy.xml"

# Service-specific policies
az apim api policy create \
  --resource-group "voicecode-prod-rg" \
  --service-name "voicecode-prod-apim" \
  --api-id "stt-api" \
  --policy-file "./policies/stt-api-policy.xml"
```

3. **Create Products**:
```bash
az apim product create \
  --resource-group "voicecode-prod-rg" \
  --service-name "voicecode-prod-apim" \
  --product-id "voicecode" \
  --product-name "VoiceCode API" \
  --description "Complete access to VoiceCode services" \
  --subscription-required true \
  --approval-required true \
  --state "published"
```

## Testing

### 1. Test Authentication
```bash
# Get access token
ACCESS_TOKEN=$(az account get-access-token \
  --resource "api://voicecode/access_as_user" \
  --query accessToken -o tsv)

# Test API call
curl -X GET https://voicecode-prod-apim.azure-api.net/v1/stt/health \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "X-API-Version: 1.0"
```

### 2. Test Rate Limiting
```bash
# Test with subscription key
for i in {1..10}; do
  curl -X GET https://voicecode-prod-apim.azure-api.net/v1/router/health \
    -H "Ocp-Apim-Subscription-Key: $SUBSCRIPTION_KEY" \
    -H "X-API-Version: 1.0" \
    -w "\nStatus: %{http_code}, Time: %{time_total}s\n"
done
```

### 3. Test CORS
```javascript
// From browser console
fetch('https://voicecode-prod-apim.azure-api.net/v1/claude/health', {
  method: 'GET',
  headers: {
    'Authorization': 'Bearer ' + accessToken,
    'X-API-Version': '1.0'
  }
})
.then(response => response.json())
.then(data => console.log(data));
```

## Monitoring

### Metrics Available
- Request count by API/operation
- Response time percentiles
- Error rates by status code
- Bandwidth usage
- Cache hit ratio

### Alerts Configured
- High error rate (>5% 5xx errors)
- High latency (>5s p95)
- Rate limit violations
- Backend health failures

### Application Insights Integration
All requests are logged to Application Insights with:
- Correlation IDs
- Request/response bodies (sampled)
- Custom dimensions (API, operation, subscription)
- Performance metrics

## Security Best Practices

1. **API Keys**: 
   - Rotate subscription keys regularly
   - Use Key Vault for storage
   - Limit key scope to specific APIs

2. **OAuth Tokens**:
   - Short expiration (1 hour)
   - Scope validation
   - Audience restriction

3. **Network Security**:
   - IP whitelisting for production
   - Private endpoints for backend
   - TLS 1.2 minimum

4. **Content Security**:
   - Request size limits
   - Content type validation
   - XSS protection headers

## Troubleshooting

### Common Issues

1. **401 Unauthorized**
   - Check token expiration
   - Verify audience/scope
   - Ensure subscription is active

2. **429 Too Many Requests**
   - Check rate limit headers
   - Verify subscription tier
   - Implement exponential backoff

3. **503 Service Unavailable**
   - Check backend health
   - Verify circuit breaker state
   - Review timeout settings

### Debug Headers
Add these headers for debugging:
- `Ocp-Apim-Trace: true` (requires permission)
- `X-Debug-Mode: true` (development only)

### Useful Commands

```bash
# View API logs
az apim api operation list \
  --resource-group "voicecode-prod-rg" \
  --service-name "voicecode-prod-apim" \
  --api-id "voicecode-api-v1"

# Export API definition
az apim api export \
  --resource-group "voicecode-prod-rg" \
  --service-name "voicecode-prod-apim" \
  --api-id "voicecode-api-v1" \
  --export-format "openapi-json"

# List subscriptions
az apim subscription list \
  --resource-group "voicecode-prod-rg" \
  --service-name "voicecode-prod-apim"
```

## Custom Domain Setup

1. Create SSL certificate in Key Vault
2. Configure custom domain in APIM:
```bash
az apim update \
  --resource-group "voicecode-prod-rg" \
  --name "voicecode-prod-apim" \
  --custom-domain \
    gateway=api.voicecode.io \
    portal=developer.voicecode.io
```

3. Update DNS records:
   - `api.voicecode.io` → APIM gateway
   - `developer.voicecode.io` → APIM portal