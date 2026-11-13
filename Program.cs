using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text.Json;
using SistemaEstoqueTI.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("integration-creation", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var origins = (builder.Configuration["FrontendOrigins"] ??
               (builder.Environment.IsDevelopment() ? "http://localhost:5500" : ""))
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(origin => origin.TrimEnd('/'))
    .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == "https" || ((builder.Environment.IsDevelopment() || parsed.IsLoopback) && parsed.Scheme == "http")))
    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins(origins)
          .WithHeaders("X-API-Key", "Content-Type")
          .WithMethods("GET", "POST", "PUT", "DELETE")));

var app = builder.Build();
// O Render termina o HTTPS no proxy. Não confiar em X-Forwarded-For arbitrário.
app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});
app.UseRouting();
app.UseCors("frontend");
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next(context);
});
app.MapControllers();
app.MapHealthChecks("/health");
await DatabaseInitializer.InitializeAsync(app.Services.GetRequiredService<Db>(),
    app.Configuration, app.Environment);
app.Run();
