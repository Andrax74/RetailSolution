using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace Retail.Api.KafkaProducer.Configurations.SeriLogConfig
{
    public class HttpContextEnricher : ILogEventEnricher
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpContextEnricher(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var context = _accessor.HttpContext;
            if (context == null) return;

            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Username", context.User.Identity?.Name ?? "anonymous"));
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Path", context.Request.Path));
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("IP", context.Connection.RemoteIpAddress?.ToString()));
        }
    }
}
