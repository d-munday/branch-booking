import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import App from "./App";
import { api, ApiError } from "./api";
import { bookableDates } from "./booking";
import { saveAttempt } from "./recovery";
import type {
  Booking,
  BookingRequest,
  Branch,
  Receipt,
  Service,
} from "./types";

vi.mock("./api", async (importOriginal) => {
  const original = await importOriginal<typeof import("./api")>();
  return {
    ...original,
    api: {
      branches: vi.fn(),
      services: vi.fn(),
      availability: vi.fn(),
      book: vi.fn(),
      recover: vi.fn(),
      booking: vi.fn(),
    },
  };
});

const branches: Branch[] = [
  {
    id: "cape-town-cbd",
    name: "Cape Town CBD",
    address: "22 Adderley Street",
    city: "Cape Town",
    province: "Western Cape",
    hours: "Mon–Fri 08:00–16:30",
  },
  {
    id: "sea-point",
    name: "Sea Point",
    address: "94 Regent Road",
    city: "Cape Town",
    province: "Western Cape",
    hours: "Mon–Fri 08:00–16:30",
  },
  {
    id: "sandton",
    name: "Sandton City",
    address: "Rivonia Road",
    city: "Johannesburg",
    province: "Gauteng",
    hours: "Mon–Fri 08:00–16:30",
  },
];
const services: Service[] = [
  {
    id: "everyday-banking",
    name: "Everyday banking",
    description: "Help with your account.",
    durationMinutes: 30,
  },
  {
    id: "savings",
    name: "Savings",
    description: "Plan for tomorrow.",
    durationMinutes: 30,
  },
];
const receipt: Receipt = {
  id: "booking-1",
  accessToken: "capability",
  reference: "BR-123",
};
const saved = (body: BookingRequest): Booking => ({
  ...body,
  ...receipt,
  status: "confirmed",
  confirmation: {
    simulated: true,
    channel: "email",
    subject: "Your visit is confirmed",
    body: "Please bring your ID.",
  },
});
let lastBody: BookingRequest;

beforeEach(() => {
  sessionStorage.clear();
  vi.mocked(api.branches).mockReset().mockResolvedValue(branches);
  vi.mocked(api.services).mockReset().mockResolvedValue(services);
  vi.mocked(api.availability)
    .mockReset()
    .mockImplementation(async (_branch, date) => ({
      date,
      slots: [
        { time: "09:00", available: true },
        { time: "09:30", available: false },
        { time: "10:00", available: true },
      ],
    }));
  vi.mocked(api.book)
    .mockReset()
    .mockImplementation(async (body) => {
      lastBody = body;
      return receipt;
    });
  vi.mocked(api.recover)
    .mockReset()
    .mockImplementation(async (body) => {
      lastBody = body;
      return receipt;
    });
  vi.mocked(api.booking)
    .mockReset()
    .mockImplementation(async () => saved(lastBody));
  HTMLDialogElement.prototype.showModal = function () {
    this.setAttribute("open", "");
  };
});

async function selectVisit() {
  await screen.findByRole("button", { name: "Everyday banking" });
  fireEvent.click(screen.getByRole("button", { name: "Everyday banking" }));
  fireEvent.click(
    screen.getByRole("button", { name: /Cape Town CBD 22 Adderley/ }),
  );
  fireEvent.click(screen.getByRole("button", { name: "Continue" }));
  await screen.findByRole("button", { name: "09:00" });
}

async function detailsStep() {
  await selectVisit();
  fireEvent.click(screen.getByRole("button", { name: "09:00" }));
  fireEvent.click(screen.getByRole("button", { name: "Continue" }));
}

function fillDetails() {
  fireEvent.change(screen.getByLabelText("First name"), {
    target: { value: " Thandi " },
  });
  fireEvent.change(screen.getByLabelText("Last name"), {
    target: { value: "Nkosi" },
  });
  fireEvent.change(screen.getByLabelText("Email address"), {
    target: { value: "THANDI@example.com" },
  });
  fireEvent.change(screen.getByLabelText("Mobile number"), {
    target: { value: "082 123 4567" },
  });
  fireEvent.click(screen.getByRole("checkbox"));
}

