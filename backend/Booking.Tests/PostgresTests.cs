using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Booking.CommandApi;
using Booking.Core;
using Booking.QueryApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Booking.Tests;

[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class PostgresTests(PostgresFixture database)
{
    [Fact]
    public async Task Insert_and_read_preserve_booking_and_durable_simulated_confirmation()
    {
        var stored = database.NextBooking();
        Assert.Equal(InsertOutcome.Created, await database.Writer.InsertAsync(stored, CancellationToken.None));
        var found = await database.Reader.FindAsync(stored.Receipt.Id, stored.TokenHash, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(stored.Receipt.Reference, found.Reference);
        Assert.Equal(stored.Booking.BranchId, found.BranchId);
        Assert.Equal(stored.Booking.ServiceId, found.ServiceId);
        Assert.Equal(stored.Booking.Date.ToString("yyyy-MM-dd"), found.Date);
        Assert.Equal(stored.Booking.Time.ToString("HH:mm"), found.Time);
        Assert.Equal(stored.Booking.FirstName, found.FirstName);
        Assert.Equal(stored.Booking.LastName, found.LastName);
        Assert.Equal(stored.Booking.Email, found.Email);
        Assert.Equal(stored.Booking.Phone, found.Phone);
        Assert.Equal(stored.Confirmation, found.Confirmation);
        Assert.Equal("confirmed", found.Status);
    }

    [Fact]
    public async Task Idempotent_replay_does_not_allocate_another_slot_or_change_customer_details()
    {
        var stored = database.NextBooking();
        Assert.Equal(InsertOutcome.Created, await database.Writer.InsertAsync(stored, CancellationToken.None));
        Assert.Equal(InsertOutcome.Replayed, await database.Writer.InsertAsync(stored, CancellationToken.None));
        var changed = stored with { Booking = stored.Booking with { Time = new(10, 0), FirstName = "Changed" } };
        Assert.Equal(InsertOutcome.Replayed, await database.Writer.InsertAsync(changed, CancellationToken.None));
        var found = await database.Reader.FindAsync(stored.Receipt.Id, stored.TokenHash, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(stored.Booking.FirstName, found.FirstName);
        Assert.Equal("08:00", found.Time);
        var occupied = await database.Reader.OccupiedAsync(stored.Booking.BranchId, stored.Booking.Date, CancellationToken.None);
        Assert.NotNull(occupied);
        Assert.Single(occupied);
    }

    [Fact]
    public async Task Concurrent_replicas_cannot_double_book_the_same_branch_and_time()
    {
        var first = database.NextBooking();
        var contenders = Enumerable.Range(0, 16).Select(_ => database.SameSlot(first)).ToArray();
        var outcomes = await Task.WhenAll(contenders.Select(booking => database.Writer.InsertAsync(booking, CancellationToken.None)));
        Assert.Single(outcomes, outcome => outcome == InsertOutcome.Created);
        Assert.Equal(15, outcomes.Count(outcome => outcome == InsertOutcome.SlotTaken));
        var occupied = await database.Reader.OccupiedAsync(first.Booking.BranchId, first.Booking.Date, CancellationToken.None);
        Assert.NotNull(occupied);
        Assert.Single(occupied);
    }

    [Fact]
    public async Task Missing_or_incorrect_access_token_never_returns_customer_data()
    {
        var stored = database.NextBooking();
        await database.Writer.InsertAsync(stored, CancellationToken.None);
        Assert.Null(await database.Reader.FindAsync(stored.Receipt.Id, ReceiptFactory.HashToken("incorrect"), CancellationToken.None));
        Assert.Null(await database.Reader.FindAsync(Guid.NewGuid(), stored.TokenHash, CancellationToken.None));
    }

    [Fact]
    public async Task Known_public_id_cannot_reissue_a_customers_private_capability()
    {
        var original = database.NextBooking();
        await database.Writer.InsertAsync(original, CancellationToken.None);
        var forged = new ReceiptFactory(ReceiptFactoryTests.Secret).Create(original.Receipt.Id, original.Booking);
        Assert.NotEqual(original.Receipt.Id, forged.Id);
        Assert.Null(await database.Reader.FindAsync(original.Receipt.Id, ReceiptFactory.HashToken(forged.AccessToken), CancellationToken.None));
        Assert.NotNull(await database.Reader.FindAsync(original.Receipt.Id, original.TokenHash, CancellationToken.None));
    }

    [Fact]
    public async Task Changed_payload_replay_cannot_read_or_replace_original_customer_data()
    {
        var key = Guid.NewGuid();
        var original = database.NextBooking(key);
        await database.Writer.InsertAsync(original, CancellationToken.None);
        var changed = original.Booking with { FirstName = "Attacker", Email = "attacker@example.com" };
        var receipt = new ReceiptFactory(ReceiptFactoryTests.Secret).Create(key, changed);
        var attemptedReplay = new StoredBooking(receipt, changed, ReceiptFactory.HashToken(receipt.AccessToken), ReceiptFactory.Confirm(receipt, changed));
        Assert.Equal(InsertOutcome.Replayed, await database.Writer.InsertAsync(attemptedReplay, CancellationToken.None));
        Assert.Null(await database.Reader.FindAsync(original.Receipt.Id, attemptedReplay.TokenHash, CancellationToken.None));
        var saved = await database.Reader.FindAsync(original.Receipt.Id, original.TokenHash, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(original.Booking.FirstName, saved.FirstName);
        Assert.Equal(original.Booking.Email, saved.Email);
    }

    [Fact]
    public async Task Http_receipt_reissue_attacks_cannot_expose_the_original_postgres_booking()
    {
        var key = Guid.NewGuid();
        var expected = database.NextBooking(key);
        var body = BookingRulesTests.ValidRequest with { Date = expected.Booking.Date.ToString("yyyy-MM-dd"), Time = "08:00" };
        using var commandBase = new ApiFactory<CommandProgram>();
        using var commandHost = commandBase.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBookingWriter>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<IBookingWriter>(database.Writer);
            services.AddSingleton<TimeProvider>(new FrozenClock(new DateTimeOffset(expected.Booking.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(2))));
        }));
        using var queryBase = new ApiFactory<QueryProgram>();
        using var queryHost = queryBase.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBookingReader>();
            services.AddSingleton<IBookingReader>(database.Reader);
        }));
        using var command = commandHost.CreateClient();
        using var query = queryHost.CreateClient();

        async Task<HttpResponseMessage> Post(string path, BookingRequest request, Guid privateKey)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request) };
            message.Headers.Add("Idempotency-Key", privateKey.ToString());
            return await command.SendAsync(message);
        }
        async Task<HttpStatusCode> ReadOriginal(string token)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"/api/bookings/{expected.Receipt.Id}");
            message.Headers.Authorization = new("Bearer", token);
            using var response = await query.SendAsync(message);
            return response.StatusCode;
        }

        using var created = await Post("/api/bookings", body, key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.Content.ReadFromJsonAsync<BookingReceipt>();
        Assert.NotNull(original);
        Assert.Equal(expected.Receipt, original);

        using var changedReplay = await Post("/api/bookings", body with { Email = "attacker@example.com" }, key);
        Assert.Equal(HttpStatusCode.Accepted, changedReplay.StatusCode);
        var changedReceipt = await changedReplay.Content.ReadFromJsonAsync<BookingReceipt>();
        Assert.NotNull(changedReceipt);
        Assert.Equal(HttpStatusCode.NotFound, await ReadOriginal(changedReceipt.AccessToken));

        using var knownIdAttack = await Post("/api/bookings/receipt", body, original.Id);
        Assert.Equal(HttpStatusCode.OK, knownIdAttack.StatusCode);
        var forged = await knownIdAttack.Content.ReadFromJsonAsync<BookingReceipt>();
        Assert.NotNull(forged);
        Assert.NotEqual(original.Id, forged.Id);
        Assert.Equal(HttpStatusCode.NotFound, await ReadOriginal(forged.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await ReadOriginal(original.AccessToken));
    }

    [Fact]
    public async Task Availability_distinguishes_empty_branch_unknown_branch_and_reserved_slot()
    {
        var stored = database.NextBooking();
        var empty = await database.Reader.OccupiedAsync(stored.Booking.BranchId, stored.Booking.Date, CancellationToken.None);
        Assert.NotNull(empty);
        Assert.Empty(empty);
        Assert.Null(await database.Reader.OccupiedAsync("unknown-branch", stored.Booking.Date, CancellationToken.None));
        await database.Writer.InsertAsync(stored, CancellationToken.None);
        var occupied = await database.Reader.OccupiedAsync(stored.Booking.BranchId, stored.Booking.Date, CancellationToken.None);
        Assert.NotNull(occupied);
        Assert.Contains(new TimeOnly(8, 0), occupied);
    }

    [Fact]
    public async Task Catalog_is_seeded_with_all_supported_branches_and_services()
    {
        var branches = await database.Reader.BranchesAsync(CancellationToken.None);
        var services = await database.Reader.ServicesAsync(CancellationToken.None);
        Assert.Equal(Catalog.BranchIds.Order(), branches.Select(branch => branch.Id).Order());
        Assert.Equal(Catalog.ServiceIds.Order(), services.Select(service => service.Id).Order());
        Assert.All(branches, branch => Assert.False(string.IsNullOrWhiteSpace(branch.Address)));
        Assert.All(services, service => Assert.Equal(30, service.DurationMinutes));
        Assert.Equal("cape-town-cbd", branches.First().Id);
        Assert.Equal("everyday-banking", services.First().Id);
    }

    [Theory]
    [InlineData("SELECT * FROM bookings")]
    [InlineData("SELECT * FROM branches")]
    [InlineData("SELECT * FROM services")]
    [InlineData("UPDATE bookings SET first_name = 'tampered'")]
    [InlineData("DELETE FROM bookings")]
    public async Task Writer_database_role_cannot_read_or_mutate_existing_data(string sql)
    {
        await using var command = database.CommandSource.CreateCommand(sql);
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Theory]
    [InlineData("UPDATE bookings SET first_name = 'tampered'")]
    [InlineData("DELETE FROM bookings")]
    [InlineData("INSERT INTO branches VALUES ('bad','bad','bad','bad','bad','bad',99)")]
    public async Task Reader_database_role_cannot_write(string sql)
    {
        await using var command = database.QuerySource.CreateCommand(sql);
        var error = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.ReadOnlySqlTransaction });
    }

    [Fact]
    public async Task Unknown_catalog_entry_is_rejected_by_database_constraint()
    {
        var stored = database.NextBooking();
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.Writer.InsertAsync(stored with { Booking = stored.Booking with { BranchId = "unknown" } }, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData(7, 30)]
    [InlineData(8, 15)]
    [InlineData(16, 30)]
    public async Task Invalid_slot_is_rejected_even_when_application_validation_is_bypassed(int hour, int minute)
    {
        var stored = database.NextBooking();
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.Writer.InsertAsync(stored with { Booking = stored.Booking with { Time = new(hour, minute) } }, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Both_role_connections_pass_non_query_readiness_checks()
    {
        await database.Writer.CheckAsync(CancellationToken.None);
        await database.Reader.CheckAsync(CancellationToken.None);
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly ConcurrentBag<Guid> ids = [];
    private int sequence;
    private NpgsqlDataSource? admin;
    public NpgsqlDataSource CommandSource { get; private set; } = null!;
    public NpgsqlDataSource QuerySource { get; private set; } = null!;
    public PostgresBookingWriter Writer { get; private set; } = null!;
    public PostgresBookingReader Reader { get; private set; } = null!;

    public Task InitializeAsync()
    {
        static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Set {name} to a dedicated PostgreSQL test database. For unit tests only use --filter Category!=Integration.");
        admin = NpgsqlDataSource.Create(Required("TEST_DATABASE_CONNECTION"));
        CommandSource = NpgsqlDataSource.Create(Required("TEST_COMMAND_CONNECTION"));
        QuerySource = NpgsqlDataSource.Create(Required("TEST_QUERY_CONNECTION"));
        Writer = new(CommandSource);
        Reader = new(QuerySource);
        return Task.CompletedTask;
    }

    public StoredBooking NextBooking(Guid? key = null)
    {
        var date = new DateOnly(2040, 1, 2).AddDays(Interlocked.Increment(ref sequence) * 7);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        var booking = BookingRules.Normalize(BookingRulesTests.ValidRequest) with { Date = date, Time = new(8, 0) };
        var receipt = new ReceiptFactory(ReceiptFactoryTests.Secret).Create(key ?? Guid.NewGuid(), booking);
        ids.Add(receipt.Id);
        return new(receipt, booking, ReceiptFactory.HashToken(receipt.AccessToken), ReceiptFactory.Confirm(receipt, booking));
    }

    public StoredBooking SameSlot(StoredBooking stored)
    {
        var receipt = new ReceiptFactory(ReceiptFactoryTests.Secret).Create(Guid.NewGuid(), stored.Booking);
        ids.Add(receipt.Id);
        return new(receipt, stored.Booking, ReceiptFactory.HashToken(receipt.AccessToken), ReceiptFactory.Confirm(receipt, stored.Booking));
    }

    public async Task DisposeAsync()
    {
        if (admin is not null)
        {
            await using var command = admin.CreateCommand("DELETE FROM bookings WHERE id = ANY(@ids)");
            command.Parameters.AddWithValue("ids", ids.ToArray());
            await command.ExecuteNonQueryAsync();
            await admin.DisposeAsync();
        }
        if (CommandSource is not null) await CommandSource.DisposeAsync();
        if (QuerySource is not null) await QuerySource.DisposeAsync();
    }
}
