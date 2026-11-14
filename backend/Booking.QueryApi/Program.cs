using System.Globalization;
using Booking.Core;
using Npgsql;

namespace Booking.QueryApi;

public partial class QueryProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
        builder.Logging.AddJsonConsole();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        var connection = new NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException(ValidationConstants.Messages.DatabaseConnectionRequired))
        { Timeout = 5, CommandTimeout = 5, MaxPoolSize = 50, ApplicationName = "booking-query" };
        builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(connection.ConnectionString));
        builder.Services.AddSingleton(services => new PostgresBookingReader(services.GetRequiredService<NpgsqlDataSource>()));
        builder.Services.AddSingleton<IBookingReader>(services => services.GetRequiredService<PostgresBookingReader>());
        builder.Services.AddSingleton<IDatabaseProbe>(services => services.GetRequiredService<PostgresBookingReader>());
        var app = builder.Build();
        // Validate configured services before accepting requests.
        _ = app.Services.GetRequiredService<NpgsqlDataSource>();
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { await next(context); }
            catch (NpgsqlException)
            {
                context.Response.StatusCode = 503;
                await Results.Problem(statusCode: 503, title: ValidationConstants.Messages.AppointmentsUnavailable).ExecuteAsync(context);
            }
        });
        app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/health/ready", async (IDatabaseProbe probe, CancellationToken cancellationToken) =>
        {
            await probe.CheckAsync(cancellationToken);
            return Results.Ok(new { status = "ready" });
        });
        app.MapGet("/api/branches", async (IBookingReader reader, CancellationToken cancellationToken) => Results.Ok(await reader.BranchesAsync(cancellationToken)));
        app.MapGet("/api/services", async (IBookingReader reader, CancellationToken cancellationToken) => Results.Ok(await reader.ServicesAsync(cancellationToken)));
        app.MapGet("/api/availability", async (string branchId, string date, IBookingReader reader, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var now = BookingRules.LocalNow(clock);
            var today = DateOnly.FromDateTime(now.DateTime);
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || day < today || day > today.AddDays(30))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = [ValidationConstants.Messages.InvalidDate] });
            var occupied = await reader.OccupiedAsync(branchId, day, cancellationToken);
            return occupied is null
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["branchId"] = [ValidationConstants.Messages.InvalidBranch] })
                : Results.Ok(new Availability(date, BookingRules.Slots(day, now, occupied)));
        });
        app.MapGet("/api/bookings/{id:guid}", async (Guid id, HttpContext context, IBookingReader reader, CancellationToken cancellationToken) =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Length != 50)
                return Results.NotFound();
            var booking = await reader.FindAsync(id, ReceiptFactory.HashToken(authorization[7..]), cancellationToken);
            return booking is null ? Results.NotFound() : Results.Ok(booking);
        });
        app.Run();
    }
}