describe("booking journey", () => {
  it("requires service and branch, books normalized details and displays the durable confirmation", async () => {
    render(<App />);
    expect(screen.getByRole("status")).toHaveTextContent(
      "Finding your next branch",
    );
    await screen.findByRole("button", { name: "Everyday banking" });
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
    await detailsStep();
    expect(screen.getByLabelText("First name")).toHaveAttribute(
      "aria-invalid",
      "false",
    );
    expect(
      screen.queryByText("Please agree so we can process your booking."),
    ).not.toBeInTheDocument();
    expect(api.book).not.toHaveBeenCalled();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByRole("heading", { name: "You’re booked, Thandi." });
    expect(api.book).toHaveBeenCalledOnce();
    expect(lastBody).toMatchObject({
      firstName: "Thandi",
      email: "thandi@example.com",
      phone: "0821234567",
      branchId: "cape-town-cbd",
      time: "09:00",
      consent: true,
    });
    expect(screen.getByText("Your visit is confirmed")).toBeVisible();
    expect(
      screen.getByText(/No real email or SMS has been sent/),
    ).toBeVisible();
    fireEvent.click(
      screen.getByRole("button", { name: "Book another appointment" }),
    );
    expect(
      screen.getByRole("heading", { name: "Let’s start with your visit." }),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
  });

  it("filters branches by text and province and lets customers clear an empty result", async () => {
    const user = userEvent.setup();
    render(<App />);
    await screen.findByRole("button", { name: "Everyday banking" });
    await user.type(
      screen.getByRole("textbox", { name: "Search branches" }),
      "regent",
    );
    expect(screen.getByText("1 branch to choose from")).toBeVisible();
    expect(
      screen.getByRole("button", { name: /Sea Point 94 Regent/ }),
    ).toBeVisible();
    await user.selectOptions(
      screen.getByRole("combobox", { name: "Filter by province" }),
      "Gauteng",
    );
    expect(screen.getByText("No branches found")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(screen.getByText("3 branches to choose from")).toBeVisible();
    await user.selectOptions(
      screen.getByRole("combobox", { name: "Filter by province" }),
      "Gauteng",
    );
    expect(
      screen.getByRole("button", { name: /Sandton City Rivonia/ }),
    ).toBeVisible();
  });

  it("can page dates, change selection and return to a previously completed step", async () => {
    render(<App />);
    await selectVisit();
    expect(
      screen.getByRole("button", { name: "09:30 unavailable" }),
    ).toBeDisabled();
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
    expect(
      screen.getByRole("button", { name: "Earlier dates" }),
    ).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Later dates" }));
    const dateButtons = within(
      screen.getByRole("group", { name: "Choose a date" }),
    ).getAllByRole("button");
    fireEvent.click(dateButtons[0]);
    await waitFor(() =>
      expect(api.availability).toHaveBeenLastCalledWith(
        "cape-town-cbd",
        bookableDates()[5],
        expect.any(AbortSignal),
      ),
    );
    fireEvent.click(screen.getByRole("button", { name: "Earlier dates" }));
    await screen.findByRole("button", { name: "10:00" });
    fireEvent.click(screen.getByRole("button", { name: "10:00" }));
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(
      screen.getByRole("heading", { name: "Find your kind of time." }),
    ).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(
      screen.getByRole("button", { name: "Everyday banking" }),
    ).toHaveAttribute("aria-pressed", "true");
  });

  it("validates each required detail before making a booking request", async () => {
    render(<App />);
    await detailsStep();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    expect(screen.getByLabelText("First name")).toHaveAttribute(
      "aria-invalid",
      "true",
    );
    expect(
      screen.getByText("Please agree so we can process your booking."),
    ).toBeVisible();
    expect(api.book).not.toHaveBeenCalled();
    fillDetails();
    expect(screen.getByLabelText("First name")).toHaveAttribute(
      "aria-invalid",
      "false",
    );
    expect(
      screen.queryByText("Please agree so we can process your booking."),
    ).not.toBeInTheDocument();
  });
});

describe("booking recovery", () => {
  it("retries catalog loading and makes help available when data cannot load", async () => {
    vi.mocked(api.branches).mockRejectedValueOnce(new Error("offline"));
    render(<App />);
    await screen.findByRole("alert");
    fireEvent.click(screen.getByRole("button", { name: "How it works" }));
    expect(
      screen.getByRole("dialog", { name: "Your visit, made simple." }),
    ).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "Close help" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    await screen.findByRole("button", { name: "Everyday banking" });
    expect(api.branches).toHaveBeenCalledTimes(2);
  });

  it("retries availability errors and explains when no slots remain", async () => {
    vi.mocked(api.availability)
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ date: bookableDates()[0], slots: [] });
    render(<App />);
    await screen.findByRole("button", { name: "Everyday banking" });
    fireEvent.click(screen.getByRole("button", { name: "Everyday banking" }));
    fireEvent.click(screen.getByRole("button", { name: /Cape Town CBD 22/ }));
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    await screen.findByText(
      "We couldn’t check availability. Please try again.",
    );
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Try again" }));
    await screen.findByText("No times left on this date");
  });

  it("reuses the saved receipt after confirmation fetching fails without posting another booking", async () => {
    vi.mocked(api.booking).mockRejectedValueOnce(new Error("offline"));
    render(<App />);
    await detailsStep();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByText(
      /Your booking was saved, but we couldn’t load the confirmation/,
    );
    expect(screen.getByLabelText("First name")).toBeDisabled();
    expect(screen.getByRole("checkbox")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Back" })).toBeDisabled();
    fireEvent.click(
      screen.getByRole("button", { name: "Retrieve confirmation" }),
    );
    await screen.findByRole("heading", { name: "You’re booked, Thandi." });
    expect(api.book).toHaveBeenCalledOnce();
    expect(api.booking).toHaveBeenCalledTimes(2);
    expect(api.recover).not.toHaveBeenCalled();
  });

  it("retrieves a saved booking after an uncertain POST without issuing another write", async () => {
    vi.mocked(api.book).mockRejectedValueOnce(
      new ApiError("Connection interrupted", 0),
    );
    render(<App />);
    await detailsStep();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByText(/We couldn’t confirm the result of your request/);
    expect(screen.getByLabelText("Email address")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Back" })).toBeDisabled();
    fireEvent.click(
      screen.getByRole("button", { name: "Retrieve confirmation" }),
    );
    await screen.findByRole("heading", { name: "You’re booked, Thandi." });
    expect(api.book).toHaveBeenCalledOnce();
    expect(api.recover).toHaveBeenCalledOnce();
    expect(vi.mocked(api.book).mock.calls[0][1]).toBe(
      vi.mocked(api.recover).mock.calls[0][1],
    );
    expect(sessionStorage.getItem("branch-booking:pending")).toBeNull();
  });

  it("safely retries the original POST when a recovered receipt confirms no booking exists", async () => {
    vi.mocked(api.book).mockRejectedValueOnce(
      new ApiError("Connection interrupted", 0),
    );
    vi.mocked(api.booking).mockRejectedValueOnce(
      new ApiError("Not found", 404),
    );
    render(<App />);
    await detailsStep();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByRole("button", { name: "Retrieve confirmation" });
    fireEvent.click(
      screen.getByRole("button", { name: "Retrieve confirmation" }),
    );
    await screen.findByRole("heading", { name: "You’re booked, Thandi." });
    expect(api.book).toHaveBeenCalledTimes(2);
    expect(vi.mocked(api.book).mock.calls[0][1]).toBe(
      vi.mocked(api.book).mock.calls[1][1],
    );
  });

  it("restores a tab request after refresh and retrieves an existing receipt outside the booking window", async () => {
    const body: BookingRequest = {
      firstName: "Thandi",
      lastName: "Nkosi",
      email: "thandi@example.com",
      phone: "0821234567",
      consent: true,
      branchId: "cape-town-cbd",
      serviceId: "everyday-banking",
      date: "2026-01-05",
      time: "09:00",
    };
    const key = "625bd352-c6d8-4e82-a34d-08c8ea31b916";
    saveAttempt({ body: JSON.stringify(body), key });
    render(<App />);
    await screen.findByLabelText("First name");
    expect(screen.getByLabelText("First name")).toHaveValue("Thandi");
    expect(screen.getByLabelText("First name")).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "A previous booking request needs checking",
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Retrieve confirmation" }),
    );
    await screen.findByRole("heading", { name: "You’re booked, Thandi." });
    expect(api.recover).toHaveBeenCalledWith(body, key);
    expect(api.book).not.toHaveBeenCalled();
    expect(sessionStorage.getItem("branch-booking:pending")).toBeNull();
  });

  it("keeps recovery locked when the receipt service itself is temporarily unavailable", async () => {
    vi.mocked(api.book).mockRejectedValueOnce(
      new ApiError("Connection interrupted", 0),
    );
    vi.mocked(api.recover).mockRejectedValueOnce(
      new ApiError("Service unavailable", 503),
    );
    render(<App />);
    await detailsStep();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByRole("button", { name: "Retrieve confirmation" });
    fireEvent.click(
      screen.getByRole("button", { name: "Retrieve confirmation" }),
    );
    await waitFor(() => expect(api.recover).toHaveBeenCalledOnce());
    await screen.findByRole("button", { name: "Retrieve confirmation" });
    expect(screen.getByLabelText("Email address")).toBeDisabled();
    expect(api.book).toHaveBeenCalledOnce();
    expect(sessionStorage.getItem("branch-booking:pending")).not.toBeNull();
  });

  it("keeps details editable after an explicit rate-limit rejection", async () => {
    vi.mocked(api.book).mockRejectedValueOnce(
      new ApiError("Please wait before trying again.", 429),
    );
    render(<App />);
    await detailsStep();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByText("Please wait before trying again.");
    expect(screen.getByLabelText("First name")).toBeEnabled();
    expect(screen.getByRole("button", { name: "Back" })).toBeEnabled();
    fireEvent.change(screen.getByLabelText("First name"), {
      target: { value: "Alex" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByRole("heading", { name: "You’re booked, Alex." });
  });

  it("returns customers to fresh availability after a booking conflict", async () => {
    vi.mocked(api.book).mockRejectedValueOnce(
      new ApiError("That time was just booked.", 409),
    );
    render(<App />);
    await detailsStep();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByRole("heading", { name: "Find your kind of time." });
    expect(screen.getByRole("alert")).toHaveTextContent(
      "That time was just booked.",
    );
    await screen.findByRole("button", { name: "09:00" });
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "10:00" }));
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByRole("heading", { name: "You’re booked, Thandi." });
    expect(vi.mocked(api.book).mock.calls[0][1]).not.toBe(
      vi.mocked(api.book).mock.calls[1][1],
    );
  });

  it("maps server field validation and refuses a mismatched saved receipt", async () => {
    vi.mocked(api.book).mockRejectedValueOnce(
      new ApiError("Invalid", 400, {
        Email: ["Choose another email address."],
      }),
    );
    vi.mocked(api.booking).mockImplementation(async () => ({
      ...saved(lastBody),
      email: "someone-else@example.com",
    }));
    render(<App />);
    await detailsStep();
    fillDetails();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByText("Choose another email address.");
    expect(screen.getByLabelText("Email address")).toHaveAttribute(
      "aria-invalid",
      "true",
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm appointment" }),
    );
    await screen.findByText(
      /Your booking was saved, but we couldn’t load the confirmation/,
    );
    expect(
      screen.queryByRole("heading", { name: /You’re booked/ }),
    ).not.toBeInTheDocument();
  });
});
