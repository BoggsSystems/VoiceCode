# VoiceCode Security and Scalability Guide

## Security Architecture

### Overview

VoiceCode implements defense-in-depth security with multiple layers of protection across all components. This document outlines security measures, threat models, and best practices.

### Security Principles

1. **Zero Trust Architecture**: Never trust, always verify
2. **Least Privilege**: Minimal permissions for all components
3. **Defense in Depth**: Multiple security layers
4. **Data Encryption**: At rest and in transit
5. **Audit Everything**: Comprehensive logging and monitoring

## Authentication and Authorization

### 1. Identity Management

```csharp
// infrastructure/identity/IdentityConfiguration.cs
public class IdentityConfiguration
{
    public static void ConfigureAuthentication(WebApplicationBuilder builder)
    {
        // Azure AD B2C Configuration
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(options =>
            {
                builder.Configuration.Bind("AzureAdB2C", options);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
            },
            options => { builder.Configuration.Bind("AzureAdB2C", options); });

        // Authorization policies
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAuthenticatedUser", policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy("RequirePaidSubscription", policy =>
                policy.RequireClaim("subscription_tier", "Basic", "Professional", "Enterprise"));

            options.AddPolicy("RequireAdminRole", policy =>
                policy.RequireRole("Admin"));

            options.AddPolicy("ApiScope", policy =>
                policy.RequireClaim("scp", "api.access"));
        });
    }
}
```

### 2. Token Management

```csharp
// services/common/Security/TokenService.cs
public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly IDistributedCache _cache;
    private readonly ILogger<TokenService> _logger;

    public async Task<string> GenerateAccessTokenAsync(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("subscription_tier", user.SubscriptionTier.ToString()),
            new Claim("jti", Guid.NewGuid().ToString()) // JWT ID for revocation
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        // Store token metadata for revocation
        await _cache.SetAsync(
            $"token:{tokenDescriptor.Subject.FindFirst("jti").Value}",
            Encoding.UTF8.GetBytes(user.Id),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = tokenDescriptor.Expires
            }
        );

        return tokenString;
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            var jti = principal.FindFirst("jti")?.Value;

            // Check if token is revoked
            if (!string.IsNullOrEmpty(jti))
            {
                var revoked = await _cache.GetAsync($"revoked:{jti}");
                if (revoked != null)
                {
                    _logger.LogWarning($"Attempted use of revoked token: {jti}");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation failed");
            return false;
        }
    }

    public async Task RevokeTokenAsync(string jti)
    {
        await _cache.SetAsync(
            $"revoked:{jti}",
            new byte[] { 1 },
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            }
        );
    }
}
```

## Data Protection

### 1. Encryption at Rest

```csharp
// services/common/Security/EncryptionService.cs
public class EncryptionService : IEncryptionService
{
    private readonly KeyVaultClient _keyVault;
    private readonly string _keyIdentifier;

    public EncryptionService(IConfiguration configuration)
    {
        _keyVault = new KeyVaultClient(
            new KeyVaultClient.AuthenticationCallback(
                new AzureServiceTokenProvider().KeyVaultTokenCallback));
        _keyIdentifier = configuration["KeyVault:EncryptionKeyIdentifier"];
    }

    public async Task<byte[]> EncryptAsync(byte[] plaintext)
    {
        var result = await _keyVault.EncryptAsync(
            _keyIdentifier,
            JsonWebKeyEncryptionAlgorithm.RSAOAEP256,
            plaintext
        );
        return result.Result;
    }

    public async Task<byte[]> DecryptAsync(byte[] ciphertext)
    {
        var result = await _keyVault.DecryptAsync(
            _keyIdentifier,
            JsonWebKeyEncryptionAlgorithm.RSAOAEP256,
            ciphertext
        );
        return result.Result;
    }

    public string EncryptString(string plaintext)
    {
        using var aes = Aes.Create();
        aes.GenerateKey();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        using var msEncrypt = new MemoryStream();
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (var swEncrypt = new StreamWriter(csEncrypt))
        {
            swEncrypt.Write(plaintext);
        }

        var encrypted = msEncrypt.ToArray();
        var result = new byte[aes.IV.Length + encrypted.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return Convert.ToBase64String(result);
    }
}
```

### 2. Encryption in Transit

```csharp
// infrastructure/security/TlsConfiguration.cs
public static class TlsConfiguration
{
    public static void ConfigureTls(WebApplicationBuilder builder)
    {
        // Enforce TLS 1.2+
        builder.WebHost.UseKestrel(options =>
        {
            options.ConfigureHttpsDefaults(httpsOptions =>
            {
                httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                httpsOptions.CheckCertificateRevocation = true;
                
                // Certificate pinning for high-security environments
                httpsOptions.ServerCertificateSelector = (context, name) =>
                {
                    // Load certificate from Key Vault
                    return LoadCertificateFromKeyVault(name);
                };
                
                httpsOptions.OnAuthenticate = (context, sslOptions) =>
                {
                    sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(
                        new[]
                        {
                            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                            TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256
                        });
                };
            });
        });

        // HSTS
        builder.Services.AddHsts(options =>
        {
            options.Preload = true;
            options.IncludeSubDomains = true;
            options.MaxAge = TimeSpan.FromDays(365);
        });
    }
}
```

## Input Validation and Sanitization

### 1. Request Validation

