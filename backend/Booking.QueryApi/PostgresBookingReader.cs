using System.Globalization;
using Booking.Core;
using Npgsql;

namespace Booking.QueryApi;

public sealed class PostgresBookingReader(NpgsqlDataSource source) : IBookingReader, IDatabaseProbe
{
    public async Task<IReadOnlyList<Branch>> BranchesAsync(CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("SELECT id,name,address,city,province,hours FROM branches ORDER BY sort_order,id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var branches = new List<Branch>();
        while (await reader.ReadAsync(cancellationToken)) branches.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return branches;
    }

    public async Task<IReadOnlyList<BranchService>> ServicesAsync(CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("SELECT id,name,description,duration_minutes FROM services ORDER BY sort_order,id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var services = new List<BranchService>();
        while (await reader.ReadAsync(cancellationToken)) services.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return services;
    }

    public async Task<IReadOnlySet<TimeOnly>?> OccupiedAsync(string branchId, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT b.appointment_time FROM branches c LEFT JOIN bookings b
              ON b.branch_id=c.id AND b.appointment_date=@date WHERE c.id=@branch
            """);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("branch", branchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var times = new HashSet<TimeOnly>();
        var found = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            found = true;
            if (!reader.IsDBNull(0)) times.Add(reader.GetFieldValue<TimeOnly>(0));
        }
        return found ? times : null;
    }

    public async Task<BookingDetails?> FindAsync(Guid id, string tokenHash, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT id,reference,branch_id,service_id,appointment_date,appointment_time,first_name,last_name,
              email,phone,confirmation_subject,confirmation_body FROM bookings WHERE id=@id AND access_token_hash=@hash
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("hash", tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetFieldValue<DateOnly>(4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetFieldValue<TimeOnly>(5).ToString("HH:mm", CultureInfo.InvariantCulture), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9), "confirmed", new(true, "email", reader.GetString(10), reader.GetString(11)));
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
    }
}
