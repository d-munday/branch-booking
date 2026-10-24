\set ON_ERROR_STOP on
CREATE ROLE booking_command LOGIN PASSWORD :'command_password';
CREATE ROLE booking_query LOGIN PASSWORD :'query_password';
REVOKE ALL ON DATABASE booking FROM PUBLIC;
GRANT CONNECT ON DATABASE booking TO booking_command, booking_query;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO booking_command, booking_query;

CREATE TABLE branches (
    id text PRIMARY KEY,
    name text NOT NULL,
    address text NOT NULL,
    city text NOT NULL,
    province text NOT NULL,
    hours text NOT NULL,
    sort_order integer NOT NULL
);
CREATE TABLE services (
    id text PRIMARY KEY,
    name text NOT NULL,
    description text NOT NULL,
    duration_minutes integer NOT NULL CHECK (duration_minutes = 30),
    sort_order integer NOT NULL
);
CREATE TABLE bookings (
    id uuid PRIMARY KEY,
    reference text NOT NULL,
    branch_id text NOT NULL REFERENCES branches(id),
    service_id text NOT NULL REFERENCES services(id),
    appointment_date date NOT NULL,
    appointment_time time NOT NULL,
    first_name varchar(80) NOT NULL CHECK (length(trim(first_name)) > 0),
    last_name varchar(80) NOT NULL CHECK (length(trim(last_name)) > 0),
    email varchar(254) NOT NULL,
    phone varchar(20) NOT NULL,
    access_token_hash char(64) NOT NULL,
    confirmation_subject text NOT NULL,
    confirmation_body text NOT NULL,
    consent_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_slot UNIQUE (branch_id, appointment_date, appointment_time),
    CONSTRAINT valid_slot CHECK (
        extract(isodow FROM appointment_date) <> 7
        AND appointment_time >= TIME '08:00'
        AND appointment_time < CASE WHEN extract(isodow FROM appointment_date) = 6 THEN TIME '12:30' ELSE TIME '16:30' END
        AND extract(minute FROM appointment_time) IN (0,30)
        AND extract(second FROM appointment_time) = 0
    )
);

INSERT INTO branches VALUES
('cape-town-cbd','Cape Town CBD','22 Adderley Street, Cape Town','Cape Town','Western Cape','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',1),
('sea-point','Sea Point','94 Regent Road, Sea Point','Cape Town','Western Cape','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',2),
('claremont','Claremont','Cavendish Square, Dreyer Street','Cape Town','Western Cape','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',3),
('bellville','Bellville','Voortrekker Road, Bellville','Cape Town','Western Cape','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',4),
('sandton','Sandton City','Sandton City, Rivonia Road','Johannesburg','Gauteng','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',5),
('rosebank','Rosebank','The Zone, Oxford Road','Johannesburg','Gauteng','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',6),
('pretoria-central','Pretoria Central','Church Square, Pretoria','Pretoria','Gauteng','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',7),
('durban-central','Durban Central','357 West Street, Durban','Durban','KwaZulu-Natal','Mon–Fri 08:00–16:30 · Sat 08:00–12:30',8);

INSERT INTO services VALUES
('everyday-banking','Everyday banking','Get help with your account, payments or the app.',30,1),
('open-account','Open an account','Start your journey with a new personal account.',30,2),
('credit','Credit and loans','Explore credit options that work for you.',30,3),
('savings','Savings and investments','Make a plan for what matters to you.',30,4),
('card-help','Card assistance','Replace a card or get help with your PIN.',30,5),
('business-banking','Business banking','Simple support for your business banking.',30,6);

GRANT INSERT ON bookings TO booking_command;
GRANT SELECT ON branches, services, bookings TO booking_query;
ALTER ROLE booking_query SET default_transaction_read_only = on;
