using Microsoft.EntityFrameworkCore;
using Retail.IngestionService;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    //.UseWindowsService() // opzionale: solo se è un servizio Windows
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        var connectionString = configuration["ConnectionStrings:MainDbConString"];

        services.AddDbContext<MainDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddHostedService<LoyaltyDataIngestor>();

        // Logging (opzionale)
        services.AddHttpContextAccessor(); // se serve
    })
    .UseSerilog((context, services, configuration) =>
    {
        SerilogConfiguration.ConfigureSerilog(context, services, configuration);
    })
    .Build();

await host.RunAsync();
