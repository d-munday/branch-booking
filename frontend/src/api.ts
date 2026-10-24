import type {
  Availability,
  Booking,
  BookingRequest,
  Branch,
  Receipt,
  Service,
} from "./types";

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public errors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const timeout = AbortSignal.timeout(15000);
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      ...init,
      signal: init.signal ? AbortSignal.any([init.signal, timeout]) : timeout,
    });
  } catch (error) {
    if (init.signal?.aborted) throw error;
    throw new ApiError(
      "We couldn’t connect. Check your connection and try again.",
      0,
    );
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new ApiError(
      response.status === 409
        ? "That time was just booked. Please choose another time."
        : response.status === 429
          ? "A few too many requests. Please wait a moment and try again."
          : response.status >= 500
            ? "Our booking service is temporarily unavailable. Please try again."
            : problem.detail ||
              problem.title ||
              "Something went wrong. Please try again.",
      response.status,
      problem.errors,
    );
  }
  return response.json() as Promise<T>;
}

export const api = {
  branches: (signal?: AbortSignal) =>
    request<Branch[]>("/branches", { signal }),
  services: (signal?: AbortSignal) =>
    request<Service[]>("/services", { signal }),
  availability: (branchId: string, date: string, signal?: AbortSignal) =>
    request<Availability>(
      `/availability?${new URLSearchParams({ branchId, date })}`,
      { signal },
    ),
  book: (body: BookingRequest, idempotencyKey: string) =>
    request<Receipt>("/bookings", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": idempotencyKey,
      },
      body: JSON.stringify(body),
    }),
  recover: (body: BookingRequest, idempotencyKey: string) =>
    request<Receipt>("/bookings/receipt", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": idempotencyKey,
      },
      body: JSON.stringify(body),
    }),
  booking: (receipt: Receipt) =>
    request<Booking>(`/bookings/${encodeURIComponent(receipt.id)}`, {
      headers: { Authorization: `Bearer ${receipt.accessToken}` },
    }),
};
