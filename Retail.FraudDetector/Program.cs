using Confluent.Kafka;
using Retail.FraudDetector;
using Serilog;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Redis
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

        // Store
        services.AddSingleton<FraudDetectionStore>();

        // Producer
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"]
            // aggiungi altre opzioni se servono (acks, linger.ms, retries, ...)
        };
        services.AddSingleton<IProducer<string, string>>(
            _ => new ProducerBuilder<string, string>(producerConfig).Build()
        );

        // Consumer 
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            GroupId = configuration["Kafka:GroupId"] ?? "fraud-detector-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // se fai commit manuale nel worker, disabilita l'auto-commit:
            EnableAutoCommit = false,
            // eventualmente altre opzioni come MaxPollIntervalMs, SessionTimeoutMs, etc.
        };

        services.AddSingleton<IConsumer<string, string>>(_ =>
        {
            var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();

            // Se preferisci sottoscrivere qui (invece che nel worker), abilita:
            // consumer.Subscribe(configuration["Kafka:TopicName"]);
            return consumer;
        });

        // Hosted worker (inietterà IConsumer e IProducer)
        services.AddHostedService<FraudDetectorWorker>();

        services.AddHttpContextAccessor();
    })
    .UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .WriteTo.Console();
    })
    .Build();

await host.RunAsync();
