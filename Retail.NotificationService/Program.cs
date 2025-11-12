using Retail.NotificationService.Models;
using Retail.NotificationService.Services;
using Retail.NotificationService;
using Serilog;
using Retail.NotificationService.Configurations.SeriLogConfig;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // 1. Configura le impostazioni (lega appsettings a classi C#)
        services.Configure<SmtpSettings>(configuration.GetSection("SmtpSettings"));
        services.Configure<SmsSettings>(configuration.GetSection("SmsSettings"));

        // 2. Registra i servizi di invio (come Singleton o Scoped)
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<ISmsService, SmsService>();

        // 3. Registra il repository FITTIZIO dei clienti
        services.AddSingleton<ICustomerRepository, MockCustomerRepository>();

        // 4. Configura HttpClientFactory per il servizio SMS
        // Questo gestisce il ciclo di vita degli HttpClient in modo efficiente
        services.AddHttpClient("SmsProvider");

        // 5. Registra il worker principale (il consumer Kafka)
        services.AddHostedService<NotificationWorker>();

        // Logging 
        services.AddHttpContextAccessor();
    })
    .UseSerilog((context, services, configuration) =>
    {
        SerilogConfiguration.ConfigureSerilog(context, services, configuration);
    })
    .Build();

await host.RunAsync();