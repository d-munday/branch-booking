using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Booking.Core;

public sealed class ReceiptFactory
{
    private readonly byte[] secret;
    public ReceiptFactory(string tokenSecret)
    {
        if (tokenSecret.Length < 32) throw new ArgumentException(ValidationConstants.Messages.TokenSecretTooShort, nameof(tokenSecret));
        secret = Encoding.UTF8.GetBytes(tokenSecret);
    }

    public BookingReceipt Create(Guid key, ValidBooking booking)
    {
        var identity = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"booking-id:v1:{key:D}"));
        identity[6] = (byte)((identity[6] & 0x0f) | 0x80);
        identity[8] = (byte)((identity[8] & 0x3f) | 0x80);
        var id = new Guid(identity.AsSpan(0, 16), bigEndian: true);

        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("booking-capability:v1");
            writer.Write(key.ToString("D"));
            writer.Write(booking.BranchId);
            writer.Write(booking.ServiceId);
            writer.Write(booking.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.Write(booking.Time.ToString("HH:mm", CultureInfo.InvariantCulture));
            writer.Write(booking.FirstName);
            writer.Write(booking.LastName);
            writer.Write(booking.Email);
            writer.Write(booking.Phone);
        }
        var token = Convert.ToBase64String(HMACSHA256.HashData(secret, payload.ToArray()))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new BookingReceipt(id, token, $"BK-{id:N}"[..23].ToUpperInvariant());
    }

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static Confirmation Confirm(BookingReceipt receipt, ValidBooking booking) => new(true, "email", "Your branch appointment is confirmed",
        $"Hi {booking.FirstName}, your appointment is confirmed for {booking.Date.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("en-ZA"))} at {booking.Time:HH:mm} (South African time). Your reference is {receipt.Reference}. Please arrive 5 minutes early and bring your ID. This is a simulated email; no message has been sent.");
}
