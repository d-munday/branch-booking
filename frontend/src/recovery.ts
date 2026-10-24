import type { BookingRequest, Receipt } from "./types";
import { validateDetails } from "./booking";

export interface PendingAttempt {
  body: string;
  key: string;
  receipt?: Receipt;
}
const storageKey = "branch-booking:pending";

export function restoreAttempt(): {
  pending: PendingAttempt;
  request: BookingRequest;
} | null {
  try {
    const saved = JSON.parse(sessionStorage.getItem(storageKey) || "null");
    if (
      !saved ||
      typeof saved.body !== "string" ||
      typeof saved.key !== "string" ||
      !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(saved.key)
    )
      return null;
    const request = JSON.parse(saved.body);
    const names = [
      "branchId",
      "serviceId",
      "date",
      "time",
      "firstName",
      "lastName",
      "email",
      "phone",
    ];
    if (
      !request ||
      !names.every((name) => typeof request[name] === "string") ||
      request.consent !== true
    )
      return null;
    if (
      !/^\d{4}-\d{2}-\d{2}$/.test(request.date) ||
      new Date(`${request.date}T12:00:00Z`).toISOString().slice(0, 10) !==
        request.date ||
      !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(request.time) ||
      !request.branchId ||
      !request.serviceId ||
      Object.keys(validateDetails(request)).length
    )
      return null;
    return { pending: { body: saved.body, key: saved.key }, request };
  } catch {
    return null;
  }
}

export function saveAttempt(pending: PendingAttempt): void {
  try {
    sessionStorage.setItem(
      storageKey,
      JSON.stringify({ body: pending.body, key: pending.key }),
    );
  } catch {
    /* Memory recovery remains available when browser storage is disabled. */
  }
}

export function clearAttempt(): void {
  try {
    sessionStorage.removeItem(storageKey);
  } catch {
    /* Storage can be unavailable in private browsing. */
  }
}
