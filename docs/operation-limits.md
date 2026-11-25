# Expensive operation limits

The API applies fixed one-minute request budgets to external edition searches,
ISBN fetches/import submissions/retries and catalogue/author maintenance starts.
Ordinary catalogue reads, import progress polling and provider readiness remain
available when these budgets are exhausted. Existing `account-entry` protection
continues to apply to login and registration.

Configure these positive integers in application configuration (or environment
variables with `ExpensiveOperationLimits__` as the prefix):

| Setting | Default | Scope |
| --- | --- | --- |
| `RequestsPerMinutePerAccount` | 60 | Shared by an account across IP addresses and expensive routes |
| `RequestsPerMinutePerIp` | 180 | Shared by accounts using the same resolved client IP |
| `ConcurrentRequestsPerAccount` | 2 | Active expensive HTTP requests for one account |
| `ConcurrentRequests` | 8 | Active expensive HTTP requests in one API process |

Requests do not wait in an HTTP queue. Rejection returns HTTP 429, a ProblemDetails
body, `Retry-After` in seconds and `Cache-Control: no-store`. Concurrency rejection
uses a one-second retry hint; window rejection reports the remaining window.
Clients should honor the hint and preserve the user's input.

Register `services.AddExpensiveOperationLimits(configuration)` after registering
existing rate limiter policies. Run forwarded headers before authentication and
run the rate limiter after authentication/authorization so account identity is
available and unauthorized requests do not consume authenticated budgets.

The IP key uses `Connection.RemoteIpAddress` after trusted forwarding middleware;
it never reads raw `X-Forwarded-For`. Configure only the actual reverse proxy in
`Authentication:KnownProxies`. Do not trust arbitrary proxy headers or expose an
API port around the trusted ingress. IPv4 and mapped IPv6 representations share
a budget.

The existing durable import queue separately limits unfinished book work to 100
items, checks capacity in its serializable enqueue transaction and processes items
sequentially. Refresh jobs retain their persisted active-slot/lease constraints;
these HTTP concurrency limits do not release or cancel accepted durable jobs.
The limiter state is local to an API process and resets on restart. Multiple API
replicas require an ingress/distributed budget as well; these local limits are
not a cluster-wide quota.
