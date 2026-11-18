using System.Globalization;
using System.Threading.RateLimiting;
using Booking.Core;
using Booking.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace Booking.CommandApi;

public partial class CommandProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 16_384;
        });
        builder.Logging.AddJsonConsole();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        var tokenSecret = builder.Configuration["Booking:TokenSecret"] ?? throw new InvalidOperationException(ValidationConstants.Messages.TokenSecretRequired);
        builder.Services.AddSingleton(_ => new ReceiptFactory(tokenSecret));
        builder.AddBookingDataSource("booking-command");
        builder.Services.AddSingleton(services => new PostgresBookingWriter(services.GetRequiredService<NpgsqlDataSource>()));
        builder.Services.AddSingleton<IBookingWriter>(services => services.GetRequiredService<PostgresBookingWriter>());
        builder.Services.AddSingleton<IDatabaseProbe>(services => services.GetRequiredService<PostgresBookingWriter>());
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (rejection, _) =>
            {
                if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay))
                    rejection.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
            AddMinuteLimit(options, "booking", builder.Configuration.GetValue("RateLimits:BookingsPerMinute", 60));
            AddMinuteLimit(options, "receipt", builder.Configuration.GetValue("RateLimits:ReceiptsPerMinute", 60));
        });
        var app = builder.Build();
        // Validate configured services before accepting requests.
        _ = app.Services.GetRequiredService<ReceiptFactory>();
        _ = app.Services.GetRequiredService<NpgsqlDataSource>();
        app.UseExceptionHandler();
        app.UseBookingDatabaseErrors(ValidationConstants.Messages.BookingUnavailable);
        app.UseRateLimiter();
        app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/health/ready", async (IDatabaseProbe probe, CancellationToken cancellationToken) =>
        {
            await probe.CheckAsync(cancellationToken);
            return Results.Ok(new { status = "ready" });
        });
        app.MapPost("/api/bookings/receipt", (BookingRequest request, HttpContext context, ReceiptFactory receipts) =>
        {
            if (!Guid.TryParseExact(context.Request.Headers["Idempotency-Key"], "D", out var key) || key == Guid.Empty)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["idempotencyKey"] = [ValidationConstants.Messages.IdempotencyKeyRequired] });
            var errors = BookingRules.ValidateForRecovery(request);
            return errors.Count != 0 ? Results.ValidationProblem(errors) : Results.Ok(receipts.Create(key, BookingRules.Normalize(request)));
        }).RequireRateLimiting("receipt");
        app.MapPost("/api/bookings", async (BookingRequest request, HttpContext context, IBookingWriter writer,
            ReceiptFactory receipts, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParseExact(context.Request.Headers["Idempotency-Key"], "D", out var key) || key == Guid.Empty)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["idempotencyKey"] = [ValidationConstants.Messages.IdempotencyKeyRequired] });
            var errors = BookingRules.Validate(request, clock);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var booking = BookingRules.Normalize(request);
            var receipt = receipts.Create(key, booking);
            var confirmation = ReceiptFactory.Confirm(receipt, booking);
            var outcome = await writer.InsertAsync(new(receipt, booking, ReceiptFactory.HashToken(receipt.AccessToken), confirmation), cancellationToken);
            return outcome switch
            {
                InsertOutcome.Created => Results.Created($"/api/bookings/{receipt.Id}", receipt),
                InsertOutcome.Replayed => Results.Json(receipt, statusCode: 202),
                _ => Results.Problem(statusCode: 409, title: ValidationConstants.Messages.SlotUnavailable)
            };
        }).RequireRateLimiting("booking");
        app.Run();
    }

    private static void AddMinuteLimit(RateLimiterOptions options, string policy, int permits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permits);
        options.AddFixedWindowLimiter(policy, limiter =>
        {
            limiter.PermitLimit = permits;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });
    }
}
