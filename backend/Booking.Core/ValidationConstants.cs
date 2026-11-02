namespace Booking.Core;

public static class ValidationConstants
{
    public static class Messages
    {
        public const string InvalidBranch = "Choose a valid branch.";
        public const string InvalidService = "Choose a valid service.";
        public const string InvalidDate = "Choose a date within the next 30 days.";
        public const string InvalidTime = "Choose an open time at least one hour from now.";
        public const string OriginalTimeRequired = "Provide the original appointment time.";
        public const string InvalidFirstName = "Enter your first name (1–80 characters).";
        public const string InvalidLastName = "Enter your last name (1–80 characters).";
        public const string InvalidEmail = "Enter a valid email address.";
        public const string InvalidPhone = "Enter a South African number, such as 082 123 4567.";
        public const string ConsentRequired = "Please agree to the use of your details for this appointment.";
        public const string IdempotencyKeyRequired = "Provide a UUID Idempotency-Key header.";
        public const string SlotUnavailable = "That time was just booked. Please choose another time.";
        public const string BookingUnavailable = "Booking is temporarily unavailable. Please try again with the same request.";
        public const string AppointmentsUnavailable = "Appointments are temporarily unavailable. Please try again shortly.";
        public const string TokenSecretRequired = "Set Booking:TokenSecret.";
        public const string DatabaseConnectionRequired = "Set ConnectionStrings:Database.";
        public const string TokenSecretTooShort = "Booking:TokenSecret must be at least 32 characters.";
    }

    public static class Patterns
    {
        public const string SouthAfricanPhone = @"^(?:0[1-8][0-9]|\+27[1-8][0-9])[0-9]{7}$";
        public const string PhoneFormatting = @"[\s()-]";
    }
}
