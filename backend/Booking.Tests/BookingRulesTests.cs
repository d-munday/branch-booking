using Booking.Core;

namespace Booking.Tests;

public sealed class BookingRulesTests
{
    private static readonly TimeProvider Clock = new FrozenClock(new(2026, 10, 24, 6, 0, 0, TimeSpan.Zero));
    internal static BookingRequest ValidRequest => new("cape-town-cbd", "everyday-banking", "2026-10-26", "09:00", "Thandi", "Dlamini", "thandi@example.com", "082 123 4567", true);

    [Fact]
    public void Valid_request_has_no_errors() => Assert.Empty(BookingRules.Validate(ValidRequest, Clock));

    [Theory]
    [InlineData("date", "2026-10-23")]
    [InlineData("date", "2026-11-24")]
    [InlineData("date", "26/10/2026")]
    [InlineData("date", "2026-02-30")]
    [InlineData("date", null)]
    [InlineData("time", "08:15")]
    [InlineData("time", "16:30")]
    [InlineData("time", "07:30")]
    [InlineData("time", "09:00:00")]
    [InlineData("time", null)]
    [InlineData("firstName", "")]
    [InlineData("firstName", "  ")]
    [InlineData("firstName", "Name123")]
    [InlineData("firstName", null)]
    [InlineData("firstName", "---")]
    [InlineData("firstName", "''")]
    [InlineData("lastName", "<script>")]
    [InlineData("lastName", null)]
    [InlineData("email", "invalid")]
    [InlineData("email", "Name <name@example.com>")]
    [InlineData("email", "name@localhost")]
    [InlineData("email", "name.surname@localhost")]
    [InlineData("email", null)]
    [InlineData("phone", "123")]
    [InlineData("phone", "+1 555 123 4567")]
    [InlineData("phone", "0921234567")]
    [InlineData("phone", null)]
    [InlineData("branchId", "missing")]
    [InlineData("branchId", null)]
    [InlineData("serviceId", "missing")]
    [InlineData("serviceId", null)]
    public void Invalid_field_produces_specific_validation_error(string field, string? value)
    {
        var request = field switch
        {
            "date" => ValidRequest with { Date = value },
            "time" => ValidRequest with { Time = value },
            "firstName" => ValidRequest with { FirstName = value },
            "lastName" => ValidRequest with { LastName = value },
            "email" => ValidRequest with { Email = value },
            "phone" => ValidRequest with { Phone = value },
            "branchId" => ValidRequest with { BranchId = value },
            "serviceId" => ValidRequest with { ServiceId = value },
            _ => throw new InvalidOperationException()
        };
        Assert.Contains(field, BookingRules.Validate(request, Clock).Keys);
    }

    [Fact]
    public void Consent_is_required() => Assert.Contains("consent", BookingRules.Validate(ValidRequest with { Consent = false }, Clock));

    [Theory]
    [InlineData("Élodie", "O’Connor")]
    [InlineData("Anne-Marie", "van der Merwe")]
    [InlineData("太郎", "山田")]
    [InlineData("Élodie", "O’Connor")]
    public void Names_support_unicode_hyphens_and_apostrophes(string first, string last)
        => Assert.Empty(BookingRules.Validate(ValidRequest with { FirstName = first, LastName = last }, Clock));

    [Fact]
    public void Name_limit_applies_after_trimming()
    {
        Assert.Empty(BookingRules.Validate(ValidRequest with { FirstName = "  " + new string('a', 80) + "  " }, Clock));
        Assert.Contains("firstName", BookingRules.Validate(ValidRequest with { FirstName = new string('a', 81) }, Clock));
        Assert.Contains("lastName", BookingRules.Validate(ValidRequest with { LastName = new string('a', 81) }, Clock));
    }

    [Fact]
    public void Overlong_email_is_rejected() => Assert.Contains("email", BookingRules.Validate(ValidRequest with { Email = new string('a', 250) + "@example.com" }, Clock));

    [Theory]
    [InlineData("0821234567")]
    [InlineData("+27 82 123 4567")]
    [InlineData("(082) 123-4567")]
    public void South_African_phone_formats_are_accepted(string phone) => Assert.Empty(BookingRules.Validate(ValidRequest with { Phone = phone }, Clock));

