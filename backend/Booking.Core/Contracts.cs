namespace Booking.Core;

public enum InsertOutcome { Created, Replayed, SlotTaken }

public interface IBookingWriter
{
    Task<InsertOutcome> InsertAsync(StoredBooking booking, CancellationToken cancellationToken);
}

public interface IBookingReader
{
    Task<IReadOnlyList<Branch>> BranchesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BranchService>> ServicesAsync(CancellationToken cancellationToken);
    Task<IReadOnlySet<TimeOnly>?> OccupiedAsync(string branchId, DateOnly date, CancellationToken cancellationToken);
    Task<BookingDetails?> FindAsync(Guid id, string tokenHash, CancellationToken cancellationToken);
}

public interface IDatabaseProbe
{
    Task CheckAsync(CancellationToken cancellationToken);
}