```csharp
// services/common/Validation/RequestValidator.cs
public class RequestValidator : IRequestValidator
{
    private readonly ILogger<RequestValidator> _logger;

    public ValidationResult ValidateAudioUpload(byte[] audioData, string format)
    {
        var result = new ValidationResult();

        // Check file size
        if (audioData.Length > ValidationRules.Audio.MaxFileSizeMB * 1024 * 1024)
        {
            result.AddError("Audio file exceeds maximum size limit");
        }

        // Validate format
        if (!ValidationRules.Audio.SupportedFormats.Contains(format.ToLower()))
        {
            result.AddError($"Unsupported audio format: {format}");
        }

        // Check for malicious content
        if (ContainsMaliciousPatterns(audioData))
        {
            result.AddError("Audio file contains suspicious patterns");
            _logger.LogWarning("Potential malicious audio upload detected");
        }

        return result;
    }

    public ValidationResult ValidateCodeInstruction(string instruction)
    {
        var result = new ValidationResult();

        // Length validation
        if (string.IsNullOrWhiteSpace(instruction))
        {
            result.AddError("Instruction cannot be empty");
        }
        else if (instruction.Length > ValidationRules.Code.MaxInstructionLength)
        {
            result.AddError("Instruction exceeds maximum length");
        }

        // Check for injection attempts
        if (ContainsSqlInjectionPatterns(instruction) || 
            ContainsScriptInjectionPatterns(instruction))
        {
            result.AddError("Instruction contains potentially malicious content");
            _logger.LogWarning($"Potential injection attempt detected: {instruction.Substring(0, 50)}...");
        }

        return result;
    }

    private bool ContainsMaliciousPatterns(byte[] data)
    {
        // Check for common malicious file signatures
        var signatures = new[]
        {
            new byte[] { 0x4D, 0x5A }, // EXE
            new byte[] { 0x50, 0x4B, 0x03, 0x04 }, // ZIP (potential malware container)
        };

        foreach (var signature in signatures)
        {
            if (data.Take(signature.Length).SequenceEqual(signature))
            {
                return true;
            }
        }

        return false;
    }

    private bool ContainsSqlInjectionPatterns(string input)
    {
        var patterns = new[]
        {
            @"('|(\-\-)|(;)|(\|\|)|(\*))",
            @"(union|select|insert|update|delete|drop|create)\s",
            @"(exec(\s|\+)+(s|x)p\w+)"
        };

        return patterns.Any(pattern => 
            Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
    }

    private bool ContainsScriptInjectionPatterns(string input)
    {
        var patterns = new[]
        {
            @"<script[^>]*>.*?</script>",
            @"javascript\s*:",
            @"on\w+\s*="
        };

        return patterns.Any(pattern => 
            Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
    }
}
```

### 2. Output Sanitization

```csharp
// services/common/Security/OutputSanitizer.cs
public class OutputSanitizer : IOutputSanitizer
{
    public string SanitizeForDisplay(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // HTML encode
        var sanitized = HttpUtility.HtmlEncode(input);
        
        // Additional sanitization for common XSS vectors
        sanitized = Regex.Replace(sanitized, @"javascript\s*:", "", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"on\w+\s*=", "", RegexOptions.IgnoreCase);
        
        return sanitized;
    }

    public string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "unnamed";

        // Remove path traversal attempts
        fileName = Path.GetFileName(fileName);
        
        // Remove invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", fileName.Split(invalidChars));
        
        // Limit length
        if (sanitized.Length > ValidationRules.Files.MaxFileNameLength)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(sanitized);
            var maxNameLength = ValidationRules.Files.MaxFileNameLength - extension.Length;
            sanitized = nameWithoutExtension.Substring(0, maxNameLength) + extension;
        }
        
        return sanitized;
    }
}
```

## API Security

### 1. Rate Limiting

```csharp
// infrastructure/security/RateLimitingConfiguration.cs
public static class RateLimitingConfiguration
{
    public static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 100,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            // Different limits for different endpoints
            options.AddPolicy("VoiceProcessing", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? "anonymous",
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddPolicy("CodeGeneration", httpContext =>
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: httpContext.User?.Identity?.Name ?? "anonymous",
                    factory: partition => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 50,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(10),
                        TokensPerPeriod = 10,
                        AutoReplenishment = true
                    }));

            options.OnRejected = async (context, _) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please try again later.");
            };
        });
    }
}
```

### 2. CORS Configuration

```csharp
// infrastructure/security/CorsConfiguration.cs
public static class CorsConfiguration
{
    public static void ConfigureCors(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("Production", builder =>
            {
                builder
                    .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>())
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                    .WithHeaders(
                        "Authorization",
                        "Content-Type",
                        "X-Request-Id",
                        "X-Session-Id")
                    .WithExposedHeaders(
                        "X-Request-Id",
                        "X-RateLimit-Remaining",
                        "X-RateLimit-Reset")
                    .SetPreflightMaxAge(TimeSpan.FromHours(24))
                    .AllowCredentials();
            });

            options.AddPolicy("Development", builder =>
            {
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });
    }
}
```

## Secret Management

### 1. Azure Key Vault Integration

```csharp
// infrastructure/security/KeyVaultConfiguration.cs
public static class KeyVaultConfiguration
{
    public static void ConfigureKeyVault(IConfigurationBuilder config, string keyVaultName)
    {
        var keyVaultEndpoint = new Uri($"https://{keyVaultName}.vault.azure.net/");
        
        config.AddAzureKeyVault(
            keyVaultEndpoint,
            new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeEnvironmentCredential = false,
                ExcludeInteractiveBrowserCredential = true,
                ExcludeAzureCliCredential = false,
                ExcludeAzurePowerShellCredential = false,
                ExcludeSharedTokenCacheCredential = false,
                ExcludeVisualStudioCredential = false,
                ExcludeVisualStudioCodeCredential = false,
                ExcludeManagedIdentityCredential = false
            }));
    }
}

// Usage in Program.cs
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/"),
    new DefaultAzureCredential());
```

### 2. Secret Rotation

