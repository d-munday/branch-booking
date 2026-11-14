using Booking.CommandApi;
using Booking.QueryApi;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Booking.Tests;

public sealed class ServiceLifetimeTests
{
    [Fact]
    public Task Command_host_disposes_its_database_pool() => VerifyPoolDisposal<CommandProgram>();

    [Fact]
    public Task Query_host_disposes_its_database_pool() => VerifyPoolDisposal<QueryProgram>();

    private static async Task VerifyPoolDisposal<TProgram>() where TProgram : class
    {
        NpgsqlDataSource dataSource;
        await using (var factory = new ApiFactory<TProgram>())
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health/live");
            response.EnsureSuccessStatusCode();
            dataSource = factory.Services.GetRequiredService<NpgsqlDataSource>();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await using var connection = await dataSource.OpenConnectionAsync();
        });
    }
}
