using Retail.FraudDetector;
using Serilog;
using StackExchange.Redis;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
       
        // 1. Configura la connessione a Redis
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        // Configura e registra il ConnectionMultiplexer di Redis
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));

        // 2. Registra il nostro store per la logica antifrode
        services.AddSingleton<FraudDetectionStore>();

        // 3. Registra il worker principale
        services.AddHostedService<FraudDetectorWorker>();

        services.AddHttpContextAccessor(); 
    })
    .UseSerilog((context, services, loggerConfig) =>
    {
        // Qui puoi replicare la configurazione di Serilog degli altri progetti
        // Per semplicità, configuriamo solo la console
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .WriteTo.Console();
    })
    .Build();

await host.RunAsync();