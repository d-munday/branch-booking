export interface Branch {
  id: string;
  name: string;
  address: string;
  city: string;
  province: string;
  hours: string;
}
export interface Service {
  id: string;
  name: string;
  description: string;
  durationMinutes: number;
}
export interface Slot {
  time: string;
  available: boolean;
}
export interface Availability {
  date: string;
  slots: Slot[];
}
export interface Details {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  consent: boolean;
}
export interface BookingRequest extends Details {
  branchId: string;
  serviceId: string;
  date: string;
  time: string;
}
export interface Receipt {
  id: string;
  accessToken: string;
  reference: string;
}
export interface Booking extends BookingRequest {
  id: string;
  reference: string;
  status: string;
  confirmation: {
    simulated: boolean;
    channel: string;
    subject: string;
    body: string;
  };
}
