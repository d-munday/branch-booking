using Booking.Core;
using Npgsql;

namespace Booking.CommandApi;

public sealed class PostgresBookingWriter(NpgsqlDataSource source) : IBookingWriter, IDatabaseProbe
{
    public async Task<InsertOutcome> InsertAsync(StoredBooking stored, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            INSERT INTO bookings (id, reference, branch_id, service_id, appointment_date, appointment_time,
              first_name, last_name, email, phone, access_token_hash, confirmation_subject, confirmation_body)
            VALUES (@id, @reference, @branch, @service, @date, @time, @first, @last, @email, @phone, @hash, @subject, @body)
            """);
        var booking = stored.Booking;
        command.Parameters.AddWithValue("id", stored.Receipt.Id);
        command.Parameters.AddWithValue("reference", stored.Receipt.Reference);
        command.Parameters.AddWithValue("branch", booking.BranchId);
        command.Parameters.AddWithValue("service", booking.ServiceId);
        command.Parameters.AddWithValue("date", booking.Date);
        command.Parameters.AddWithValue("time", booking.Time);
        command.Parameters.AddWithValue("first", booking.FirstName);
        command.Parameters.AddWithValue("last", booking.LastName);
        command.Parameters.AddWithValue("email", booking.Email);
        command.Parameters.AddWithValue("phone", booking.Phone);
        command.Parameters.AddWithValue("hash", stored.TokenHash);
        command.Parameters.AddWithValue("subject", stored.Confirmation.Subject);
        command.Parameters.AddWithValue("body", stored.Confirmation.Body);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return InsertOutcome.Created;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return exception.ConstraintName == "bookings_pkey" ? InsertOutcome.Replayed : InsertOutcome.SlotTaken;
        }
    }

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
    }
}
