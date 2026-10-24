"""Exercise the deployed booking stack using synthetic data and Python's standard library."""
import argparse
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, urlopen
from uuid import UUID, uuid4


def require(condition, description):
    if not condition:
        raise RuntimeError(description)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:8080")
    state = parser.add_mutually_exclusive_group()
    state.add_argument("--state-file", type=Path, help="Save the synthetic request and private receipt for restart verification; use ignored .local/.")
    state.add_argument("--verify-state-file", type=Path, help="Read a previously saved receipt and verify persistence without creating another appointment.")
    args = parser.parse_args()
    base_url = args.base_url.rstrip("/")
    address = urlsplit(base_url)
    require(address.scheme in ("http", "https") and address.hostname
            and not address.username and not address.password
            and not address.query and not address.fragment,
            "Use an HTTP(S) base URL without credentials, query parameters or fragments.")
    if args.state_file:
        require(not args.state_file.exists(), "The state file already exists; choose another file or use --verify-state-file.")

    def call(method, path, body=None, headers=None):
        data = json.dumps(body).encode("utf-8") if body is not None else None
        request_headers = {"Accept": "application/json", **(headers or {})}
        if data is not None:
            request_headers["Content-Type"] = "application/json"
        request = Request(base_url + path, data=data, headers=request_headers, method=method)
        try:
            response = urlopen(request, timeout=15)
        except HTTPError as failure:
            response = failure
        with response:
            status = response.status
            content = response.read().decode("utf-8")
            result = json.loads(content) if "json" in response.headers.get("Content-Type", "") else content
        time.sleep(0.22)
        return status, result

    def check_saved(body, receipt):
        status, booking = call("GET", "/api/bookings/" + receipt["id"],
                               headers={"Authorization": "Bearer " + receipt["accessToken"]})
        require(status == 200, "The saved appointment must be readable using its receipt capability.")
        require(all(booking.get(name) == value for name, value in body.items() if name != "consent"),
                "The persisted appointment must match all submitted booking and contact details.")
        require(booking["id"] == receipt["id"] and booking["reference"] == receipt["reference"]
                and booking["status"] == "confirmed", "The saved appointment must be confirmed with the original reference.")
        confirmation = booking["confirmation"]
        require(confirmation["simulated"] is True and confirmation["channel"] == "email"
                and confirmation["subject"] and receipt["reference"] in confirmation["body"],
                "The appointment must include its persisted simulated email confirmation.")

    status, page = call("GET", "/")
    require(status == 200 and isinstance(page, str) and 'id="root"' in page,
            "The React entry page must load through the web gateway.")
    print("PASS React entry page through the production gateway")

    if args.verify_state_file:
        saved = json.loads(args.verify_state_file.read_text(encoding="utf-8"))
        check_saved(saved["request"], saved["receipt"])
        print(f"PASS saved appointment and confirmation survive restart; public booking reference: {saved['receipt']['reference']}")
        return

    status, branches = call("GET", "/api/branches")
    require(status == 200 and isinstance(branches, list) and branches,
            "The query API must return at least one branch.")
    status, services = call("GET", "/api/services")
    require(status == 200 and isinstance(services, list) and services,
            "The query API must return at least one service.")
    print("PASS branch and service catalogs")

    branch_id = branches[0]["id"]
    service_id = services[0]["id"]
    today = datetime.now(timezone(timedelta(hours=2))).date()
    selected_date = selected_time = None
    for days in range(1, 31):
        day = (today + timedelta(days=days)).isoformat()
        status, available = call("GET", "/api/availability?" + urlencode({"branchId": branch_id, "date": day}))
        require(status == 200, "The availability query must succeed for a date in the booking window.")
        free_slots = [slot["time"] for slot in available["slots"] if slot["available"]]
        if free_slots:
            selected_date, selected_time = day, free_slots[0]
            break
    require(selected_time is not None, "No free slot was found within the booking window.")
    print("PASS future branch availability")

    key = str(uuid4())
    body = {"branchId": branch_id, "serviceId": service_id,
            "date": selected_date, "time": selected_time,
            "firstName": "Alex", "lastName": "Smoke",
            "email": f"qa+{uuid4().hex}@example.com", "phone": "0821234567", "consent": True}
    status, receipt = call("POST", "/api/bookings", body, {"Idempotency-Key": key})
    require(status == 201, f"Creating a booking must return HTTP 201, received {status}.")
    UUID(receipt["id"])
    require(len(receipt["accessToken"]) == 43 and receipt["id"] != key,
            "The receipt must have a separate public ID and private capability.")
    booking_path = "/api/bookings/" + receipt["id"]
    check_saved(body, receipt)
    print("PASS create, persist, retrieve and simulate confirmation")

    status, replay = call("POST", "/api/bookings", body, {"Idempotency-Key": key})
    require(status == 202 and replay == receipt,
            "Replaying the original request and key must return HTTP 202 with the same receipt.")
    status, recovered = call("POST", "/api/bookings/receipt", body, {"Idempotency-Key": key})
    require(status == 200 and recovered == receipt, "Receipt recovery must reproduce the original capability.")
    print("PASS idempotent replay and receipt recovery")

    status, _ = call("POST", "/api/bookings", body, {"Idempotency-Key": str(uuid4())})
    require(status == 409, "A different booking request for the occupied slot must return HTTP 409.")
    status, availability = call("GET", "/api/availability?" + urlencode({"branchId": branch_id, "date": selected_date}))
    require(status == 200 and any(slot["time"] == selected_time and not slot["available"] for slot in availability["slots"]),
            "The reserved slot must disappear from available times.")
    print("PASS duplicate-slot protection and updated availability")

    status, _ = call("GET", booking_path)
    require(status == 404, "A booking request without a capability must return HTTP 404.")
    changed_body = {**body, "firstName": "Different"}
    status, changed_receipt = call("POST", "/api/bookings/receipt", changed_body, {"Idempotency-Key": key})
    require(status == 200 and changed_receipt["accessToken"] != receipt["accessToken"],
            "A changed payload must derive a different capability.")
    status, _ = call("GET", booking_path, headers={"Authorization": "Bearer " + changed_receipt["accessToken"]})
    require(status == 404, "A capability from a changed request must not retrieve the original appointment.")
    print("PASS private receipt access and changed-payload protection")
    if args.state_file:
        args.state_file.parent.mkdir(parents=True, exist_ok=True)
        with args.state_file.open("x", encoding="utf-8") as output:
            json.dump({"request": body, "receipt": receipt}, output, indent=2)
    print(f"PASS full-stack smoke test; public booking reference: {receipt['reference']}")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, TimeoutError, ValueError, KeyError, TypeError) as failure:
        if isinstance(failure, URLError):
            sys.exit("FAIL Cannot reach the stack. Check that the web gateway and both APIs are running.")
        sys.exit(f"FAIL {failure}")
