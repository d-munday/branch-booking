using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Booking.CommandApi;
using Booking.QueryApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Booking.Tests;

public sealed class DatabaseHostingTests
{
    [Fact]
    public Task Command_pool_honours_connection_settings() => VerifyPool<CommandProgram>();

    [Fact]
    public Task Query_pool_honours_connection_settings() => VerifyPool<QueryProgram>();

    private static async Task VerifyPool<TProgram>() where TProgram : class
    {
        using var fixture = new ApiFactory<TProgram>();
        using var host = fixture.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Database",
            "Host=localhost;Maximum Pool Size=7;Timeout=2;Command Timeout=11;Application Name=custom"));
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();
        var settings = new NpgsqlConnectionStringBuilder(host.Services.GetRequiredService<NpgsqlDataSource>().ConnectionString);
        Assert.Equal(7, settings.MaxPoolSize);
        Assert.Equal(2, settings.Timeout);
        Assert.Equal(11, settings.CommandTimeout);
        Assert.Equal("custom", settings.ApplicationName);
    }

    [Fact]
    public Task Command_permission_error_is_safely_diagnosed() =>
        VerifyPermanentFailure<CommandProgram>(PostgresErrorCodes.InsufficientPrivilege);

    [Fact]
    public Task Query_missing_table_is_safely_diagnosed() =>
        VerifyPermanentFailure<QueryProgram>(PostgresErrorCodes.UndefinedTable);

    private static async Task VerifyPermanentFailure<TProgram>(string sqlState) where TProgram : class
    {
        using var fixture = new ApiFactory<TProgram>();
        const string privateDetail = "customer@example.com";
        fixture.Probe.Exception = new PostgresException(privateDetail, "ERROR", "ERROR", sqlState);
        using var logs = new TestLog();
        using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(privateDetail, body);
        Assert.DoesNotContain(sqlState, body);
        using var problem = JsonDocument.Parse(body);
        var traceId = problem.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrEmpty(traceId));
        Assert.Contains(logs.Errors, entry => entry.Contains(sqlState) && entry.Contains(traceId));
        Assert.DoesNotContain(logs.Errors, entry => entry.Contains(privateDetail));
    }

    private sealed class TestLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            if (IsEnabled(level)) Errors.Enqueue(format(state, error) + error);
        }
    }
}