```csharp
// services/common/Security/SecretRotationService.cs
public class SecretRotationService : BackgroundService
{
    private readonly ISecretClient _keyVault;
    private readonly ILogger<SecretRotationService> _logger;
    private readonly TimeSpan _rotationInterval = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RotateSecretsAsync();
                await Task.Delay(_rotationInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during secret rotation");
            }
        }
    }

    private async Task RotateSecretsAsync()
    {
        var secretsToRotate = new[] { "ClaudeApiKey", "StorageConnectionString", "ServiceBusConnection" };

        foreach (var secretName in secretsToRotate)
        {
            var secret = await _keyVault.GetSecretAsync(secretName);
            
            if (IsRotationRequired(secret))
            {
                var newValue = await GenerateNewSecretValueAsync(secretName);
                
                // Create new version
                await _keyVault.SetSecretAsync(secretName, newValue, new SecretProperties
                {
                    ExpiresOn = DateTimeOffset.UtcNow.AddDays(90),
                    Tags = { ["rotated"] = DateTimeOffset.UtcNow.ToString("O") }
                });

                _logger.LogInformation($"Rotated secret: {secretName}");
            }
        }
    }

    private bool IsRotationRequired(KeyVaultSecret secret)
    {
        if (secret.Properties.ExpiresOn.HasValue)
        {
            var daysUntilExpiry = (secret.Properties.ExpiresOn.Value - DateTimeOffset.UtcNow).TotalDays;
            return daysUntilExpiry < 7;
        }

        // Check last rotation date
        if (secret.Properties.Tags.TryGetValue("rotated", out var lastRotated))
        {
            if (DateTimeOffset.TryParse(lastRotated, out var rotationDate))
            {
                return (DateTimeOffset.UtcNow - rotationDate) > _rotationInterval;
            }
        }

        return true; // Rotate if no rotation history
    }
}
```

## Audit and Compliance

### 1. Audit Logging

```csharp
// services/common/Auditing/AuditService.cs
public class AuditService : IAuditService
{
    private readonly CosmosClient _cosmosClient;
    private readonly Container _auditContainer;
    private readonly ILogger<AuditService> _logger;

    public AuditService(CosmosClient cosmosClient, IConfiguration configuration)
    {
        _cosmosClient = cosmosClient;
        _auditContainer = _cosmosClient.GetContainer(
            configuration["Cosmos:DatabaseName"],
            "AuditLogs"
        );
    }

    public async Task LogAsync(AuditEvent auditEvent)
    {
        try
        {
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid().ToString(),
                UserId = auditEvent.UserId,
                Action = auditEvent.Action,
                ResourceType = auditEvent.ResourceType,
                ResourceId = auditEvent.ResourceId,
                Changes = auditEvent.Changes,
                IpAddress = auditEvent.IpAddress,
                UserAgent = auditEvent.UserAgent,
                Result = auditEvent.Result,
                ErrorMessage = auditEvent.ErrorMessage,
                Timestamp = DateTime.UtcNow,
                CorrelationId = auditEvent.CorrelationId,
                AdditionalData = auditEvent.AdditionalData
            };

            await _auditContainer.CreateItemAsync(
                auditLog,
                new PartitionKey(auditLog.UserId)
            );

            // Also send to SIEM if configured
            await SendToSiemAsync(auditLog);
        }
        catch (Exception ex)
        {
            // Audit logging should never throw
            _logger.LogError(ex, "Failed to write audit log");
        }
    }

    public async Task<List<AuditLog>> QueryAsync(AuditQuery query)
    {
        var queryDefinition = new QueryDefinition(
            "SELECT * FROM c WHERE c.userId = @userId AND c.timestamp >= @startDate AND c.timestamp <= @endDate")
            .WithParameter("@userId", query.UserId)
            .WithParameter("@startDate", query.StartDate)
            .WithParameter("@endDate", query.EndDate);

        var results = new List<AuditLog>();
        using var iterator = _auditContainer.GetItemQueryIterator<AuditLog>(queryDefinition);
        
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        return results;
    }

    private async Task SendToSiemAsync(AuditLog log)
    {
        // Send to Azure Sentinel or other SIEM
        // Implementation depends on SIEM choice
    }
}

// Audit middleware
public class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuditService _auditService;

    public async Task InvokeAsync(HttpContext context)
    {
        var auditEvent = new AuditEvent
        {
            UserId = context.User?.Identity?.Name,
            Action = $"{context.Request.Method} {context.Request.Path}",
            ResourceType = "API",
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers["User-Agent"].ToString(),
            CorrelationId = context.Request.Headers["X-Correlation-Id"].ToString()
        };

        try
        {
            await _next(context);
            auditEvent.Result = "Success";
        }
        catch (Exception ex)
        {
            auditEvent.Result = "Failure";
            auditEvent.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            await _auditService.LogAsync(auditEvent);
        }
    }
}
```

### 2. Compliance Monitoring

```csharp
// services/common/Compliance/ComplianceService.cs
public class ComplianceService : IComplianceService
{
    public async Task<ComplianceReport> GenerateComplianceReportAsync(DateTime startDate, DateTime endDate)
    {
        var report = new ComplianceReport
        {
            Period = new DateRange(startDate, endDate),
            GeneratedAt = DateTime.UtcNow
        };

        // Data retention compliance
        report.DataRetention = await CheckDataRetentionComplianceAsync();
        
        // Access control compliance
        report.AccessControl = await CheckAccessControlComplianceAsync();
        
        // Encryption compliance
        report.Encryption = await CheckEncryptionComplianceAsync();
        
        // Audit trail compliance
        report.AuditTrail = await CheckAuditTrailComplianceAsync();

        return report;
    }

    private async Task<DataRetentionCompliance> CheckDataRetentionComplianceAsync()
    {
        return new DataRetentionCompliance
        {
            PersonalDataRetentionDays = 365,
            AudioDataRetentionDays = 30,
            LogRetentionDays = 90,
            BackupRetentionDays = 30,
            DeletionRequestsProcessed = await CountDeletionRequestsAsync(),
            OrphanedDataFound = await CheckForOrphanedDataAsync()
        };
    }
}
```

