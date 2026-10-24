"""Generate unique local Docker secrets without overwriting an existing configuration."""
from pathlib import Path
import secrets

target = Path(__file__).resolve().parent.parent / ".env"
with target.open("x", encoding="utf-8", newline="\n") as output:
    for name in ("POSTGRES_PASSWORD", "COMMAND_DB_PASSWORD", "QUERY_DB_PASSWORD", "BOOKING_TOKEN_SECRET"):
        output.write(f"{name}={secrets.token_hex(32)}\n")
    output.write("APP_PORT=8080\n")
print("Created .env with independent generated secrets. Keep it private.")
