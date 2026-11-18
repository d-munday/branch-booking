using Booking.Core;
using Npgsql;

namespace Booking.Hosting;

internal static class DatabaseHosting
{
    public static void AddBookingDataSource(this WebApplicationBuilder builder, string applicationName)
    {
        var configured = builder.Configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(ValidationConstants.Messages.DatabaseConnectionRequired);
        var defaults = new NpgsqlConnectionStringBuilder
        {
            ApplicationName = applicationName, Timeout = 5, CommandTimeout = 5, MaxPoolSize = 50
        };
        var settings = new NpgsqlConnectionStringBuilder($"{defaults.ConnectionString};{configured}");
        builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(settings.ConnectionString));
    }

    public static void UseBookingDatabaseErrors(this WebApplication app, string unavailableTitle)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                await next(context);
            }
            catch (NpgsqlException error) when (!context.Response.HasStarted)
            {
                var transient = error.IsTransient;
                app.Logger.Log(transient ? LogLevel.Warning : LogLevel.Error,
                    "Database request failed. TraceId {TraceId}, SQLSTATE {SqlState}, transient {IsTransient}",
                    context.TraceIdentifier, error.SqlState, transient);
                await Results.Problem(
                    statusCode: transient ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError,
                    title: transient ? unavailableTitle : null).ExecuteAsync(context);
            }
        });
    }
}