## Network Security

### 1. Azure Virtual Network Configuration

```bicep
// infrastructure/network/vnet.bicep
resource vnet 'Microsoft.Network/virtualNetworks@2021-05-01' = {
  name: 'voicecode-vnet'
  location: resourceGroup().location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'WebAppSubnet'
        properties: {
          addressPrefix: '10.0.1.0/24'
          serviceEndpoints: [
            {
              service: 'Microsoft.Storage'
            }
            {
              service: 'Microsoft.Sql'
            }
            {
              service: 'Microsoft.ServiceBus'
            }
            {
              service: 'Microsoft.KeyVault'
            }
          ]
          delegations: [
            {
              name: 'webapp'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'PrivateEndpointSubnet'
        properties: {
          addressPrefix: '10.0.2.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// Network Security Groups
resource webAppNSG 'Microsoft.Network/networkSecurityGroups@2021-05-01' = {
  name: 'webapp-nsg'
  location: resourceGroup().location
  properties: {
    securityRules: [
      {
        name: 'AllowHTTPS'
        properties: {
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: '*'
          access: 'Allow'
          priority: 100
          direction: 'Inbound'
        }
      }
      {
        name: 'DenyAllInbound'
        properties: {
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
          access: 'Deny'
          priority: 4096
          direction: 'Inbound'
        }
      }
    ]
  }
}
```

### 2. Private Endpoints

```csharp
// infrastructure/PrivateEndpoints.cs
public static class PrivateEndpointConfiguration
{
    public static void ConfigurePrivateEndpoints(IServiceCollection services)
    {
        // Configure services to use private endpoints
        services.Configure<CosmosOptions>(options =>
        {
            options.ConnectionMode = ConnectionMode.Direct;
            options.LimitToEndpoint = true;
        });

        services.Configure<ServiceBusOptions>(options =>
        {
            options.TransportType = ServiceBusTransportType.AmqpTcp;
            options.EnablePrivateEndpoint = true;
        });
    }
}
```

## DDoS Protection

### 1. Azure DDoS Protection

```bicep
// infrastructure/ddos-protection.bicep
resource ddosProtectionPlan 'Microsoft.Network/ddosProtectionPlans@2021-05-01' = {
  name: 'voicecode-ddos-protection'
  location: resourceGroup().location
  properties: {}
}

// Associate with VNet
resource vnet 'Microsoft.Network/virtualNetworks@2021-05-01' existing = {
  name: 'voicecode-vnet'
}

resource vnetUpdate 'Microsoft.Network/virtualNetworks@2021-05-01' = {
  name: vnet.name
  location: vnet.location
  properties: union(vnet.properties, {
    enableDdosProtection: true
    ddosProtectionPlan: {
      id: ddosProtectionPlan.id
    }
  })
}
```

### 2. Application-Level DDoS Protection

```csharp
// middleware/DDoSProtectionMiddleware.cs
public class DDoSProtectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DDoSProtectionMiddleware> _logger;
    private readonly DDoSProtectionOptions _options;

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(clientIp))
        {
            await _next(context);
            return;
        }

        var key = $"ddos_protection:{clientIp}";
        var requestCount = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(1));
            return 0;
        });

        if (requestCount >= _options.MaxRequestsPerMinute)
        {
            _logger.LogWarning($"Potential DDoS attack from IP: {clientIp}");
            context.Response.StatusCode = 429;
            await context.Response.WriteAsync("Too many requests");
            return;
        }

        _cache.Set(key, requestCount + 1, TimeSpan.FromMinutes(1));
        await _next(context);
    }
}
```

## Scalability Patterns

### 1. Horizontal Scaling

```csharp
// infrastructure/scaling/AutoScaleConfiguration.cs
public static class AutoScaleConfiguration
{
    public static void ConfigureAutoScaling(WebApplication app)
    {
        // Configure health checks for load balancer
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false // Liveness probe
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready") // Readiness probe
        });
    }
}

// Azure autoscale settings (Bicep)
resource autoscaleSettings 'Microsoft.Insights/autoscalesettings@2021-05-01-preview' = {
  name: 'voicecode-autoscale'
  location: resourceGroup().location
  properties: {
    targetResourceUri: appServicePlan.id
    enabled: true
    profiles: [
      {
        name: 'Default'
        capacity: {
          minimum: '2'
          maximum: '10'
          default: '2'
        }
        rules: [
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 70
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: 30
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
        ]
      }
    ]
  }
}
```

### 2. Caching Strategy

