export const validationMessages = {
  firstName: "Enter your first name (up to 80 characters).",
  lastName: "Enter your last name (up to 80 characters).",
  email: "Enter a valid email address.",
  phone: "Enter a South African mobile number, e.g. 082 123 4567.",
  consent: "Please agree so we can process your booking.",
  connectionFailed: "We couldn’t connect. Check your connection and try again.",
  slotUnavailable: "That time was just booked. Please choose another time.",
  tooManyRequests: "A few too many requests. Please wait a moment and try again.",
  serviceUnavailable:
    "Our booking service is temporarily unavailable. Please try again.",
  unexpectedError: "Something went wrong. Please try again.",
  pendingBooking:
    "A previous booking request needs checking. Retrieve your confirmation before starting another appointment.",
  branchesUnavailable:
    "We couldn’t load our branches. Please try again in a moment.",
  availabilityUnavailable:
    "We couldn’t check availability. Please try again.",
  bookingMismatch: "Saved booking does not match request",
  invalidDetails: "Please check your details and try again.",
  confirmationUnavailable:
    "Your booking was saved, but we couldn’t load the confirmation. Keep this page open and retry to retrieve it safely.",
  bookingResultUnknown:
    "We couldn’t confirm the result of your request. Keep this page open and retry with these details so we can safely recover your booking.",
  bookingFailed: "We couldn’t complete your booking. Please try again.",
  noBranchesFound: "No branches found",
  branchSearchHint: "Try a different suburb or choose another province.",
  noSlotsAvailable: "No times left on this date",
  selectAnotherDate:
    "Choose another date to find your next available appointment.",
} as const;

export const validationPatterns = {
  email: /^[^\s@]+@[^\s@]+\.[^\s@]+$/,
  phone: /^(?:0|\+27)[6-8][0-9]{8}$/,
  phoneFormatting: /[\s()-]/g,
  uuid: /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i,
  date: /^\d{4}-\d{2}-\d{2}$/,
  time: /^(?:[01]\d|2[0-3]):[0-5]\d$/,
} as const;
