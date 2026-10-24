import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { clearAttempt, restoreAttempt, saveAttempt } from "./recovery";
import type { BookingRequest } from "./types";

const key = "625bd352-c6d8-4e82-a34d-08c8ea31b916";
const storageKey = "branch-booking:pending";
const request: BookingRequest = {
  branchId: "cape-town-cbd",
  serviceId: "everyday-banking",
  date: "2026-10-26",
  time: "09:00",
  firstName: "Thandi",
  lastName: "Nkosi",
  email: "thandi@example.com",
  phone: "0821234567",
  consent: true,
};
const pending = { body: JSON.stringify(request), key };
beforeEach(() => sessionStorage.clear());
afterEach(() => vi.restoreAllMocks());

describe("tab-scoped booking recovery", () => {
  it("restores the exact request and key but never persists a bearer capability", () => {
    saveAttempt({
      ...pending,
      receipt: {
        id: "id",
        reference: "BR-1",
        accessToken: "secret-capability",
      },
    });
    expect(restoreAttempt()).toEqual({ pending, request });
    expect(sessionStorage.getItem(storageKey)).not.toContain(
      "secret-capability",
    );
    clearAttempt();
    expect(restoreAttempt()).toBeNull();
  });

  it.each([
    null,
    "{not json",
    "{}",
    JSON.stringify({ ...pending, key: "invalid" }),
    JSON.stringify({ ...pending, body: "{broken" }),
    JSON.stringify({
      ...pending,
      body: JSON.stringify({ ...request, consent: false }),
    }),
    JSON.stringify({
      ...pending,
      body: JSON.stringify({ ...request, email: 123 }),
    }),
  ])(
    "ignores missing or malformed stored state without breaking the booking page",
    (value) => {
      if (value !== null) sessionStorage.setItem(storageKey, value);
      expect(restoreAttempt()).toBeNull();
    },
  );

  it("continues safely when browser storage is disabled or its quota is exceeded", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new DOMException("Unavailable");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new DOMException("Quota exceeded");
    });
    vi.spyOn(Storage.prototype, "removeItem").mockImplementation(() => {
      throw new DOMException("Unavailable");
    });
    expect(restoreAttempt()).toBeNull();
    expect(() => saveAttempt(pending)).not.toThrow();
    expect(() => clearAttempt()).not.toThrow();
  });

  it.each([
    ["date", "garbage"],
    ["date", "2026-02-30"],
    ["date", "2026-99-99"],
    ["time", "24:00"],
    ["time", "09:60"],
    ["branchId", ""],
    ["serviceId", ""],
    ["email", "invalid-email"],
    ["phone", "0121234567"],
  ])(
    "discards invalid stored %s before the appointment summary can render it",
    (field, value) => {
      sessionStorage.setItem(
        storageKey,
        JSON.stringify({
          ...pending,
          body: JSON.stringify({ ...request, [field]: value }),
        }),
      );
      expect(restoreAttempt()).toBeNull();
    },
  );
});