```csharp
// services/common/Caching/CachingService.cs
public class DistributedCachingService : ICachingService
{
    private readonly IDistributedCache _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<DistributedCachingService> _logger;

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        CacheOptions options = null)
    {
        options ??= CacheOptions.Default;

        // L1 Cache (Memory)
        if (_memoryCache.TryGetValue(key, out T cachedValue))
        {
            return cachedValue;
        }

        // L2 Cache (Redis)
        var distributedValue = await _distributedCache.GetAsync(key);
        if (distributedValue != null)
        {
            var deserializedValue = JsonSerializer.Deserialize<T>(distributedValue);
            
            // Populate L1 cache
            _memoryCache.Set(key, deserializedValue, options.MemoryCacheDuration);
            
            return deserializedValue;
        }

        // Cache miss - execute factory
        var value = await factory();

        // Set in both caches
        var serializedValue = JsonSerializer.SerializeToUtf8Bytes(value);
        
        await _distributedCache.SetAsync(
            key,
            serializedValue,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.DistributedCacheDuration
            }
        );

        _memoryCache.Set(key, value, options.MemoryCacheDuration);

        return value;
    }

    public async Task InvalidateAsync(string key)
    {
        _memoryCache.Remove(key);
        await _distributedCache.RemoveAsync(key);
        
        // Publish cache invalidation event for other instances
        await PublishCacheInvalidationAsync(key);
    }
}

public class CacheOptions
{
    public TimeSpan MemoryCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan DistributedCacheDuration { get; set; } = TimeSpan.FromHours(1);
    
    public static CacheOptions Default => new();
    public static CacheOptions ShortTerm => new() 
    { 
        MemoryCacheDuration = TimeSpan.FromMinutes(1),
        DistributedCacheDuration = TimeSpan.FromMinutes(5)
    };
    public static CacheOptions LongTerm => new()
    {
        MemoryCacheDuration = TimeSpan.FromHours(1),
        DistributedCacheDuration = TimeSpan.FromHours(24)
    };
}
```

### 3. Database Scaling

```csharp
// infrastructure/database/CosmosDbScaling.cs
public static class CosmosDbScaling
{
    public static void ConfigureCosmosDb(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<CosmosClient>(serviceProvider =>
        {
            var cosmosClientOptions = new CosmosClientOptions
            {
                ApplicationName = "VoiceCode",
                ConnectionMode = ConnectionMode.Direct,
                
                // Performance optimizations
                MaxRetryAttemptsOnRateLimitedRequests = 9,
                MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
                
                // Connection pool settings
                GatewayModeMaxConnectionLimit = 50,
                MaxTcpConnectionsPerEndpoint = 100,
                MaxRequestsPerTcpConnection = 30,
                
                // Consistency level for better performance
                ConsistencyLevel = ConsistencyLevel.Session,
                
                // Enable bulk operations
                AllowBulkExecution = true,
                
                // Request timeout
                RequestTimeout = TimeSpan.FromSeconds(60),
                
                // Telemetry
                EnableTcpConnectionEndpointRediscovery = true
            };

            return new CosmosClient(
                configuration["CosmosDb:ConnectionString"],
                cosmosClientOptions
            );
        });

        // Configure container with partition key
        services.AddSingleton<Container>(serviceProvider =>
        {
            var cosmosClient = serviceProvider.GetRequiredService<CosmosClient>();
            var database = cosmosClient.GetDatabase(configuration["CosmosDb:DatabaseName"]);
            
            // Ensure container exists with proper configuration
            var containerResponse = database.CreateContainerIfNotExistsAsync(
                new ContainerProperties
                {
                    Id = "UserData",
                    PartitionKeyPath = "/userId",
                    IndexingPolicy = new IndexingPolicy
                    {
                        IndexingMode = IndexingMode.Consistent,
                        IncludedPaths = { new IncludedPath { Path = "/*" } },
                        ExcludedPaths = { new ExcludedPath { Path = "/audioData/*" } }
                    },
                    DefaultTimeToLive = -1 // Enable TTL
                },
                ThroughputProperties.CreateAutoscaleThroughput(4000) // Autoscale RU/s
            ).GetAwaiter().GetResult();

            return containerResponse.Container;
        });
    }
}
```

### 4. Message Queue Scaling

```csharp
// infrastructure/messaging/ServiceBusScaling.cs
public static class ServiceBusScaling
{
    public static void ConfigureServiceBus(IServiceCollection services, IConfiguration configuration)
    {
        // Service Bus client with connection pooling
        services.AddSingleton<ServiceBusClient>(serviceProvider =>
        {
            var options = new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets,
                RetryOptions = new ServiceBusRetryOptions
                {
                    Mode = ServiceBusRetryMode.Exponential,
                    MaxRetries = 5,
                    Delay = TimeSpan.FromSeconds(1),
                    MaxDelay = TimeSpan.FromSeconds(30)
                }
            };

            return new ServiceBusClient(
                configuration["ServiceBus:ConnectionString"],
                options
            );
        });

        // Configure processors with scaling options
        services.AddSingleton<ServiceBusProcessor>(serviceProvider =>
        {
            var client = serviceProvider.GetRequiredService<ServiceBusClient>();
            
            var processorOptions = new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Environment.ProcessorCount * 2,
                PrefetchCount = 100,
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
            };

            return client.CreateProcessor(
                configuration["ServiceBus:QueueName"],
                processorOptions
            );
        });
    }
}

// Bicep template for Service Bus with partitioning
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: 'voicecode-servicebus'
  location: resourceGroup().location
  sku: {
    name: 'Premium'
    tier: 'Premium'
    capacity: 1
  }
  properties: {
    zoneRedundant: true
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  parent: serviceBusNamespace
  name: 'voice-processing'
  properties: {
    maxSizeInMegabytes: 5120
    requiresDuplicateDetection: true
    requiresSession: false
    defaultMessageTimeToLive: 'PT4H'
    deadLetteringOnMessageExpiration: true
    enableBatchedOperations: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    maxDeliveryCount: 3
    enablePartitioning: true
    enableExpress: false
  }
}
```

## Performance Optimization

### 1. Async Processing

