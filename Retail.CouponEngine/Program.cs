using Confluent.Kafka;
using Retail.CouponEngine;
using Serilog;
using StackExchange.Redis;

// Configura l'host generico
IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // Registra il ConnectionMultiplexer di Redis come Singleton
        var redisConnectionString = hostContext.Configuration.GetConnectionString("Redis") ?? "";

        // Configura e registra il ConnectionMultiplexer di Redis
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString)
        );

        // Registra il nostro nuovo State Store
        services.AddSingleton<RedisStateStore>();

        // --- INIZIO MODIFICA: Registra il Producer Kafka ---
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = hostContext.Configuration["Kafka:BootstrapServers"]
        };

        // Registra il Producer Kafka come Singleton
        services.AddSingleton<IProducer<string, string>>(
            _ => new ProducerBuilder<string, string>(producerConfig).Build()
        );
        // --- FINE MODIFICA ---

        // Registra il Coupon Engine Worker come Hosted Service
        services.AddHostedService<CouponEngineWorker>();

        // Logging 
        services.AddHttpContextAccessor();
    })
    .UseSerilog((context, services, configuration) =>
    {
        SerilogConfiguration.ConfigureSerilog(context, services, configuration);
    })
    .Build();

await host.RunAsync();