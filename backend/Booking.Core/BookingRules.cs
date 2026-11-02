using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Booking.Core;

public static partial class BookingRules
{
    public static DateTimeOffset LocalNow(TimeProvider clock) => clock.GetUtcNow().ToOffset(TimeSpan.FromHours(2));

    public static Dictionary<string, string[]> Validate(BookingRequest request, TimeProvider clock) => Validate(request, clock, enforceWindow: true);

    public static Dictionary<string, string[]> ValidateForRecovery(BookingRequest request) => Validate(request, TimeProvider.System, enforceWindow: false);

    private static Dictionary<string, string[]> Validate(BookingRequest request, TimeProvider clock, bool enforceWindow)
    {
        var errors = new Dictionary<string, string[]>();
        void Reject(string key, string message) => errors[key] = [message];
        if (!Catalog.BranchIds.Contains(request.BranchId ?? "")) Reject("branchId", ValidationConstants.Messages.InvalidBranch);
        if (!Catalog.ServiceIds.Contains(request.ServiceId ?? "")) Reject("serviceId", ValidationConstants.Messages.InvalidService);
        var validDate = DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
        var validTime = TimeOnly.TryParseExact(request.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
        var now = LocalNow(clock);
        if (!validDate || (enforceWindow && (date < DateOnly.FromDateTime(now.DateTime) || date > DateOnly.FromDateTime(now.DateTime).AddDays(30))))
            Reject("date", ValidationConstants.Messages.InvalidDate);
        if (!validTime || (validDate && (enforceWindow ? !IsBookable(date, time, now) : !IsOpenTime(date, time))))
            Reject("time", enforceWindow ? ValidationConstants.Messages.InvalidTime : ValidationConstants.Messages.OriginalTimeRequired);
        if (!ValidName(request.FirstName)) Reject("firstName", ValidationConstants.Messages.InvalidFirstName);
        if (!ValidName(request.LastName)) Reject("lastName", ValidationConstants.Messages.InvalidLastName);
        var email = request.Email?.Trim();
        if (email is null || email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !parsed.Host.Contains('.'))
            Reject("email", ValidationConstants.Messages.InvalidEmail);
        if (!PhonePattern().IsMatch(Regex.Replace(request.Phone?.Trim() ?? "", ValidationConstants.Patterns.PhoneFormatting, ""))) Reject("phone", ValidationConstants.Messages.InvalidPhone);
        if (!request.Consent) Reject("consent", ValidationConstants.Messages.ConsentRequired);
        return errors;
    }

    public static ValidBooking Normalize(BookingRequest request) => new(request.BranchId!, request.ServiceId!,
        DateOnly.ParseExact(request.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(request.Time!, "HH:mm", CultureInfo.InvariantCulture),
        request.FirstName!.Trim(), request.LastName!.Trim(), request.Email!.Trim().ToLowerInvariant(),
        Regex.Replace(request.Phone!, ValidationConstants.Patterns.PhoneFormatting, ""));

    public static bool IsBookable(DateOnly date, TimeOnly time, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(2)).DateTime);
        return date >= today && date <= today.AddDays(30) && IsOpenTime(date, time) &&
            new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(2)) >= now.AddHours(1);
    }

    public static IReadOnlyList<Slot> Slots(DateOnly date, DateTimeOffset now, IReadOnlySet<TimeOnly> occupied)
    {
        if (date.DayOfWeek == DayOfWeek.Sunday) return [];
        var result = new List<Slot>();
        for (var time = new TimeOnly(8, 0); time < ClosingTime(date); time = time.AddMinutes(30))
            result.Add(new Slot(time.ToString("HH:mm", CultureInfo.InvariantCulture), IsBookable(date, time, now) && !occupied.Contains(time)));
        return result;
    }

    private static TimeOnly ClosingTime(DateOnly date) => date.DayOfWeek == DayOfWeek.Saturday ? new(12, 30) : new(16, 30);
    private static bool IsOpenTime(DateOnly date, TimeOnly time) => date.DayOfWeek != DayOfWeek.Sunday
        && time >= new TimeOnly(8, 0) && time < ClosingTime(date) && time.Minute % 30 == 0 && time.Second == 0;
    private static bool ValidName(string? value) => value?.Trim() is { Length: > 0 and <= 80 } name && name.Any(char.IsLetter)
        && name.All(c => char.IsLetter(c) || char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c is ' ' or '\'' or '-' or '’');
    [GeneratedRegex(ValidationConstants.Patterns.SouthAfricanPhone, RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