```csharp
// services/common/Performance/AsyncProcessor.cs
public class AsyncBatchProcessor<T>
{
    private readonly Channel<T> _channel;
    private readonly Func<IReadOnlyList<T>, Task> _processBatch;
    private readonly int _batchSize;
    private readonly TimeSpan _batchTimeout;
    private readonly ILogger _logger;

    public AsyncBatchProcessor(
        Func<IReadOnlyList<T>, Task> processBatch,
        int batchSize = 100,
        TimeSpan? batchTimeout = null,
        ILogger logger = null)
    {
        _processBatch = processBatch;
        _batchSize = batchSize;
        _batchTimeout = batchTimeout ?? TimeSpan.FromSeconds(5);
        _logger = logger;
        
        _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
    }

    public async Task EnqueueAsync(T item)
    {
        await _channel.Writer.WriteAsync(item);
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var batch = new List<T>(_batchSize);
        using var timeoutCts = new CancellationTokenSource();
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                timeoutCts.CancelAfter(_batchTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token
                );

                while (batch.Count < _batchSize)
                {
                    if (await _channel.Reader.WaitToReadAsync(linkedCts.Token))
                    {
                        if (_channel.Reader.TryRead(out var item))
                        {
                            batch.Add(item);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                // Timeout reached, process current batch
            }

            if (batch.Count > 0)
            {
                try
                {
                    await _processBatch(batch);
                    _logger?.LogInformation($"Processed batch of {batch.Count} items");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Error processing batch of {batch.Count} items");
                }
                finally
                {
                    batch.Clear();
                    timeoutCts.TryReset();
                }
            }
        }
    }
}
```

### 2. Connection Pooling

```csharp
// services/common/Performance/ConnectionPooling.cs
public class HttpClientFactory
{
    private readonly Dictionary<string, HttpClient> _httpClients = new();
    private readonly Dictionary<string, SocketsHttpHandler> _handlers = new();
    private readonly object _lock = new();

    public HttpClient GetClient(string name, Action<SocketsHttpHandler> configureHandler = null)
    {
        lock (_lock)
        {
            if (_httpClients.TryGetValue(name, out var existingClient))
            {
                return existingClient;
            }

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 100,
                EnableMultipleHttp2Connections = true,
                AutomaticDecompression = DecompressionMethods.All
            };

            configureHandler?.Invoke(handler);

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClients[name] = client;
            _handlers[name] = handler;

            return client;
        }
    }
}
```

## Monitoring and Alerting

### 1. Application Insights Integration

```csharp
// infrastructure/monitoring/TelemetryConfiguration.cs
public static class TelemetryConfiguration
{
    public static void ConfigureTelemetry(IServiceCollection services, IConfiguration configuration)
    {
        services.AddApplicationInsightsTelemetry(options =>
        {
            options.ConnectionString = configuration["ApplicationInsights:ConnectionString"];
            options.EnableAdaptiveSampling = true;
            options.EnableQuickPulseMetricStream = true;
            options.EnableDependencyTrackingTelemetryModule = true;
            options.EnablePerformanceCounterCollectionModule = true;
            options.EnableEventCounterCollectionModule = true;
            options.EnableDiagnosticsTelemetryModule = true;
        });

        // Custom telemetry initializers
        services.AddSingleton<ITelemetryInitializer, CustomTelemetryInitializer>();
        
        // Custom telemetry processors
        services.AddApplicationInsightsTelemetryProcessor<SecurityTelemetryProcessor>();
        services.AddApplicationInsightsTelemetryProcessor<PerformanceTelemetryProcessor>();
    }
}

public class CustomTelemetryInitializer : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.GlobalProperties["Environment"] = 
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        telemetry.Context.GlobalProperties["ServiceName"] = "VoiceCode";
        
        if (telemetry is RequestTelemetry request)
        {
            // Add custom properties
            if (HttpContext.Current != null)
            {
                request.Properties["UserId"] = 
                    HttpContext.Current.User?.Identity?.Name ?? "anonymous";
                request.Properties["SessionId"] = 
                    HttpContext.Current.Request.Headers["X-Session-Id"].ToString();
            }
        }
    }
}

public class SecurityTelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;

    public SecurityTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next;
    }

    public void Process(ITelemetry item)
    {
        // Remove sensitive data
        if (item is DependencyTelemetry dependency)
        {
            // Redact connection strings
            if (dependency.Type == "SQL" || dependency.Type == "Azure Service Bus")
            {
                dependency.Data = RedactConnectionString(dependency.Data);
            }
        }
        
        if (item is RequestTelemetry request)
        {
            // Remove auth headers
            request.Properties.Remove("Authorization");
            request.Properties.Remove("X-API-Key");
        }

        _next.Process(item);
    }

    private string RedactConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        // Redact sensitive parts
        return Regex.Replace(
            connectionString,
            @"(Password|pwd|SharedAccessKey)=([^;]+)",
            "$1=***REDACTED***",
            RegexOptions.IgnoreCase
        );
    }
}
```

### 2. Custom Metrics

```csharp
// services/common/Monitoring/MetricsService.cs
public class MetricsService : IMetricsService
{
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<MetricsService> _logger;

    public void TrackCodeGeneration(CodeGenerationMetrics metrics)
    {
        _telemetryClient.TrackMetric("CodeGeneration.Duration", metrics.DurationMs);
        _telemetryClient.TrackMetric("CodeGeneration.TokensUsed", metrics.TokensUsed);
        _telemetryClient.TrackMetric("CodeGeneration.LinesGenerated", metrics.LinesGenerated);
        
        _telemetryClient.TrackEvent("CodeGeneration", new Dictionary<string, string>
        {
            ["Language"] = metrics.Language,
            ["UserId"] = metrics.UserId,
            ["Success"] = metrics.Success.ToString()
        });
    }

    public void TrackApiCall(ApiCallMetrics metrics)
    {
        var properties = new Dictionary<string, string>
        {
            ["Endpoint"] = metrics.Endpoint,
            ["Method"] = metrics.Method,
            ["StatusCode"] = metrics.StatusCode.ToString(),
            ["UserId"] = metrics.UserId
        };

        var telemetryMetrics = new Dictionary<string, double>
        {
            ["Duration"] = metrics.DurationMs,
            ["RequestSize"] = metrics.RequestSize,
            ["ResponseSize"] = metrics.ResponseSize
        };

        _telemetryClient.TrackEvent("ApiCall", properties, telemetryMetrics);
        
        // Track SLA metrics
        if (metrics.DurationMs > 1000)
        {
            _telemetryClient.TrackMetric("SLA.SlowRequests", 1);
        }
        
        if (metrics.StatusCode >= 500)
        {
            _telemetryClient.TrackMetric("SLA.ServerErrors", 1);
        }
    }
}
```

