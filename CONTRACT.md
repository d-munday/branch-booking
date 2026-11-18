# Booking contract

All API paths use `/api`. JSON uses camelCase. Dates and times are branch-local Africa/Johannesburg (UTC+02:00), ISO `YYYY-MM-DD` and `HH:mm`. A booking is one 30-minute appointment, one customer per branch/time. Sundays closed, weekdays 08:00–16:30, Saturdays 08:00–12:30. Slots are start times, closing time exclusive. Book 1 hour ahead through 30 days inclusive.

`GET /api/branches` => array of `{id,name,address,city,province,hours}`. Seed IDs: `cape-town-cbd`, `sea-point`, `claremont`, `bellville`, `sandton`, `rosebank`, `pretoria-central`, `durban-central`.

`GET /api/services` => array of `{id,name,description,durationMinutes}`. IDs: `everyday-banking`, `open-account`, `credit`, `savings`, `card-help`, `business-banking`. All 30 minutes for the bounded appointment system.

`GET /api/availability?branchId=...&date=...` => `{date,slots:[{time,available}]}`. Existing reservations unavailable. Invalid date/branch => 400. Sunday => empty slots.

`POST /api/bookings` with private `Idempotency-Key` UUID header and body `{branchId,serviceId,date,time,firstName,lastName,email,phone,consent}` => 201 `{id,accessToken,reference}`. Validation errors => 400 ProblemDetails with `errors` map. Occupied slot => 409. For replay of same key and normalized body, 202 with same receipt, client GET verifies saved request. Changed requests with a reused key never create new bookings and receive a token that cannot read the original booking. The public booking UUID is an HMAC-derived identity distinct from the private idempotency key; the token is bound to the key and normalized payload.

`POST /api/bookings/receipt` with the original body and private `Idempotency-Key` => 200 `{id,accessToken,reference}`. This stateless recovery endpoint validates structural input and original slot shape but ignores the current booking window, so a lost response remains recoverable after the appointment time expires. It does not connect to, read or write the database. The client must GET the booking using this receipt to establish that the booking was committed; no matching saved booking or a changed payload gives 404. Both POST endpoints are rate-limited.

`GET /api/bookings/{id}` with `Authorization: Bearer <accessToken>` => `{id,reference,branchId,serviceId,date,time,firstName,lastName,email,phone,status,confirmation:{simulated,channel,subject,body}}`. Unknown/incorrect token => 404. Status `confirmed`; confirmation channel `email`, simulated `true`. No real external email/SMS is sent. Front end renders persisted confirmation. Do not put access tokens in URLs or logs.

Health: `/health/live` process liveness and `/health/ready` connection readiness (no table query required on insert-only writer). Transient database failures return 503. Unexpected permanent database failures return 500 with a trace ID for support.

Database roles: command service INSERT-only on bookings; query service SELECT-only on catalog and bookings. No service uses the schema-owner role. Schema migrations are a distinct one-shot administration task. Command API MUST NOT issue SELECT, UPDATE, DELETE, or INSERT RETURNING (RETURNING requires SELECT privileges). Receipt generated in application before INSERT. Shared database constraints enforce branch/service integrity and unique slot allocation across replicas.

Frontend stack: React + TypeScript + Vite, native fetch, lucide-react icons; Vitest and Testing Library. Backend: .NET 10, Npgsql, xUnit and Microsoft's Code Coverage collector. Root Dockerfile for both .NET services via build argument SERVICE, frontend/Dockerfile for nginx/Vite. Compose routes APIs through nginx without CORS. The durable simulated confirmation is inserted atomically with its booking.
