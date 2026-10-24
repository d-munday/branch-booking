import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Confirmation, HelpDialog, Summary } from "./components";
import type { Booking, Branch, Service } from "./types";

const branch: Branch = {
  id: "cape-town-cbd",
  name: "Cape Town CBD",
  address: "22 Adderley Street",
  city: "Cape Town",
  province: "Western Cape",
  hours: "Mon–Fri",
};
const service: Service = {
  id: "everyday-banking",
  name: "Everyday banking",
  description: "Account help",
  durationMinutes: 30,
};
const booking: Booking = {
  id: "booking-1",
  reference: "BR-123",
  branchId: branch.id,
  serviceId: service.id,
  date: "2026-10-26",
  time: "09:00",
  firstName: "Thandi",
  lastName: "Nkosi",
  email: "thandi@example.com",
  phone: "0821234567",
  consent: true,
  status: "confirmed",
  confirmation: {
    simulated: true,
    channel: "email",
    subject: "Confirmed",
    body: "Your appointment is booked.",
  },
};

describe("appointment receipt actions", () => {
  it("downloads an iCalendar file, revokes its temporary URL, prints and restarts", () => {
    const create = vi.fn().mockReturnValue("blob:calendar");
    const revoke = vi.fn();
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: create,
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: revoke,
    });
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => {});
    const print = vi.spyOn(window, "print").mockImplementation(() => {});
    const restart = vi.fn();
    render(
      <Confirmation
        booking={booking}
        branch={branch}
        service={service}
        restart={restart}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Add to calendar" }));
    expect(create).toHaveBeenCalledWith(expect.any(Blob));
    expect(click).toHaveBeenCalledOnce();
    expect(revoke).toHaveBeenCalledWith("blob:calendar");
    const anchor = click.mock.contexts[0] as unknown as HTMLAnchorElement;
    expect(anchor.download).toBe("BR-123.ics");
    fireEvent.click(screen.getByRole("button", { name: "Print confirmation" }));
    expect(print).toHaveBeenCalledOnce();
    fireEvent.click(
      screen.getByRole("button", { name: "Book another appointment" }),
    );
    expect(restart).toHaveBeenCalledOnce();
  });
});

describe("accessible appointment assistance", () => {
  it("opens a named modal and closes through its explicit action or Escape cancellation", () => {
    HTMLDialogElement.prototype.showModal = function () {
      this.setAttribute("open", "");
    };
    const close = vi.fn();
    render(<HelpDialog close={close} />);
    const dialog = screen.getByRole("dialog", {
      name: "Your visit, made simple.",
    });
    expect(dialog).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "Got it" }));
    expect(close).toHaveBeenCalledOnce();
    fireEvent(dialog, new Event("cancel", { bubbles: true }));
    expect(close).toHaveBeenCalledTimes(2);
  });
  it("shows useful placeholders and updates them when a visit has been selected", () => {
    const { rerender } = render(<Summary date="2026-10-26" time="" step={0} />);
    expect(screen.getByText("Choose a branch")).toBeVisible();
    expect(screen.getByText("Choose a service")).toBeVisible();
    rerender(
      <Summary
        branch={branch}
        service={service}
        date="2026-10-26"
        time="09:00"
        step={1}
      />,
    );
    expect(screen.getByText("Cape Town CBD")).toBeVisible();
    expect(screen.getByText(/09:00/)).toBeVisible();
  });
});
