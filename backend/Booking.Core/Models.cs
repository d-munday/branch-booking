namespace Booking.Core;

public sealed record Branch(string Id, string Name, string Address, string City, string Province, string Hours);
public sealed record BranchService(string Id, string Name, string Description, int DurationMinutes);
public sealed record BookingRequest(string? BranchId, string? ServiceId, string? Date, string? Time,
    string? FirstName, string? LastName, string? Email, string? Phone, bool Consent);
public sealed record BookingReceipt(Guid Id, string AccessToken, string Reference);
public sealed record Confirmation(bool Simulated, string Channel, string Subject, string Body);
public sealed record BookingDetails(Guid Id, string Reference, string BranchId, string ServiceId, string Date,
    string Time, string FirstName, string LastName, string Email, string Phone, string Status, Confirmation Confirmation);
public sealed record Availability(string Date, IReadOnlyList<Slot> Slots);
public sealed record Slot(string Time, bool Available);
public sealed record ValidBooking(string BranchId, string ServiceId, DateOnly Date, TimeOnly Time,
    string FirstName, string LastName, string Email, string Phone);
public sealed record StoredBooking(BookingReceipt Receipt, ValidBooking Booking, string TokenHash, Confirmation Confirmation);

public static class Catalog
{
    public static readonly IReadOnlySet<string> BranchIds = new HashSet<string>(StringComparer.Ordinal)
    { "cape-town-cbd", "sea-point", "claremont", "bellville", "sandton", "rosebank", "pretoria-central", "durban-central" };
    public static readonly IReadOnlySet<string> ServiceIds = new HashSet<string>(StringComparer.Ordinal)
    { "everyday-banking", "open-account", "credit", "savings", "card-help", "business-banking" };
}
