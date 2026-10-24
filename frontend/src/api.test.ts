import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError } from "./api";
import type { BookingRequest, Receipt } from "./types";

afterEach(() => vi.unstubAllGlobals());
const response = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });

describe("HTTP booking client", () => {
  it("loads catalogs and encodes availability query parameters with cancellation", async () => {
    const fetch = vi.fn().mockImplementation(async () => response([]));
    vi.stubGlobal("fetch", fetch);
    const controller = new AbortController();
    await api.branches(controller.signal);
    await api.services();
    await api.availability("cape & coast", "2026-10-26", controller.signal);
    expect(fetch.mock.calls.map((call) => call[0])).toEqual([
      "/api/branches",
      "/api/services",
      "/api/availability?branchId=cape+%26+coast&date=2026-10-26",
    ]);
    expect(fetch.mock.calls[0][1].signal).toBeInstanceOf(AbortSignal);
  });
  it("sends the idempotency key and puts receipt capabilities only in authorization headers", async () => {
    const receipt: Receipt = {
      id: "id/with slash",
      accessToken: "private-token",
      reference: "BR-1",
    };
    const fetch = vi
      .fn()
      .mockImplementation(async () => response(receipt, 201));
    vi.stubGlobal("fetch", fetch);
    const body = { firstName: "Alex" } as BookingRequest;
    expect(await api.book(body, "idempotency-uuid")).toEqual(receipt);
    expect(fetch.mock.calls[0][1]).toMatchObject({
      method: "POST",
      headers: {
        "Idempotency-Key": "idempotency-uuid",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(body),
    });
    await api.booking(receipt);
    expect(fetch.mock.calls[1][0]).toBe("/api/bookings/id%2Fwith%20slash");
    expect(fetch.mock.calls[1][1].headers).toEqual({
      Authorization: "Bearer private-token",
    });
    expect(fetch.mock.calls[1][0]).not.toContain("private-token");
  });
  it.each([
    [409, {}, "That time was just booked"],
    [429, {}, "too many requests"],
    [503, { title: "Internal secret" }, "temporarily unavailable"],
    [400, { detail: "Choose another date" }, "Choose another date"],
    [404, { title: "Not found" }, "Not found"],
    [400, {}, "Something went wrong"],
  ])(
    "translates HTTP %s into a useful, bounded error",
    async (status, problem, message) => {
      vi.stubGlobal(
        "fetch",
        vi.fn().mockResolvedValue(response(problem, status)),
      );
      await expect(api.services()).rejects.toMatchObject({
        status,
        message: expect.stringContaining(message),
      });
    },
  );
  it("retains validation errors for inline field feedback", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          response({ errors: { Email: ["Invalid email"] } }, 400),
        ),
    );
    await expect(api.book({} as BookingRequest, "key")).rejects.toMatchObject({
      errors: { Email: ["Invalid email"] },
    });
  });
  it("derives a recovery receipt through a same-origin POST with the original idempotency key", async () => {
    const fetch = vi
      .fn()
      .mockImplementation(async () => response({ id: "restored" }));
    vi.stubGlobal("fetch", fetch);
    const body = { firstName: "Thandi" } as BookingRequest;
    await api.recover(body, "original-key");
    expect(fetch.mock.calls[0][0]).toBe("/api/bookings/receipt");
    expect(fetch.mock.calls[0][1]).toMatchObject({
      method: "POST",
      headers: { "Idempotency-Key": "original-key" },
      body: JSON.stringify(body),
    });
  });
  it("handles non-JSON gateway errors and network failures", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(
        new Response("<html>Bad gateway</html>", { status: 502 }),
      )
      .mockRejectedValueOnce(new TypeError("network"));
    vi.stubGlobal("fetch", fetch);
    await expect(api.branches()).rejects.toThrow("temporarily unavailable");
    await expect(api.branches()).rejects.toMatchObject({
      status: 0,
      message: expect.stringContaining("couldn’t connect"),
    });
  });
  it("preserves caller cancellation rather than presenting a connection error", async () => {
    const controller = new AbortController();
    controller.abort();
    const abort = new DOMException("Cancelled", "AbortError");
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(abort));
    await expect(api.branches(controller.signal)).rejects.toBe(abort);
    expect(new ApiError("message", 400).errors).toEqual({});
  });
});
