using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Retail.Api.KafkaProducer.Configurations.SeriLogConfig;
using Retail.Core;
using Serilog;
using System.Security.Claims;
using System.Text.Json;

// --- builder e configurazione base ---
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

// Keycloak settings
var keycloakSettings = builder.Configuration.GetSection("Keycloak");

// Validate critical configuration early
var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"];
var kafkaTopic = builder.Configuration["Kafka:TopicName"];

if (string.IsNullOrWhiteSpace(kafkaBootstrap))
{
    Log.Fatal("Kafka:BootstrapServers mancante nella configurazione. Impossibile avviare.");
    return;
}
if (string.IsNullOrWhiteSpace(kafkaTopic))
{
    Log.Warning("Kafka:TopicName non configurato. Endpoint di produzione Kafka potrebbe fallire.");
}

// --- Authentication / Keycloak ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = keycloakSettings["Authority"];
    options.Audience = keycloakSettings["Audience"];
    options.RequireHttpsMetadata = bool.TryParse(keycloakSettings["RequireHttpsMetadata"], out var r) && r;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "name",
        RoleClaimType = "role", // corrisponde al claim che aggiungiamo manualmente
        ValidAudience = keycloakSettings["Audience"]
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var claimsIdentity = context.Principal?.Identity as ClaimsIdentity;
            if (claimsIdentity != null)
            {
                // ATTENZIONE: la header potrebbe essere "Bearer <token>"
                var authHeader = context.Request.Headers["Authorization"].ToString();
                var accessToken = authHeader.StartsWith("Bearer ") ? authHeader.Substring(7) : authHeader;

                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    try
                    {
                        var jwtToken = handler.ReadJwtToken(accessToken);

                        if (jwtToken.Payload.TryGetValue("realm_access", out var realmAccessObj)
                            && realmAccessObj is JsonElement realmAccess
                            && realmAccess.TryGetProperty("roles", out JsonElement roles))
                        {
                            foreach (var role in roles.EnumerateArray())
                            {
                                var roleName = role.GetString();
                                if (!string.IsNullOrWhiteSpace(roleName))
                                {
                                    // Aggiungi claim con lo stesso tipo definito in RoleClaimType
                                    claimsIdentity.AddClaim(new Claim("role", roleName));
                                    // oppure: claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Impossibile leggere token JWT in OnTokenValidated.");
                    }
                }
            }
            return Task.CompletedTask;
        }
    };
});

// --- Authorization (policy) ---
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManagersOnly", policy => policy.RequireRole("store-manager"));
});

// --- Kafka producer registration ---
var producerConfig = new ProducerConfig { BootstrapServers = kafkaBootstrap };
builder.Services.AddSingleton<IProducer<string, string>>(
    _ => new ProducerBuilder<string, string>(producerConfig).Build()
);

// Configurazione Serilog
builder.Services.AddHttpContextAccessor();
builder.Host.UseSerilog((context, services, logConfig) =>
{
    SerilogConfiguration.ConfigureSerilog(context, services, logConfig);
});

var app = builder.Build();

// Middleware: authentication/authorization
app.UseAuthentication();
app.UseAuthorization();

// Map endpoint protetto
app.MapPost("/api/loyalty/event", async (
    [FromBody] LoyaltyCardEvent loyaltyEvent,
    IProducer<string, string> producer) =>
{
    if (loyaltyEvent == null) return Results.BadRequest("Body mancante.");

    var eventJson = JsonSerializer.Serialize(loyaltyEvent);
    try
    {
        var dr = await producer.ProduceAsync(
            kafkaTopic,
            new Message<string, string> { Key = loyaltyEvent.IdCarta, Value = eventJson }
        );
        return Results.Ok(new { status = "Evento accodato", offset = dr.Offset.Value });
    }
    catch (ProduceException<string, string> e)
    {
        Log.Error(e, "Errore ProduceAsync per il topic {Topic}", kafkaTopic);
        return Results.Problem($"Errore durante l'invio a Kafka: {e.Error.Reason}", statusCode: 500);
    }
})
.RequireAuthorization("ManagersOnly"); // usa la policy già definita

// Healthcheck semplice (consigliato)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

// IMPORTANT: avvia l'app
await app.RunAsync();