    [Fact]
    public void Normalization_trims_contact_data_and_preserves_name_case()
    {
        var normalized = BookingRules.Normalize(ValidRequest with { FirstName = " Thandi ", LastName = " Dlamini ", Email = " THANDI@Example.COM ", Phone = " (082) 123-4567 " });
        Assert.Equal("Thandi", normalized.FirstName);
        Assert.Equal("Dlamini", normalized.LastName);
        Assert.Equal("thandi@example.com", normalized.Email);
        Assert.Equal("0821234567", normalized.Phone);
        Assert.Equal(new DateOnly(2026, 10, 26), normalized.Date);
        Assert.Equal(new TimeOnly(9, 0), normalized.Time);
    }

    [Fact]
    public void Branch_time_is_always_UTC_plus_two_even_across_midnight()
    {
        var local = BookingRules.LocalNow(new FrozenClock(new(2026, 10, 24, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.FromHours(2)), local);
        Assert.Equal(TimeSpan.FromHours(2), local.Offset);
    }

    [Theory]
    [InlineData(26, 8, 0, true)]
    [InlineData(26, 16, 0, true)]
    [InlineData(26, 16, 30, false)]
    [InlineData(24, 12, 0, true)]
    [InlineData(24, 12, 30, false)]
    [InlineData(25, 9, 0, false)]
    [InlineData(26, 7, 30, false)]
    [InlineData(26, 9, 1, false)]
    public void Slots_respect_branch_opening_hours(int day, int hour, int minute, bool expected)
        => Assert.Equal(expected, BookingRules.IsBookable(new(2026, 10, day), new(hour, minute), BookingRules.LocalNow(Clock)));

    [Fact]
    public void Booking_requires_at_least_one_hour_notice()
    {
        var now = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(2));
        Assert.True(BookingRules.IsBookable(new(2026, 10, 26), new(9, 0), now));
        Assert.False(BookingRules.IsBookable(new(2026, 10, 26), new(8, 30), now));
        Assert.False(BookingRules.IsBookable(new(2026, 10, 26), new(9, 0), now.AddSeconds(1)));
        Assert.False(BookingRules.IsBookable(new(2026, 10, 26), new(9, 0, 1), now));
    }

    [Fact]
    public void Thirty_day_horizon_is_inclusive()
    {
        var now = BookingRules.LocalNow(Clock);
        Assert.True(BookingRules.IsBookable(new(2026, 11, 23), new(9, 0), now));
        Assert.False(BookingRules.IsBookable(new(2026, 11, 24), new(9, 0), now));
        Assert.False(BookingRules.IsBookable(new(2026, 10, 23), new(9, 0), now));
    }

    [Fact]
    public void Availability_marks_taken_slots_and_excludes_closed_hours()
    {
        var slots = BookingRules.Slots(new(2026, 10, 26), BookingRules.LocalNow(Clock), new HashSet<TimeOnly> { new(9, 0) });
        Assert.Equal(17, slots.Count);
        Assert.Equal("08:00", slots.First().Time);
        Assert.Equal("16:00", slots.Last().Time);
        Assert.False(slots.Single(x => x.Time == "09:00").Available);
        Assert.True(slots.Single(x => x.Time == "09:30").Available);
    }

    [Fact]
    public void Saturday_has_nine_slots_and_Sunday_has_none()
    {
        Assert.Equal(9, BookingRules.Slots(new(2026, 10, 24), BookingRules.LocalNow(Clock), new HashSet<TimeOnly>()).Count);
        Assert.Empty(BookingRules.Slots(new(2026, 10, 25), BookingRules.LocalNow(Clock), new HashSet<TimeOnly>()));
    }

    [Fact]
    public void Elapsed_slots_are_visible_but_unavailable()
    {
        var slots = BookingRules.Slots(new(2026, 10, 24), BookingRules.LocalNow(Clock), new HashSet<TimeOnly>());
        Assert.False(slots.Single(x => x.Time == "08:30").Available);
        Assert.True(slots.Single(x => x.Time == "09:00").Available);
    }
}

internal sealed class FrozenClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
