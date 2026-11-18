import { api, ApiError } from "./api";
import type { PendingAttempt } from "./recovery";
import type { Booking, BookingRequest, Receipt } from "./types";
import { validationMessages } from "./validations.constants";

export type SubmissionState =
  | { status: "editing" }
  | { status: "submitting" | "pending"; attempt: PendingAttempt }
  | { status: "confirmed"; booking: Booking };

type SubmissionEvent =
  | { type: "start"; attempt: PendingAttempt }
  | { type: "defer"; receipt?: Receipt }
  | { type: "confirm"; booking: Booking }
  | { type: "reset" };

export function submissionReducer(
  state: SubmissionState,
  event: SubmissionEvent,
): SubmissionState {
  switch (event.type) {
    case "start":
      return { status: "submitting", attempt: event.attempt };
    case "defer":
      return state.status === "submitting"
        ? { status: "pending", attempt: { ...state.attempt, receipt: event.receipt } }
        : state;
    case "confirm":
      return { status: "confirmed", booking: event.booking };
    case "reset":
      return { status: "editing" };
  }
}

type Outcome =
  | { status: "confirmed"; booking: Booking }
  | { status: "pending"; message: string; receipt?: Receipt }
  | { status: "rejected"; error: ApiError };

export async function resolveSubmission(
  body: BookingRequest,
  attempt: PendingAttempt,
  recovering: boolean,
): Promise<Outcome> {
  let receipt = attempt.receipt;
  let operation: "lookup" | "write" | "confirm" = "lookup";
  try {
    let saved: Booking | undefined;
    if (recovering && !receipt) {
      const candidate = await api.recover(body, attempt.key);
      try {
        saved = await api.booking(candidate);
        receipt = candidate;
      } catch (failure) {
        if (!(failure instanceof ApiError && failure.status === 404)) throw failure;
      }
    }
    if (!receipt) {
      operation = "write";
      receipt = await api.book(body, attempt.key);
    }
    operation = "confirm";
    saved ??= await api.booking(receipt);
    if (
      saved.branchId !== body.branchId ||
      saved.serviceId !== body.serviceId ||
      saved.date !== body.date ||
      saved.time !== body.time ||
      saved.email.toLowerCase() !== body.email ||
      saved.phone !== body.phone ||
      saved.firstName !== body.firstName ||
      saved.lastName !== body.lastName
    ) {
      throw new Error(validationMessages.bookingMismatch);
    }
    return { status: "confirmed", booking: saved };
  } catch (failure) {
    const throttled = failure instanceof ApiError && failure.status === 429;
    if (
      operation === "write" &&
      failure instanceof ApiError &&
      failure.status >= 400 && failure.status < 500 &&
      failure.status !== 408 &&
      !(recovering && throttled)
    ) {
      return { status: "rejected", error: failure };
    }
    const message = throttled
      ? failure.message
      : receipt
        ? validationMessages.confirmationUnavailable
        : validationMessages.bookingResultUnknown;
    return { status: "pending", receipt, message };
  }
}
