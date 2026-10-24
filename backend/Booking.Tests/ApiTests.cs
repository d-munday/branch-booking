using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Booking.CommandApi;
using Booking.Core;
using Booking.QueryApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Booking.Tests;

public sealed class CommandApiTests
{
    [Fact]
    public async Task Created_booking_returns_receipt_and_persists_normalized_confirmation()
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();
        using var response = await Post(client, BookingRulesTests.ValidRequest with { FirstName = " Thandi " }, key.ToString());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<BookingReceipt>();
        Assert.NotNull(receipt);
        Assert.NotEqual(key, receipt.Id);
        Assert.Equal($"/api/bookings/{receipt.Id}", response.Headers.Location?.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(factory.Writer.Inserted);
        Assert.Equal("Thandi", factory.Writer.Inserted.Booking.FirstName);
        Assert.Equal(ReceiptFactory.HashToken(receipt.AccessToken), factory.Writer.Inserted.TokenHash);
        Assert.True(factory.Writer.Inserted.Confirmation.Simulated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("87cb89c966a442c68db11238c7acde07")]
    public async Task Invalid_idempotency_key_is_rejected_before_writing(string? key)
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        using var response = await Post(client, BookingRulesTests.ValidRequest, key);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("idempotencyKey", await response.Content.ReadAsStringAsync());
        Assert.Null(factory.Writer.Inserted);
    }

    [Fact]
    public async Task Invalid_booking_returns_field_errors_before_writing()
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        using var response = await Post(client, BookingRulesTests.ValidRequest with { Consent = false, Email = "wrong" }, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("consent", body);
        Assert.Contains("email", body);
        Assert.Null(factory.Writer.Inserted);
    }

    [Fact]
    public async Task Replayed_key_returns_the_same_receipt_with_accepted_status()
    {
        using var factory = new ApiFactory<CommandProgram>();
        factory.Writer.Outcome = InsertOutcome.Replayed;
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();
        using var response = await Post(client, BookingRulesTests.ValidRequest, key.ToString());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(new ReceiptFactory(ReceiptFactoryTests.Secret).Create(key, BookingRules.Normalize(BookingRulesTests.ValidRequest)), await response.Content.ReadFromJsonAsync<BookingReceipt>());
    }

