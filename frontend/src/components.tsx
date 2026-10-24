import {
  CalendarDays,
  Check,
  CheckCircle2,
  ChevronRight,
  Clock3,
  FileCheck2,
  Mail,
  MapPin,
  ShieldCheck,
  X,
} from "lucide-react";
import type { Booking, Branch, Service } from "./types";
import { calendarFile, dateLabel } from "./booking";
import { useEffect, useRef } from "react";

export function Summary({
  branch,
  service,
  date,
  time,
  step,
}: {
  branch?: Branch;
  service?: Service;
  date: string;
  time: string;
  step: number;
}) {
  return (
    <aside className="sidebar" aria-label="Appointment summary">
      <div className="summary-card">
        <div className="summary-top">
          <span className="icon-square">
            <CalendarDays size={22} />
          </span>
          <span>YOUR APPOINTMENT</span>
        </div>
        <h2>
          A little time.
          <br />A lot taken care of.
        </h2>
        <div className="summary-item">
          <MapPin size={19} />
          <div>
            <span>Branch</span>
            <strong>{branch?.name || "Choose a branch"}</strong>
            {branch && <small>{branch.city}</small>}
          </div>
        </div>
        <div className="summary-item">
          <FileCheck2 size={19} />
          <div>
            <span>What we’re helping with</span>
            <strong>{service?.name || "Choose a service"}</strong>
          </div>
        </div>
        <div className="summary-item">
          <Clock3 size={19} />
          <div>
            <span>Date & time</span>
            <strong>
              {step > 0 && time
                ? `${dateLabel(date, true)} · ${time}`
                : "Let’s find a time that suits you"}
            </strong>
            <small>
              {service?.durationMinutes || 30} minute appointment · SAST
            </small>
          </div>
        </div>
        <div className="summary-bottom">
          <ShieldCheck size={17} /> No booking fees. Just better planning.
        </div>
      </div>
      <div className="bring-card">
        <span className="icon-circle">
          <FileCheck2 size={20} />
        </span>
        <h3>Come prepared</h3>
        <p>A few things to bring for a smoother visit.</p>
        <ul>
          <li>
            <Check size={15} /> Your original South African ID
          </li>
          <li>
            <Check size={15} /> Your cellphone
          </li>
          <li>
            <Check size={15} /> Any documents for your enquiry
          </li>
        </ul>
        <p className="small-note">
          We’ll let you know if anything else is needed.
        </p>
      </div>
    </aside>
  );
}

export function HelpDialog({ close }: { close: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  return (
    <dialog
      ref={dialog}
      className="help-dialog"
      onCancel={close}
      aria-labelledby="help-title"
    >
      <button className="close-button" onClick={close} aria-label="Close help">
        <X size={20} />
      </button>
      <span className="eyebrow">GOOD TO KNOW</span>
      <h2 id="help-title">Your visit, made simple.</h2>
      <details open>
        <summary>How does booking work?</summary>
        <p>
          Choose a branch and service, pick an available 30-minute slot, and add
          your contact details. You’ll receive an on-screen confirmation with
          your booking reference.
        </p>
      </details>
      <details>
        <summary>Will I receive an email or SMS?</summary>
        <p>
          This is a demonstration booking system. An email confirmation is
          simulated and saved with your appointment. No real message is sent.
        </p>
      </details>
      <details>
        <summary>When should I arrive?</summary>
        <p>
          Please arrive 5 minutes before your appointment. Bring your original
          ID, cellphone, and documents relevant to your enquiry.
        </p>
      </details>
      <details>
        <summary>Are these real branch appointments?</summary>
        <p>
          No. This independent concept is inspired by Capitec’s visual style.
          Branch listings and appointment availability are demonstration data.
        </p>
      </details>
      <button className="primary-button" onClick={close}>
        Got it <Check size={17} />
      </button>
    </dialog>
  );
}

export function Confirmation({
  booking,
  branch,
  service,
  restart,
}: {
  booking: Booking;
  branch: Branch;
  service: Service;
  restart: () => void;
}) {
  function downloadCalendar() {
    const url = URL.createObjectURL(
      new Blob([calendarFile(booking, branch.name, branch.address)], {
        type: "text/calendar;charset=utf-8",
      }),
    );
    const link = document.createElement("a");
    link.href = url;
    link.download = `${booking.reference}.ics`;
    link.click();
    URL.revokeObjectURL(url);
  }
  return (
    <section className="confirmation" aria-labelledby="confirmation-title">
      <div className="success-icon">
        <CheckCircle2 size={36} />
      </div>
      <span className="eyebrow">THAT’S ONE LESS THING TO DO</span>
      <h2 id="confirmation-title">You’re booked, {booking.firstName}.</h2>
      <p>We’ll be ready to help. Here’s everything you need for your visit.</p>
      <div className="ticket">
        <div className="ticket-top">
          <span>BOOKING REFERENCE</span>
          <strong>{booking.reference}</strong>
          <span className="confirmed-pill">
            <Check size={13} /> Confirmed
          </span>
        </div>
        <div className="ticket-details">
          <div>
            <MapPin size={19} />
            <span>
              <strong>{branch.name}</strong>
              <small>{branch.address}</small>
            </span>
          </div>
          <div>
            <CalendarDays size={19} />
            <span>
              <strong>{dateLabel(booking.date)}</strong>
              <small>
                {booking.time} SAST · {service.durationMinutes} minutes
              </small>
            </span>
          </div>
          <div>
            <FileCheck2 size={19} />
            <span>
              <strong>{service.name}</strong>
              <small>Please arrive 5 minutes early.</small>
            </span>
          </div>
        </div>
      </div>
      <div className="email-preview">
        <div className="email-heading">
          <Mail size={19} />
          <h3>Your confirmation email</h3>
          <span>SIMULATED</span>
        </div>
        <p className="email-to">To: {booking.email}</p>
        <strong>{booking.confirmation.subject}</strong>
        <p className="email-body">{booking.confirmation.body}</p>
        <p className="small-note">
          This confirmation is saved with your booking. No real email or SMS has
          been sent.
        </p>
      </div>
      <div className="confirmation-actions">
        <button className="primary-button" onClick={downloadCalendar}>
          <CalendarDays size={18} /> Add to calendar
        </button>
        <button className="text-button" onClick={() => window.print()}>
          Print confirmation
        </button>
      </div>
      <button className="text-button restart-button" onClick={restart}>
        Book another appointment <ChevronRight size={16} />
      </button>
    </section>
  );
}
