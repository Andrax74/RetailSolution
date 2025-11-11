using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Core;
using System.Security.Claims;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configurazione Keycloak
var keycloakSettings = builder.Configuration.GetSection("Keycloak");

// --- Inizio Configurazione Autenticazione ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = keycloakSettings["Authority"];
    options.Audience = keycloakSettings["Audience"];
    options.RequireHttpsMetadata = Convert.ToBoolean(keycloakSettings["RequireHttpsMetadata"]);

    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        NameClaimType = "name",
        RoleClaimType = "role",
        ValidAudience = keycloakSettings["Audience"]
    };

    // FIX: parsing manuale del JWT per estrarre i ruoli da realm_access.roles
    // https://stackoverflow.com/questions/52481245/asp-net-core-jwt-bearer-authentication-and-custom-claims
    /* blocco di codice personalizzato (OnTokenValidated) che estrae 
     * manualmente i ruoli dal claim realm_access del token JWT standard di Keycloak e 
     * li aggiunge come ClaimTypes.Role all'identità .NET. */
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context =>
        {
            var claimsIdentity = context.Principal?.Identity as ClaimsIdentity;

            if (claimsIdentity != null)
            {
                var accessToken = context.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(accessToken);

                if (jwtToken.Payload.TryGetValue("realm_access", out var realmAccessObj) &&
                    realmAccessObj is JsonElement realmAccess &&
                    realmAccess.TryGetProperty("roles", out JsonElement roles))
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        claimsIdentity.AddClaim(new Claim("role", role.GetString() ?? ""));
                    }
                }
            }

            return Task.CompletedTask;
        }
    };
});
// --- Fine Configurazione Autenticazione ---

// --- Inizio Configurazione Autorizzazione ---
// Aggiunta di una policy di autorizzazione personalizzata
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManagersOnly", policy =>
    policy.RequireRole("store-manager"));
});
// --- Fine Configurazione Autorizzazione ---

// Configurazione Kafka
// Lettura delle impostazioni Kafka dal file di configurazione
var configuration = builder.Configuration;
var producerConfig = new ProducerConfig
{
    BootstrapServers = configuration["Kafka:BootstrapServers"]
};

// Registrazione del producer Kafka come servizio singleton
builder.Services.AddSingleton<IProducer<string, string>>(
_ => new ProducerBuilder<string, string>(producerConfig).Build()
);

var app = builder.Build();

// Middleware
app.UseAuthentication();
app.UseAuthorization();

string topicName = configuration["Kafka:TopicName"] ?? "";

// Endpoint protetto per la produzione di messaggi Kafka
app.MapPost("api/loyalty/event", async (
[FromBody] LoyaltyCardEvent loyaltyEvent,
IProducer<string, string> producer) =>
{
    // Serializzazione dell'evento in JSON
    var eventJson = JsonSerializer.Serialize(loyaltyEvent);

    try
    {
        // Invio del messaggio a Kafka
        var deliveryResult = await producer.ProduceAsync(
            topicName,
            new Message<string, string> { Key = loyaltyEvent.IdCarta, Value = eventJson }
        );

        // Risposta di successo
        return Results.Ok(new { status = "Evento accodato", offset = deliveryResult.Offset.Value });
    }
    catch (ProduceException<string, string> e)
    {
        // Gestione degli errori di produzione
        return Results.Problem($"Errore durante l'invio a Kafka: {e.Error.Reason}", statusCode: 500);
    }
}) 
.RequireAuthorization(new AuthorizeAttribute { Roles = "store-manager" }); 