    [Fact]
    public async Task Occupied_slot_returns_actionable_conflict()
    {
        using var factory = new ApiFactory<CommandProgram>();
        factory.Writer.Outcome = InsertOutcome.SlotTaken;
        using var client = factory.CreateClient();
        using var response = await Post(client, BookingRulesTests.ValidRequest, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("choose another time", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Database_failure_returns_retryable_problem_without_internal_details()
    {
        using var factory = new ApiFactory<CommandProgram>();
        factory.Writer.Exception = new NpgsqlException("sensitive database internals");
        using var client = factory.CreateClient();
        using var response = await Post(client, BookingRulesTests.ValidRequest, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_routes_are_available(string path)
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(path.EndsWith("ready", StringComparison.Ordinal), factory.Probe.Checked);
    }

    [Fact]
    public async Task Readiness_reports_database_failure()
    {
        using var factory = new ApiFactory<CommandProgram>();
        factory.Probe.Exception = new NpgsqlException("offline");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_is_rejected()
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/bookings", new StringContent("{broken", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(factory.Writer.Inserted);
    }

    [Fact]
    public async Task Command_api_does_not_expose_query_routes()
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/branches");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Receipt_recovery_after_original_booking_window_expires_does_not_access_database()
    {
        using var factory = new ApiFactory<CommandProgram>();
        factory.Writer.Exception = new InvalidOperationException("Recovery must not write.");
        factory.Reader.Exception = new InvalidOperationException("Recovery must not read.");
        factory.Probe.Exception = new InvalidOperationException("Recovery must not connect.");
        using var client = factory.CreateClient();
        var original = BookingRulesTests.ValidRequest with { Date = "2020-10-26" };
        var key = Guid.NewGuid();
        using var response = await Recover(client, original, key.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ReceiptFactory(ReceiptFactoryTests.Secret).Create(key, BookingRules.Normalize(original)), await response.Content.ReadFromJsonAsync<BookingReceipt>());
        Assert.Null(factory.Writer.Inserted);
        Assert.False(factory.Probe.Checked);
        using var create = await Post(client, original, key.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Receipt_recovery_requires_the_original_private_key(string? key)
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        using var response = await Recover(client, BookingRulesTests.ValidRequest, key);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("idempotencyKey", await response.Content.ReadAsStringAsync());
        Assert.Null(factory.Writer.Inserted);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("email")]
    [InlineData("name")]
    [InlineData("branch")]
    [InlineData("service")]
    [InlineData("phone")]
    [InlineData("consent")]
    public async Task Invalid_recovery_payload_cannot_mint_a_receipt(string field)
    {
        var original = BookingRulesTests.ValidRequest;
        var malformed = field switch
        {
            "date" => original with { Date = "not-a-date" },
            "time" => original with { Time = "08:15" },
            "email" => original with { Email = "not-an-email" },
            "name" => original with { FirstName = "---" },
            "branch" => original with { BranchId = "unknown" },
            "service" => original with { ServiceId = "unknown" },
            "phone" => original with { Phone = "123" },
            "consent" => original with { Consent = false },
            _ => throw new InvalidOperationException()
        };
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        using var response = await Recover(client, malformed, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body);
        Assert.Null(factory.Writer.Inserted);
    }

    [Fact]
    public async Task Recovery_uses_a_public_booking_id_as_a_different_key()
    {
        using var factory = new ApiFactory<CommandProgram>();
        using var client = factory.CreateClient();
        var original = new ReceiptFactory(ReceiptFactoryTests.Secret).Create(Guid.NewGuid(), BookingRules.Normalize(BookingRulesTests.ValidRequest));
        using var response = await Recover(client, BookingRulesTests.ValidRequest, original.Id.ToString());
        var forged = await response.Content.ReadFromJsonAsync<BookingReceipt>();
        Assert.NotNull(forged);
        Assert.NotEqual(original.Id, forged.Id);
        Assert.NotEqual(original.AccessToken, forged.AccessToken);
    }

    private static Task<HttpResponseMessage> Recover(HttpClient client, BookingRequest body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings/receipt") { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, BookingRequest body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }
}

public sealed class QueryApiTests
{
    [Theory]
    [InlineData("/api/branches", "Cape Town CBD")]
    [InlineData("/api/services", "Everyday banking")]
    public async Task Catalog_routes_serve_the_read_model(string path, string expected)
    {
        using var factory = new ApiFactory<QueryProgram>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("2026-10-26", 17)]
    [InlineData("2026-10-25", 0)]
    public async Task Availability_reflects_open_hours_and_reservations(string date, int expected)
    {
        using var factory = new ApiFactory<QueryProgram>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/availability?branchId=cape-town-cbd&date={date}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var availability = await response.Content.ReadFromJsonAsync<Availability>();
        Assert.NotNull(availability);
        Assert.Equal(expected, availability.Slots.Count);
        if (expected > 0) Assert.False(availability.Slots.Single(s => s.Time == "09:00").Available);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("2026-10-23")]
    [InlineData("2026-11-24")]
    public async Task Availability_rejects_invalid_or_out_of_range_dates(string date)
    {
        using var factory = new ApiFactory<QueryProgram>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/availability?branchId=cape-town-cbd&date={date}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("date", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Availability_rejects_unknown_branch()
    {
        using var factory = new ApiFactory<QueryProgram>();
        factory.Reader.Occupied = null;
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/availability?branchId=unknown&date=2026-10-26");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("branchId", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Valid_capability_returns_saved_booking_and_hashes_the_token()
    {
        using var factory = new ApiFactory<QueryProgram>();
        var receipt = new ReceiptFactory(ReceiptFactoryTests.Secret).Create(Guid.NewGuid(), BookingRules.Normalize(BookingRulesTests.ValidRequest));
        factory.Reader.Details = new(receipt.Id, receipt.Reference, "cape-town-cbd", "everyday-banking", "2026-10-26", "09:00", "Thandi", "Dlamini", "thandi@example.com", "0821234567", "confirmed", new(true, "email", "Confirmed", "Saved confirmation"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", receipt.AccessToken);
        using var response = await client.GetAsync($"/api/bookings/{receipt.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(factory.Reader.Details, await response.Content.ReadFromJsonAsync<BookingDetails>());
        Assert.Equal(ReceiptFactory.HashToken(receipt.AccessToken), factory.Reader.RequestedHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic abc")]
    [InlineData("Bearer short")]
    public async Task Invalid_or_missing_capability_is_not_found(string? authorization)
    {
        using var factory = new ApiFactory<QueryProgram>();
        using var client = factory.CreateClient();
        if (authorization is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        using var response = await client.GetAsync($"/api/bookings/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(factory.Reader.RequestedHash);
    }

    [Fact]
    public async Task Unknown_booking_does_not_leak_its_existence()
    {
        using var factory = new ApiFactory<QueryProgram>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", new string('a', 43));
        using var response = await client.GetAsync($"/api/bookings/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Database_failure_returns_retryable_problem()
    {
        using var factory = new ApiFactory<QueryProgram>();
        factory.Reader.Exception = new NpgsqlException("sensitive database internals");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/branches");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_routes_are_available(string path)
    {
        using var factory = new ApiFactory<QueryProgram>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Query_api_does_not_expose_the_write_route()
    {
        using var factory = new ApiFactory<QueryProgram>();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/bookings", BookingRulesTests.ValidRequest);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

internal sealed class ApiFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    public StubWriter Writer { get; } = new();
    public StubReader Reader { get; } = new();
    public StubProbe Probe { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Booking:TokenSecret", ReceiptFactoryTests.Secret);
        builder.UseSetting("ConnectionStrings:Database", "Host=127.0.0.1;Database=unused;Username=unused");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBookingWriter>();
            services.RemoveAll<IBookingReader>();
            services.RemoveAll<IDatabaseProbe>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<IBookingWriter>(Writer);
            services.AddSingleton<IBookingReader>(Reader);
            services.AddSingleton<IDatabaseProbe>(Probe);
            services.AddSingleton<TimeProvider>(new FrozenClock(new(2026, 10, 24, 6, 0, 0, TimeSpan.Zero)));
        });
    }
}

internal sealed class StubWriter : IBookingWriter
{
    public StoredBooking? Inserted { get; private set; }
    public InsertOutcome Outcome { get; set; } = InsertOutcome.Created;
    public Exception? Exception { get; set; }
    public Task<InsertOutcome> InsertAsync(StoredBooking booking, CancellationToken cancellationToken)
    {
        if (Exception is not null) throw Exception;
        Inserted = booking;
        return Task.FromResult(Outcome);
    }
}

internal sealed class StubReader : IBookingReader
{
    public IReadOnlySet<TimeOnly>? Occupied { get; set; } = new HashSet<TimeOnly> { new(9, 0) };
    public BookingDetails? Details { get; set; }
    public string? RequestedHash { get; private set; }
    public Exception? Exception { get; set; }
    public Task<IReadOnlyList<Branch>> BranchesAsync(CancellationToken cancellationToken)
    {
        if (Exception is not null) throw Exception;
        return Task.FromResult<IReadOnlyList<Branch>>([new("cape-town-cbd", "Cape Town CBD", "1 Main Road", "Cape Town", "Western Cape", "08:00–16:30")]);
    }
    public Task<IReadOnlyList<BranchService>> ServicesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<BranchService>>([new("everyday-banking", "Everyday banking", "Account help", 30)]);
    public Task<IReadOnlySet<TimeOnly>?> OccupiedAsync(string branchId, DateOnly date, CancellationToken cancellationToken) => Task.FromResult(Occupied);
    public Task<BookingDetails?> FindAsync(Guid id, string tokenHash, CancellationToken cancellationToken)
    {
        RequestedHash = tokenHash;
        return Task.FromResult(Details);
    }
}

internal sealed class StubProbe : IDatabaseProbe
{
    public bool Checked { get; private set; }
    public Exception? Exception { get; set; }
    public Task CheckAsync(CancellationToken cancellationToken)
    {
        if (Exception is not null) throw Exception;
        Checked = true;
        return Task.CompletedTask;
    }
}