### 3. Alerting Rules

```bicep
// infrastructure/monitoring/alerts.bicep
resource apiErrorAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'voicecode-api-errors'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ServerErrors'
          metricName: 'requests/failed'
          dimensions: []
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Count'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

resource performanceAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'voicecode-performance'
  location: 'global'
  properties: {
    severity: 3
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ResponseTime'
          metricName: 'requests/duration'
          dimensions: []
          operator: 'GreaterThan'
          threshold: 2000
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}
```

## Disaster Recovery

### 1. Backup Strategy

```csharp
// services/common/DisasterRecovery/BackupService.cs
public class BackupService : IBackupService
{
    private readonly CosmosClient _cosmosClient;
    private readonly BlobServiceClient _blobClient;
    private readonly ILogger<BackupService> _logger;

    public async Task<BackupResult> CreateBackupAsync(BackupRequest request)
    {
        var backupId = Guid.NewGuid().ToString();
        var backupContainer = _blobClient.GetBlobContainerClient("backups");
        await backupContainer.CreateIfNotExistsAsync();

        var result = new BackupResult
        {
            BackupId = backupId,
            StartTime = DateTime.UtcNow,
            Type = request.Type
        };

        try
        {
            // Backup Cosmos DB
            if (request.IncludeDatabase)
            {
                await BackupCosmosDbAsync(backupId, backupContainer);
                result.DatabaseBackupSize = await GetBackupSizeAsync(backupContainer, $"{backupId}/database");
            }

            // Backup configuration
            if (request.IncludeConfiguration)
            {
                await BackupConfigurationAsync(backupId, backupContainer);
                result.ConfigurationBackupSize = await GetBackupSizeAsync(backupContainer, $"{backupId}/config");
            }

            // Backup user data
            if (request.IncludeUserData)
            {
                await BackupUserDataAsync(backupId, backupContainer);
                result.UserDataBackupSize = await GetBackupSizeAsync(backupContainer, $"{backupId}/userdata");
            }

            result.EndTime = DateTime.UtcNow;
            result.Success = true;

            // Store backup metadata
            await StoreBackupMetadataAsync(result);

            _logger.LogInformation($"Backup completed successfully: {backupId}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, $"Backup failed: {backupId}");
        }

        return result;
    }

    private async Task BackupCosmosDbAsync(string backupId, BlobContainerClient container)
    {
        var database = _cosmosClient.GetDatabase("VoiceCode");
        var containers = new[] { "Users", "CodeHistory", "AuditLogs" };

        foreach (var containerName in containers)
        {
            var cosmosContainer = database.GetContainer(containerName);
            var query = cosmosContainer.GetItemQueryIterator<dynamic>();
            var items = new List<dynamic>();

            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                items.AddRange(response);
            }

            // Save to blob storage
            var blobName = $"{backupId}/database/{containerName}.json";
            var blob = container.GetBlobClient(blobName);
            
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, items);
            stream.Position = 0;
            
            await blob.UploadAsync(stream, overwrite: true);
        }
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupId)
    {
        // Implementation for restore process
        throw new NotImplementedException();
    }
}
```

### 2. Geo-Replication

```bicep
// infrastructure/geo-replication.bicep
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2021-10-15' = {
  name: 'voicecode-cosmos'
  location: primaryRegion
  kind: 'GlobalDocumentDB'
  properties: {
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
      maxStalenessPrefix: 100
      maxIntervalInSeconds: 5
    }
    locations: [
      {
        locationName: primaryRegion
        failoverPriority: 0
        isZoneRedundant: true
      }
      {
        locationName: secondaryRegion
        failoverPriority: 1
        isZoneRedundant: true
      }
    ]
    databaseAccountOfferType: 'Standard'
    enableAutomaticFailover: true
    enableMultipleWriteLocations: true
    backupPolicy: {
      type: 'Continuous'
    }
  }
}

// Traffic Manager for geo-routing
resource trafficManager 'Microsoft.Network/trafficmanagerprofiles@2018-08-01' = {
  name: 'voicecode-traffic-manager'
  location: 'global'
  properties: {
    profileStatus: 'Enabled'
    trafficRoutingMethod: 'Performance'
    dnsConfig: {
      relativeName: 'voicecode'
      ttl: 60
    }
    monitorConfig: {
      protocol: 'HTTPS'
      port: 443
      path: '/health/live'
      intervalInSeconds: 30
      toleratedNumberOfFailures: 3
      timeoutInSeconds: 10
    }
    endpoints: [
      {
        name: 'primary-endpoint'
        type: 'Microsoft.Network/trafficManagerProfiles/azureEndpoints'
        properties: {
          targetResourceId: primaryWebApp.id
          endpointStatus: 'Enabled'
          weight: 100
          priority: 1
        }
      }
      {
        name: 'secondary-endpoint'
        type: 'Microsoft.Network/trafficManagerProfiles/azureEndpoints'
        properties: {
          targetResourceId: secondaryWebApp.id
          endpointStatus: 'Enabled'
          weight: 100
          priority: 2
        }
      }
    ]
  }
}
```

