# Validation — 24 October 2026

| Check | Observed result |
| --- | --- |
| Backend .NET 10 suite | 139 passed, zero skipped |
| Backend line / branch coverage | 96.34% / 91.06%, all three production assemblies included |
| Frontend Vitest suite | 63 passed |
| Frontend line / branch / function coverage | 99.91% / 99.00% / 94.11% |
| TypeScript strict compilation | Passed |
| Vite production build | Passed |
| Locked production npm dependency audit | Zero vulnerabilities reported |
| PostgreSQL integration | Actual PostgreSQL 18.4, isolated native Windows instance |
| Concurrent allocation | 16 simultaneous writes to one slot: one winner |
| Database privilege boundary | Writer SELECT denied; reader writes denied |
| Capability attack regressions | Changed request or known public ID cannot retrieve original private receipt |
| Browser journey | Service, branch, weekday, time, details, consent, persisted confirmation: passed |
| Responsive review | Desktop and 390px mobile layouts inspected; mobile content does not overflow horizontally |
| Persistence in browser | Previously reserved slot remains unavailable after API restart |
| Container builds / Compose startup | Passed on Rancher Desktop: all three application images built, all four containers running |
| Containerized end-to-end smoke test | Passed through Windows localhost:8080: catalogs, availability, booking, saved simulated confirmation, replay, recovery, conflicts and private access |
| Container restart persistence | Passed: original booking and confirmation readable after restarting all four containers |
| Production browser load | Passed at localhost:8080, React rendered all eight branches; no browser errors or warnings observed |
| Browser calendar download | Automation stalled; download completion is unverified. Calendar contents, escaping, UTC conversion and click handler are covered by tests. |

Reports are generated in ignored `artifacts/` and `frontend/coverage/`. Reproduce them with the commands in README.md. Both coverage gates require at least 80%; no production API/domain/repository assemblies are excluded. Frontend excludes the React bootstrap, type declarations and test infrastructure.

Confirmation transport is deliberately simulated. The UI shows the saved email content and explicitly states that no actual email or SMS was sent. The illustrated branch catalog is sample data.

The Docker deployment is a single-host baseline with independently scalable stateless APIs. README.md describes the deployment work needed for real public traffic, including TLS, a highly available database, identity/abuse controls, retention, backups and monitoring.

## Rancher Desktop follow-up

Rancher Desktop 1.24.0 includes Docker and Compose under `C:\Program Files\Rancher Desktop\resources\resources\win32\`; they were absent from the shell PATH. The earlier statement that Docker was not installed was incorrect. Rancher initially reported `ERROR` with an incomplete containerd startup. Its application was shut down and relaunched with Docker/Moby selected, preserving its WSL distributions and container data.

The Windows Docker/Compose client successfully built all three application images against Rancher's Docker 29.5.3 daemon. Its Windows API proxy subsequently timed out dialing a Hyper-V socket. The daemon remained healthy inside Rancher's WSL distribution. Using Rancher's bundled Linux Compose executable directly through WSL successfully started the four-service stack. Windows browser and API access work at `http://localhost:8080`.

`scripts/smoke-test.py --state-file .local/rancher-smoke-state.json` passed the full deployed API journey. After restarting all four containers, `--verify-state-file .local/rancher-smoke-state.json` confirmed that the same booking and simulated confirmation persisted. All three application containers run as non-root with read-only filesystems. The stack is left running for inspection. README.md includes the working WSL Compose commands; no factory reset or deletion of existing Rancher data was performed.
