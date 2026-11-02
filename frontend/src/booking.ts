import type { Booking, Details } from "./types";
import { validationMessages, validationPatterns } from "./validations.constants";

const zone = "Africa/Johannesburg";
export function localDate(now = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: zone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(now);
}
export function addDays(date: string, amount: number): string {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() + amount);
  return value.toISOString().slice(0, 10);
}
export function dateLabel(date: string, short = false): string {
  return new Intl.DateTimeFormat("en-ZA", {
    weekday: short ? "short" : "long",
    day: "numeric",
    month: short ? "short" : "long",
    timeZone: zone,
  }).format(new Date(`${date}T12:00:00Z`));
}
export function bookableDates(today = localDate()): string[] {
  return Array.from({ length: 31 }, (_, i) => addDays(today, i)).filter(
    (date) => new Date(`${date}T12:00:00Z`).getUTCDay() !== 0,
  );
}
export function validateDetails(details: Details): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!details.firstName.trim() || details.firstName.trim().length > 80)
    errors.firstName = validationMessages.firstName;
  if (!details.lastName.trim() || details.lastName.trim().length > 80)
    errors.lastName = validationMessages.lastName;
  if (
    !validationPatterns.email.test(details.email.trim()) ||
    details.email.length > 254
  )
    errors.email = validationMessages.email;
  if (
    !validationPatterns.phone.test(
      details.phone.replace(validationPatterns.phoneFormatting, ""),
    )
  )
    errors.phone = validationMessages.phone;
  if (!details.consent)
    errors.consent = validationMessages.consent;
  return errors;
}
export function calendarFile(
  booking: Booking,
  branchName: string,
  address: string,
): string {
  const escape = (value: string) =>
    value
      .replace(/\\/g, "\\\\")
      .replace(/\r?\n/g, "\\n")
      .replace(/;/g, "\\;")
      .replace(/,/g, "\\,");
  const start = new Date(`${booking.date}T${booking.time}:00+02:00`);
  const end = new Date(start.getTime() + 30 * 60000);
  const stamp = (date: Date) =>
    date
      .toISOString()
      .replace(/[-:]/g, "")
      .replace(/\.\d{3}/, "");
  return [
    "BEGIN:VCALENDAR",
    "VERSION:2.0",
    "PRODID:-//Branch Appointments//EN",
    "BEGIN:VEVENT",
    `UID:${booking.id}@branch-appointments`,
    `DTSTAMP:${stamp(new Date())}`,
    `DTSTART:${stamp(start)}`,
    `DTEND:${stamp(end)}`,
    `SUMMARY:${escape(`Branch appointment at ${branchName}`)}`,
    `LOCATION:${escape(address)}`,
    `DESCRIPTION:${escape(`Booking reference: ${booking.reference}. Please bring your ID.`)}`,
    "END:VEVENT",
    "END:VCALENDAR",
    "",
  ].join("\r\n");
}
