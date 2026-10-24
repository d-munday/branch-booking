using Booking.Core;

namespace Booking.Tests;

public sealed class ReceiptFactoryTests
{
    internal const string Secret = "test-only-secret-at-least-32-characters-long";
    private static ValidBooking Booking => BookingRules.Normalize(BookingRulesTests.ValidRequest);
    [Fact]
    public void Short_signing_secret_is_rejected() => Assert.Throws<ArgumentException>(() => new ReceiptFactory("short"));

    [Fact]
    public void Retry_key_returns_the_same_unpredictable_receipt()
    {
        var factory = new ReceiptFactory(Secret);
        var key = Guid.NewGuid();
        var receipt = factory.Create(key, Booking);
        Assert.Equal(receipt, factory.Create(key, Booking));
        Assert.NotEqual(key, receipt.Id);
        Assert.Matches("^BK-[A-F0-9]{20}$", receipt.Reference);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", receipt.AccessToken);
        Assert.DoesNotContain(key.ToString(), receipt.AccessToken);
    }

    [Fact]
    public void Different_keys_or_secrets_produce_different_tokens()
    {
        var key = Guid.NewGuid();
        var first = new ReceiptFactory(Secret).Create(key, Booking);
        Assert.NotEqual(first.AccessToken, new ReceiptFactory(Secret).Create(Guid.NewGuid(), Booking).AccessToken);
        var changedSecret = new ReceiptFactory(Secret + "different").Create(key, Booking);
        Assert.NotEqual(first.AccessToken, changedSecret.AccessToken);
        Assert.NotEqual(first.Id, changedSecret.Id);
    }

    [Fact]
    public void Stored_token_hash_is_stable_and_does_not_expose_the_token()
    {
        var token = new ReceiptFactory(Secret).Create(Guid.NewGuid(), Booking).AccessToken;
        var hash = ReceiptFactory.HashToken(token);
        Assert.Matches("^[A-F0-9]{64}$", hash);
        Assert.Equal(hash, ReceiptFactory.HashToken(token));
        Assert.NotEqual(hash, ReceiptFactory.HashToken(token + "x"));
    }

    [Fact]
    public void Confirmation_contains_booking_time_reference_and_explicit_simulation()
    {
        var receipt = new ReceiptFactory(Secret).Create(Guid.NewGuid(), Booking);
        var confirmation = ReceiptFactory.Confirm(receipt, BookingRules.Normalize(BookingRulesTests.ValidRequest));
        Assert.True(confirmation.Simulated);
        Assert.Equal("email", confirmation.Channel);
        Assert.Equal("Your branch appointment is confirmed", confirmation.Subject);
        Assert.Contains("Thandi", confirmation.Body);
        Assert.Contains("26 October 2026", confirmation.Body);
        Assert.Contains("09:00", confirmation.Body);
        Assert.Contains("South African time", confirmation.Body);
        Assert.Contains(receipt.Reference, confirmation.Body);
        Assert.Contains("simulated", confirmation.Body);
    }

    [Fact]
    public void Public_booking_id_cannot_be_used_to_recover_its_private_capability()
    {
        var factory = new ReceiptFactory(Secret);
        var original = factory.Create(Guid.NewGuid(), Booking);
        var attemptedRecovery = factory.Create(original.Id, Booking);
        Assert.NotEqual(original.Id, attemptedRecovery.Id);
        Assert.NotEqual(original.AccessToken, attemptedRecovery.AccessToken);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("service")]
    [InlineData("date")]
    [InlineData("time")]
    [InlineData("firstName")]
    [InlineData("lastName")]
    [InlineData("email")]
    [InlineData("phone")]
    public void Reusing_key_with_changed_payload_cannot_mint_the_original_capability(string field)
    {
        var originalBooking = Booking;
        var changed = field switch
        {
            "branch" => originalBooking with { BranchId = "sea-point" },
            "service" => originalBooking with { ServiceId = "credit" },
            "date" => originalBooking with { Date = originalBooking.Date.AddDays(1) },
            "time" => originalBooking with { Time = originalBooking.Time.AddMinutes(30) },
            "firstName" => originalBooking with { FirstName = "Different" },
            "lastName" => originalBooking with { LastName = "Different" },
            "email" => originalBooking with { Email = "different@example.com" },
            "phone" => originalBooking with { Phone = "0831234567" },
            _ => throw new InvalidOperationException()
        };
        var factory = new ReceiptFactory(Secret);
        var key = Guid.NewGuid();
        var original = factory.Create(key, originalBooking);
        var replay = factory.Create(key, changed);
        Assert.Equal(original.Id, replay.Id);
        Assert.NotEqual(original.AccessToken, replay.AccessToken);
    }

    [Fact]
    public void Canonical_payload_has_unambiguous_field_boundaries()
    {
        var factory = new ReceiptFactory(Secret);
        var key = Guid.NewGuid();
        var first = factory.Create(key, Booking with { FirstName = "A", LastName = "BC" });
        var second = factory.Create(key, Booking with { FirstName = "AB", LastName = "C" });
        Assert.NotEqual(first.AccessToken, second.AccessToken);
    }

    [Fact]
    public void Equivalent_normalized_request_returns_same_capability()
    {
        var factory = new ReceiptFactory(Secret);
        var key = Guid.NewGuid();
        var equivalent = BookingRules.Normalize(BookingRulesTests.ValidRequest with
        { FirstName = " Thandi ", Email = "THANDI@EXAMPLE.COM", Phone = "(082) 123-4567" });
        Assert.Equal(factory.Create(key, Booking), factory.Create(key, equivalent));
    }
}
