using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Formatting.Compact;
using Serilog.Sinks.MSSqlServer;

namespace Retail.CouponEngine
{
    public class SerilogConfiguration
    {
        public static void ConfigureSerilog(HostBuilderContext context, 
            IServiceProvider services, 
            LoggerConfiguration configuration)
        {
            var httpContextAccessor = services.GetRequiredService<IHttpContextAccessor>();
            var httpContextEnricher = new HttpContextEnricher(httpContextAccessor);
            var connectionStringLogs = context.Configuration.GetConnectionString("LogsDbConString");

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProcessId()
                .Enrich.WithEnvironmentName()
                .Enrich.WithExceptionDetails()
                .Enrich.With(httpContextEnricher)
                .WriteTo.Console(
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code
                )
                .WriteTo.File(
                    "logs/log-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/log-.json",
                    rollingInterval: RollingInterval.Day
                )
                .WriteTo.MSSqlServer(
                    connectionString: connectionStringLogs,
                    sinkOptions: new MSSqlServerSinkOptions
                    {
                        TableName = "LogsCouponEngine",
                        SchemaName = "dbo",
                        AutoCreateSqlTable = true
                    },
                    columnOptions: null,
                    restrictedToMinimumLevel: LogEventLevel.Information
                );
        }
    }
}