## Compliance and Governance

### 1. Data Privacy

```csharp
// services/common/Privacy/DataPrivacyService.cs
public class DataPrivacyService : IDataPrivacyService
{
    public async Task<ExportDataResult> ExportUserDataAsync(string userId)
    {
        var result = new ExportDataResult
        {
            UserId = userId,
            RequestedAt = DateTime.UtcNow
        };

        // Collect all user data
        var userData = new UserDataExport
        {
            Profile = await GetUserProfileAsync(userId),
            CodeHistory = await GetCodeHistoryAsync(userId),
            AudioRecordings = await GetAudioRecordingsAsync(userId),
            AuditLogs = await GetAuditLogsAsync(userId),
            Preferences = await GetUserPreferencesAsync(userId)
        };

        // Create encrypted archive
        var encryptedData = await EncryptUserDataAsync(userData);
        
        // Store in secure location
        var downloadUrl = await StoreExportAsync(userId, encryptedData);
        
        result.DownloadUrl = downloadUrl;
        result.ExpiresAt = DateTime.UtcNow.AddDays(7);
        result.Success = true;

        // Audit the export
        await AuditDataExportAsync(userId);

        return result;
    }

    public async Task<DeleteDataResult> DeleteUserDataAsync(string userId, bool hardDelete = false)
    {
        var result = new DeleteDataResult
        {
            UserId = userId,
            RequestedAt = DateTime.UtcNow,
            HardDelete = hardDelete
        };

        try
        {
            if (hardDelete)
            {
                // Permanently delete all user data
                await DeleteFromCosmosDbAsync(userId);
                await DeleteFromBlobStorageAsync(userId);
                await DeleteFromCacheAsync(userId);
            }
            else
            {
                // Soft delete - anonymize data
                await AnonymizeUserDataAsync(userId);
            }

            result.Success = true;
            result.CompletedAt = DateTime.UtcNow;

            // Audit the deletion
            await AuditDataDeletionAsync(userId, hardDelete);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, $"Failed to delete user data: {userId}");
        }

        return result;
    }

    private async Task AnonymizeUserDataAsync(string userId)
    {
        // Replace PII with anonymized values
        var anonymizedUser = new User
        {
            Id = userId,
            Email = $"deleted_{Guid.NewGuid():N}@anonymous.local",
            DisplayName = "Deleted User",
            Metadata = new Dictionary<string, object>
            {
                ["anonymized"] = true,
                ["anonymizedAt"] = DateTime.UtcNow
            }
        };

        await UpdateUserAsync(anonymizedUser);
    }
}
```

### 2. Audit Trail

```csharp
// services/common/Compliance/ComplianceAuditService.cs
public class ComplianceAuditService : IComplianceAuditService
{
    private readonly ImmutableLog _immutableLog;
    private readonly IEncryptionService _encryption;

    public async Task LogComplianceEventAsync(ComplianceEvent complianceEvent)
    {
        // Create tamper-proof log entry
        var logEntry = new ImmutableLogEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            EventType = complianceEvent.Type,
            UserId = complianceEvent.UserId,
            Action = complianceEvent.Action,
            Data = complianceEvent.Data,
            IpAddress = complianceEvent.IpAddress,
            UserAgent = complianceEvent.UserAgent
        };

        // Calculate hash for integrity
        logEntry.Hash = CalculateHash(logEntry);
        
        // Link to previous entry
        var previousEntry = await _immutableLog.GetLatestEntryAsync();
        if (previousEntry != null)
        {
            logEntry.PreviousHash = previousEntry.Hash;
        }

        // Encrypt sensitive data
        logEntry.EncryptedData = await _encryption.EncryptAsync(
            JsonSerializer.SerializeToUtf8Bytes(complianceEvent.SensitiveData)
        );

        // Store in immutable log
        await _immutableLog.AppendAsync(logEntry);

        // Send to SIEM for real-time monitoring
        await SendToSiemAsync(logEntry);
    }

    private string CalculateHash(ImmutableLogEntry entry)
    {
        using var sha256 = SHA256.Create();
        var data = $"{entry.Id}{entry.Timestamp:O}{entry.EventType}{entry.UserId}{entry.Action}{entry.PreviousHash}";
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    public async Task<bool> VerifyLogIntegrityAsync(DateTime startDate, DateTime endDate)
    {
        var entries = await _immutableLog.GetEntriesAsync(startDate, endDate);
        
        string previousHash = null;
        foreach (var entry in entries.OrderBy(e => e.Timestamp))
        {
            // Verify hash
            var calculatedHash = CalculateHash(entry);
            if (calculatedHash != entry.Hash)
            {
                _logger.LogError($"Hash mismatch for entry {entry.Id}");
                return false;
            }

            // Verify chain
            if (previousHash != null && entry.PreviousHash != previousHash)
            {
                _logger.LogError($"Chain broken at entry {entry.Id}");
                return false;
            }

            previousHash = entry.Hash;
        }

        return true;
    }
}
```

## Summary

This comprehensive security and scalability guide ensures VoiceCode:

1. **Security**:
   - Implements defense-in-depth with multiple security layers
   - Protects data at rest and in transit
   - Provides comprehensive audit trails
   - Ensures compliance with privacy regulations

2. **Scalability**:
   - Supports horizontal scaling across all services
   - Implements efficient caching strategies
   - Uses asynchronous processing patterns
   - Optimizes database and message queue performance

3. **Reliability**:
   - Provides disaster recovery capabilities
   - Implements geo-replication
   - Ensures high availability
   - Monitors system health continuously

4. **Compliance**:
   - Maintains immutable audit logs
   - Supports data privacy requirements
   - Enables data export and deletion
   - Provides compliance reporting