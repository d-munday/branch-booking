import { describe, expect, it, vi } from "vitest";
import {
  addDays,
  bookableDates,
  calendarFile,
  dateLabel,
  localDate,
  validateDetails,
} from "./booking";
import type { Booking, Details } from "./types";

const details: Details = {
  firstName: "Thandi",
  lastName: "Nkosi",
  email: "thandi@example.com",
  phone: "082 123 4567",
  consent: true,
};

describe("branch-local booking dates", () => {
  it("uses South African calendar dates when UTC is still on the previous day", () => {
    expect(localDate(new Date("2026-10-24T22:30:00Z"))).toBe("2026-10-25");
    expect(localDate()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
  it("adds calendar days across leap-day and year boundaries", () => {
    expect(addDays("2028-02-28", 1)).toBe("2028-02-29");
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(addDays("2027-01-01", -1)).toBe("2026-12-31");
  });
  it("offers exactly the inclusive 30-day window with Sundays excluded", () => {
    const dates = bookableDates("2026-10-24");
    expect(dates[0]).toBe("2026-10-24");
    expect(dates.at(-1)).toBe("2026-11-23");
    expect(dates).not.toContain("2026-10-25");
    expect(
      dates.every((day) => new Date(`${day}T12:00:00Z`).getUTCDay() !== 0),
    ).toBe(true);
    expect(bookableDates().length).toBeGreaterThan(20);
  });
  it("formats long and compact labels for the same date", () => {
    expect(dateLabel("2026-10-26")).toContain("Monday");
    expect(dateLabel("2026-10-26", true)).toContain("Mon");
  });
});

describe("customer detail validation", () => {
  it("accepts Unicode names, formatted domestic numbers and international numbers", () => {
    expect(
      validateDetails({
        ...details,
        firstName: "佐藤",
        lastName: "Dlamini-Müller",
      }),
    ).toEqual({});
    expect(validateDetails({ ...details, phone: "+27 (82) 123-4567" })).toEqual(
      {},
    );
    expect(
      validateDetails({
        ...details,
        firstName: "a".repeat(80),
        lastName: "b".repeat(80),
      }),
    ).toEqual({});
  });
  it.each([
    ["firstName", " "],
    ["firstName", "a".repeat(81)],
    ["lastName", ""],
    ["lastName", "b".repeat(81)],
    ["email", "invalid"],
    ["email", "a".repeat(250) + "@test.com"],
    ["phone", "0121234567"],
    ["phone", "082123456"],
    ["phone", "+278212345678"],
    ["consent", false],
  ])(
    "rejects invalid %s without invalidating unrelated fields",
    (field, value) => {
      expect(
        Object.keys(validateDetails({ ...details, [field]: value })),
      ).toEqual([field]);
    },
  );
});

describe("calendar export", () => {
  it("exports a 30-minute UTC event and escapes calendar control characters", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-10-24T10:00:00Z"));
    const booking = {
      id: "abc",
      reference: "BR-123",
      date: "2026-10-26",
      time: "10:00",
    } as Booking;
    const file = calendarFile(
      booking,
      "Cape;Town, CBD",
      "Line 1\nLine 2\\Street",
    );
    expect(file).toContain(
      "DTSTART:20261026T080000Z\r\nDTEND:20261026T083000Z",
    );
    expect(file).toContain("DTSTAMP:20261024T100000Z");
    expect(file).toContain("SUMMARY:Branch appointment at Cape\\;Town\\, CBD");
    expect(file).toContain("LOCATION:Line 1\\nLine 2\\\\Street");
    expect(file.endsWith("END:VCALENDAR\r\n")).toBe(true);
    vi.useRealTimers();
  });
});
