import { useEffect, useRef, useState } from "react";
import {
  ArrowLeft,
  ArrowRight,
  Building2,
  CalendarDays,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  CircleHelp,
  Clock3,
  CreditCard,
  Headphones,
  LockKeyhole,
  MapPin,
  Search,
  ShieldCheck,
  Sparkles,
  Wallet,
} from "lucide-react";
import { api, ApiError } from "./api";
import type {
  Availability,
  Booking,
  BookingRequest,
  Branch,
  Details,
  Service,
} from "./types";
import { bookableDates, dateLabel, validateDetails } from "./booking";
import { Confirmation, HelpDialog, Summary } from "./components";
import { clearAttempt, restoreAttempt, saveAttempt } from "./recovery";
import type { PendingAttempt } from "./recovery";
import { validationMessages, validationPatterns } from "./validations.constants";

const emptyDetails: Details = {
  firstName: "",
  lastName: "",
  email: "",
  phone: "",
  consent: false,
};
const serviceIcons = [
  Wallet,
  CreditCard,
  Building2,
  Sparkles,
  CreditCard,
  Building2,
];

export default function App() {
  const [restored] = useState(restoreAttempt);
  const [branches, setBranches] = useState<Branch[]>([]);
  const [services, setServices] = useState<Service[]>([]);
  const [loading, setLoading] = useState(true);
  const [catalogError, setCatalogError] = useState("");
  const [attempt, setAttempt] = useState(0);
  const [step, setStep] = useState(restored ? 2 : 0);
  const [branchId, setBranchId] = useState(restored?.request.branchId || "");
  const [serviceId, setServiceId] = useState(restored?.request.serviceId || "");
  const [search, setSearch] = useState("");
  const [province, setProvince] = useState("all");
  const [date, setDate] = useState(
    () => restored?.request.date || bookableDates()[0],
  );
  const [datePage, setDatePage] = useState(0);
  const [time, setTime] = useState(restored?.request.time || "");
  const [availability, setAvailability] = useState<Availability | null>(null);
  const [slotsLoading, setSlotsLoading] = useState(false);
  const [slotAttempt, setSlotAttempt] = useState(0);
  const [slotError, setSlotError] = useState("");
  const [details, setDetails] = useState<Details>(
    restored?.request || emptyDetails,
  );
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState(
    restored
      ? validationMessages.pendingBooking
      : "",
  );
  const [submitting, setSubmitting] = useState(false);
  const [recovering, setRecovering] = useState(!!restored);
  const [booking, setBooking] = useState<Booking | null>(null);
  const [help, setHelp] = useState(false);
  const pending = useRef<PendingAttempt | null>(restored?.pending || null);
  const heading = useRef<HTMLHeadingElement>(null);
  const branch = branches.find((item) => item.id === branchId);
  const service = services.find((item) => item.id === serviceId);
  const dates = bookableDates();

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setCatalogError("");
    Promise.all([
      api.branches(controller.signal),
      api.services(controller.signal),
    ])
      .then(([nextBranches, nextServices]) => {
        setBranches(nextBranches);
        setServices(nextServices);
      })
      .catch(() => {
        if (!controller.signal.aborted)
          setCatalogError(
            validationMessages.branchesUnavailable,
          );
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [attempt]);

  useEffect(() => {
    if (!branchId || step !== 1) return;
    const controller = new AbortController();
    setSlotsLoading(true);
    setSlotError("");
    setAvailability(null);
    api
      .availability(branchId, date, controller.signal)
      .then(setAvailability)
      .catch(() => {
        if (!controller.signal.aborted)
          setSlotError(validationMessages.availabilityUnavailable);
      })
      .finally(() => {
        if (!controller.signal.aborted) setSlotsLoading(false);
      });
    return () => controller.abort();
  }, [branchId, date, step, slotAttempt]);

  function go(next: number) {
    setStep(next);
    setError("");
    requestAnimationFrame(() => {
      heading.current?.focus();
      heading.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
    });
  }

  async function submit() {
    const validation = validateDetails(details);
    setFieldErrors(validation);
    setError("");
    if (Object.keys(validation).length) {
      requestAnimationFrame(() =>
        document.getElementById(Object.keys(validation)[0])?.focus(),
      );
      return;
    }
    const body: BookingRequest = {
      ...details,
      firstName: details.firstName.trim(),
      lastName: details.lastName.trim(),
      email: details.email.trim().toLowerCase(),
      phone: details.phone.replace(validationPatterns.phoneFormatting, ""),
      branchId,
      serviceId,
      date,
      time,
    };
    const serialized = JSON.stringify(body);
    if (pending.current?.body !== serialized)
      pending.current = { body: serialized, key: crypto.randomUUID() };
    const submission = pending.current;
    saveAttempt(submission);
    setSubmitting(true);
    try {
      let saved: Booking | undefined;
      if (recovering && !submission.receipt) {
        const recoveredReceipt = await api.recover(body, submission.key);
        try {
          saved = await api.booking(recoveredReceipt);
          submission.receipt = recoveredReceipt;
        } catch (failure) {
          if (!(failure instanceof ApiError) || failure.status !== 404)
            throw failure;
        }
      }
      if (!saved) {
        submission.receipt ??= await api.book(body, submission.key);
        saved = await api.booking(submission.receipt);
      }
      if (
        saved.branchId !== branchId ||
        saved.serviceId !== serviceId ||
        saved.date !== date ||
        saved.time !== time ||
        saved.email.toLowerCase() !== body.email ||
        saved.firstName !== body.firstName ||
        saved.lastName !== body.lastName ||
        saved.phone !== body.phone
      )
        throw new Error(validationMessages.bookingMismatch);
      clearAttempt();
      setRecovering(false);
      setBooking(saved);
      go(3);
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        clearAttempt();
        setRecovering(false);
        pending.current = null;
        setTime("");
        go(1);
        setError(failure.message);
        setSlotAttempt((value) => value + 1);
      } else if (failure instanceof ApiError && failure.status === 400) {
        clearAttempt();
        pending.current = null;
        setRecovering(false);
        const errors = Object.fromEntries(
          Object.entries(failure.errors).map(([name, messages]) => [
            name.charAt(0).toLowerCase() + name.slice(1),
            messages[0],
          ]),
        );
        setFieldErrors(errors);
        setError(validationMessages.invalidDetails);
      } else {
        const uncertain =
          !!submission.receipt ||
          !(failure instanceof ApiError) ||
          failure.status === 0 ||
          failure.status >= 500;
        if (!uncertain) {
          clearAttempt();
          pending.current = null;
        }
        setRecovering(uncertain);
        setError(
          submission.receipt
            ? validationMessages.confirmationUnavailable
            : uncertain
              ? validationMessages.bookingResultUnknown
              : failure instanceof Error
                ? failure.message
                : validationMessages.bookingFailed,
        );
      }
    } finally {
      setSubmitting(false);
    }
  }

  function restart() {
    clearAttempt();
    setRecovering(false);
    setBooking(null);
    setDetails(emptyDetails);
    setFieldErrors({});
    setBranchId("");
    setServiceId("");
    setTime("");
    pending.current = null;
    go(0);
  }

  const filtered = branches.filter(
    (item) =>
      (province === "all" || item.province === province) &&
      `${item.name} ${item.city} ${item.address}`
        .toLowerCase()
        .includes(search.toLowerCase()),
  );
  const field = (
    name: Exclude<keyof Details, "consent">,
    label: string,
    placeholder: string,
    type = "text",
  ) => (
    <div className="field">
      <label htmlFor={name}>{label}</label>
      <input
        id={name}
        name={name}
        type={type}
        autoComplete={
          name === "firstName"
            ? "given-name"
            : name === "lastName"
              ? "family-name"
              : name === "phone"
                ? "tel"
                : "email"
        }
        placeholder={placeholder}
        value={details[name]}
        maxLength={name === "email" ? 254 : name === "phone" ? 20 : 80}
        aria-invalid={!!fieldErrors[name]}
        aria-describedby={fieldErrors[name] ? `${name}-error` : undefined}
        onChange={(event) => {
          setDetails((value) => ({ ...value, [name]: event.target.value }));
          setFieldErrors((value) => ({ ...value, [name]: "" }));
        }}
      />
      {fieldErrors[name] && (
        <span className="field-error" id={`${name}-error`}>
          {fieldErrors[name]}
        </span>
      )}
    </div>
  );

  return (
    <>
      <a className="skip-link" href="#booking">
        Skip to booking
      </a>
      <div className="top-strip">
        <div className="shell">
          <span>Simple banking. More living.</span>
          <span>
            <ShieldCheck size={12} /> A secure booking experience
          </span>
        </div>
      </div>
      <header className="site-header">
        <div className="shell header-inner">
          <a className="brand" href="/" aria-label="Branch appointments home">
            <span className="brand-mark" aria-hidden="true">
              <i />
              <i />
            </span>
            <span>
              capitec<span className="brand-dot">.</span>
            </span>
          </a>
          <div className="header-divider" />
          <span className="header-title">Branch appointments</span>
          <nav aria-label="Main navigation">
            <button onClick={() => setHelp(true)}>How it works</button>
            <button className="help-link" onClick={() => setHelp(true)}>
              <Headphones size={17} /> Need a hand?
            </button>
          </nav>
        </div>
      </header>
      <main className="shell">
        <div className="breadcrumb">
          <span>Personal banking</span>
          <ChevronRight size={13} />
          <span>Visit a branch</span>
          <ChevronRight size={13} />
          <strong>Book an appointment</strong>
        </div>
        <section className="hero">
          <div>
            <span className="eyebrow">
              <span /> MORE TIME FOR WHAT MATTERS
            </span>
            <h1>
              Your next branch visit.
              <br />
              <span>A little more on your terms.</span>
            </h1>
            <p>
              Skip the uncertainty. Choose your branch, pick a time,
              <br className="desktop-break" /> and let’s take care of your
              banking together.
            </p>
          </div>
          <div className="hero-note">
            <span className="hero-calendar">
              <CalendarDays size={32} />
              <i>
                <Check size={12} />
              </i>
            </span>
            <div>
              <strong>A few clicks. You’re booked.</strong>
              <span>Free to book. Easy to plan around.</span>
            </div>
          </div>
        </section>
        <div className="workspace" id="booking">
          <div className="booking-column">
            <ol className="steps" aria-label="Booking progress">
              {["Branch & service", "Date & time", "Your details"].map(
                (label, index) => (
                  <li
                    key={label}
                    className={
                      step === index ? "active" : step > index ? "complete" : ""
                    }
                    aria-current={step === index ? "step" : undefined}
                  >
                    <span>
                      {step > index ? <Check size={16} /> : index + 1}
                    </span>
                    <strong>{label}</strong>
                    {index < 2 && <div />}
                  </li>
                ),
              )}
            </ol>
            <div className="form-card">
              {step < 3 && (
                <div className="form-heading">
                  <span className="step-label">STEP {step + 1} OF 3</span>
                  <h2 tabIndex={-1} ref={heading}>
                    {
                      [
                        "Let’s start with your visit.",
                        "Find your kind of time.",
                        "A few details, and you’re set.",
                      ][step]
                    }
                  </h2>
                  <p>
                    {
                      [
                        "A familiar branch or somewhere new. We’re here for you.",
                        "Choose an available date and a time that works for you.",
                        "We’ll use these details for your appointment confirmation.",
                      ][step]
                    }
                  </p>
                </div>
              )}
              {loading && (
                <div className="loading-state" role="status">
                  <span className="spinner" /> Finding your next branch…
                </div>
              )}
              {catalogError && (
                <div className="error-box" role="alert">
                  {catalogError}
                  <button onClick={() => setAttempt((value) => value + 1)}>
                    Try again
                  </button>
                </div>
              )}
              {!loading && !catalogError && step === 0 && (
                <>
                  <div className="section-label">
                    <span className="section-number">01</span>
                    <h3>What can we help you with?</h3>
                  </div>
                  <div
                    className="service-grid"
                    role="group"
                    aria-label="Choose a service"
                  >
                    {services.map((item, index) => {
                      const Icon = serviceIcons[index % serviceIcons.length];
                      return (
                        <button
                          key={item.id}
                          className={`service-option ${serviceId === item.id ? "selected" : ""}`}
                          aria-pressed={serviceId === item.id}
                          onClick={() => setServiceId(item.id)}
                        >
                          <Icon size={22} />
                          <span>{item.name}</span>
                          <span className="selection-dot">
                            {serviceId === item.id && <Check size={10} />}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                  {service && (
                    <p className="service-description">
                      {service.description}{" "}
                      <span>About {service.durationMinutes} minutes.</span>
                    </p>
                  )}
                  <div className="section-label branch-label">
                    <span className="section-number">02</span>
                    <h3>Choose your branch</h3>
                    <span className="demo-label">Sample branches</span>
                  </div>
                  <div className="branch-filters">
                    <div className="search-field">
                      <Search size={18} />
                      <input
                        aria-label="Search branches"
                        placeholder="Search by suburb, city or branch"
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                      />
                    </div>
                    <div className="select-field">
                      <select
                        aria-label="Filter by province"
                        value={province}
                        onChange={(event) => setProvince(event.target.value)}
                      >
                        <option value="all">All provinces</option>
                        {[
                          ...new Set(branches.map((item) => item.province)),
                        ].map((item) => (
                          <option key={item}>{item}</option>
                        ))}
                      </select>
                      <ChevronDown size={15} />
                    </div>
                  </div>
                  <div className="branch-results">
                    <span>
                      {filtered.length}{" "}
                      {filtered.length === 1 ? "branch" : "branches"} to choose
                      from
                    </span>
                    <span>
                      <MapPin size={12} /> South Africa
                    </span>
                  </div>
                  <div
                    className="branch-list"
                    role="group"
                    aria-label="Choose a branch"
                  >
                    {filtered.map((item) => (
                      <button
                        className={`branch-option ${branchId === item.id ? "selected" : ""}`}
                        key={item.id}
                        aria-pressed={branchId === item.id}
                        onClick={() => {
                          setBranchId(item.id);
                          setTime("");
                        }}
                      >
                        <span className="branch-icon">
                          <Building2 size={22} />
                        </span>
                        <span className="branch-copy">
                          <strong>{item.name}</strong>
                          <span>{item.address}</span>
                          <small>
                            <Clock3 size={11} /> {item.hours}
                          </small>
                        </span>
                        <span className="branch-radio">
                          {branchId === item.id && <span />}
                        </span>
                      </button>
                    ))}
                  </div>
                  {!filtered.length && (
                    <div className="empty-state">
                      <Search size={26} />
                      <h3>{validationMessages.noBranchesFound}</h3>
                      <p>{validationMessages.branchSearchHint}</p>
                      <button
                        className="text-button"
                        onClick={() => {
                          setSearch("");
                          setProvince("all");
                        }}
                      >
                        Clear filters
                      </button>
                    </div>
                  )}
                </>
              )}
              {!loading && !catalogError && step === 1 && (
                <>
                  <div className="date-header">
                    <h3>Pick a date</h3>
                    <div>
                      <button
                        aria-label="Earlier dates"
                        disabled={datePage === 0}
                        onClick={() => setDatePage((value) => value - 1)}
                      >
                        <ChevronLeft size={18} />
                      </button>
                      <button
                        aria-label="Later dates"
                        disabled={(datePage + 1) * 5 >= dates.length}
                        onClick={() => setDatePage((value) => value + 1)}
                      >
                        <ChevronRight size={18} />
                      </button>
                    </div>
                  </div>
                  <div
                    className="date-grid"
                    role="group"
                    aria-label="Choose a date"
                  >
                    {dates.slice(datePage * 5, datePage * 5 + 5).map((item) => (
                      <button
                        aria-label={dateLabel(item)}
                        aria-pressed={date === item}
                        className={`date-option ${date === item ? "selected" : ""}`}
                        key={item}
                        onClick={() => {
                          setDate(item);
                          setTime("");
                        }}
                      >
                        <span>
                          {dateLabel(item, true).split(",")[0].split(" ")[0]}
                        </span>
                        <strong>{Number(item.slice(8))}</strong>
                        <small>
                          {new Intl.DateTimeFormat("en-ZA", {
                            month: "short",
                            timeZone: "Africa/Johannesburg",
                          }).format(new Date(`${item}T12:00:00Z`))}
                        </small>
                      </button>
                    ))}
                  </div>
                  <div className="time-heading">
                    <h3>Pick a time</h3>
                    <span>South African Standard Time (UTC+2)</span>
                  </div>
                  {slotsLoading && (
                    <div className="loading-state" role="status">
                      <span className="spinner" /> Checking available times…
                    </div>
                  )}
                  {slotError && (
                    <div className="error-box" role="alert">
                      {slotError}
                      <button
                        onClick={() => setSlotAttempt((value) => value + 1)}
                      >
                        Try again
                      </button>
                    </div>
                  )}
                  {availability && (
                    <>
                      <div
                        className="time-grid"
                        role="group"
                        aria-label="Choose a time"
                      >
                        {availability.slots.map((slot) => (
                          <button
                            key={slot.time}
                            disabled={!slot.available}
                            aria-label={`${slot.time}${slot.available ? "" : " unavailable"}`}
                            aria-pressed={time === slot.time}
                            className={`time-option ${time === slot.time ? "selected" : ""}`}
                            onClick={() => setTime(slot.time)}
                          >
                            {slot.time}
                            {time === slot.time && <Check size={14} />}
                          </button>
                        ))}
                      </div>
                      {!availability.slots.some((slot) => slot.available) && (
                        <div className="empty-state">
                          <CalendarDays size={27} />
                          <h3>{validationMessages.noSlotsAvailable}</h3>
                          <p>{validationMessages.selectAnotherDate}</p>
                        </div>
                      )}
                    </>
                  )}
                  <div className="info-note">
                    <Clock3 size={18} />
                    <p>
                      Your appointment is about{" "}
                      <strong>{service?.durationMinutes} minutes.</strong>{" "}
                      Please arrive 5 minutes early. Your time is reserved when
                      you confirm your booking.
                    </p>
                  </div>
                </>
              )}
              {!loading && !catalogError && step === 2 && (
                <form
                  id="details-form"
                  noValidate
                  onSubmit={(event) => {
                    event.preventDefault();
                    void submit();
                  }}
                >
                  <fieldset disabled={submitting || recovering}>
                    <div className="details-grid">
                      {field("firstName", "First name", "e.g. Thandi")}
                      {field("lastName", "Last name", "e.g. Nkosi")}
                      {field(
                        "email",
                        "Email address",
                        "you@example.com",
                        "email",
                      )}
                      {field("phone", "Mobile number", "082 123 4567", "tel")}
                    </div>
                    <div className="privacy-note">
                      <LockKeyhole size={19} />
                      <p>
                        Your details stay private and are only used to manage
                        this appointment. We’ll never ask for your PIN or
                        banking password.
                      </p>
                    </div>
                    <label className="consent">
                      <input
                        id="consent"
                        type="checkbox"
                        checked={details.consent}
                        aria-invalid={!!fieldErrors.consent}
                        aria-describedby={
                          fieldErrors.consent ? "consent-error" : undefined
                        }
                        onChange={(event) => {
                          setDetails((value) => ({
                            ...value,
                            consent: event.target.checked,
                          }));
                          setFieldErrors((value) => ({
                            ...value,
                            consent: "",
                          }));
                        }}
                      />
                      <span>
                        I agree to my details being used to book and confirm
                        this appointment.
                      </span>
                    </label>
                    {fieldErrors.consent && (
                      <p className="field-error" id="consent-error">
                        {fieldErrors.consent}
                      </p>
                    )}
                    <div className="simulation-note">
                      <MailIcon />
                      <span>
                        This is a demo. We’ll show a simulated email
                        confirmation. No real email or SMS will be sent.
                      </span>
                    </div>
                  </fieldset>
                </form>
              )}
              {error && (
                <div className="error-box" role="alert">
                  {error}
                </div>
              )}
              {step < 3 && !loading && !catalogError && (
                <div className="form-actions">
                  {step > 0 ? (
                    <button
                      className="back-button"
                      disabled={submitting || recovering}
                      onClick={() => go(step - 1)}
                    >
                      <ArrowLeft size={17} /> Back
                    </button>
                  ) : (
                    <span>
                      <LockKeyhole size={13} /> Your details are safe with us
                    </span>
                  )}
                  <button
                    type={step === 2 ? "submit" : "button"}
                    form={step === 2 ? "details-form" : undefined}
                    className="primary-button"
                    disabled={
                      submitting ||
                      (step === 0 && (!branchId || !serviceId)) ||
                      (step === 1 && (!time || slotsLoading || !!slotError))
                    }
                    onClick={
                      step < 2
                        ? (event) => {
                            event.preventDefault();
                            go(step + 1);
                          }
                        : undefined
                    }
                  >
                    {submitting ? (
                      <>
                        <span className="spinner" /> Confirming…
                      </>
                    ) : (
                      <>
                        {step === 2
                          ? recovering
                            ? "Retrieve confirmation"
                            : "Confirm appointment"
                          : "Continue"}
                        <ArrowRight size={18} />
                      </>
                    )}
                  </button>
                </div>
              )}
              {step === 3 && booking && branch && service && (
                <Confirmation
                  booking={booking}
                  branch={branch}
                  service={service}
                  restart={restart}
                />
              )}
            </div>
            <div className="below-card">
              <ShieldCheck size={16} />
              <span>A simpler way to visit. The same personal service.</span>
              <button onClick={() => setHelp(true)}>
                Questions? <CircleHelp size={14} />
              </button>
            </div>
          </div>
          <Summary
            branch={branch}
            service={service}
            date={date}
            time={time}
            step={step}
          />
        </div>
        <section className="benefits" aria-label="Booking benefits">
          <div>
            <CalendarDays size={24} />
            <span>
              <strong>Your day, your schedule</strong>
              <small>Find a time that fits into your life.</small>
            </span>
          </div>
          <div>
            <Headphones size={24} />
            <span>
              <strong>Real people. Ready to help.</strong>
              <small>Personal support for your banking.</small>
            </span>
          </div>
          <div>
            <ShieldCheck size={24} />
            <span>
              <strong>Simple from the start</strong>
              <small>No booking fees. No unnecessary steps.</small>
            </span>
          </div>
        </section>
      </main>
      <footer>
        <div className="shell">
          <span>Better banking starts with you.</span>
          <span>
            Independent demonstration · Inspired by Capitec · No real
            appointments
          </span>
          <button onClick={() => setHelp(true)}>
            About this demo <ChevronRight size={13} />
          </button>
        </div>
      </footer>
      {help && <HelpDialog close={() => setHelp(false)} />}
    </>
  );
}

function MailIcon() {
  return (
    <span className="simulation-symbol" aria-hidden="true">
      i
    </span>
  );
}
