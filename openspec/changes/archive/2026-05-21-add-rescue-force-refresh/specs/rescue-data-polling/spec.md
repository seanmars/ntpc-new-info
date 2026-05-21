## ADDED Requirements

### Requirement: On-demand synchronous upstream refresh endpoint

The system SHALL expose an HTTP endpoint `POST /api/rescue/refresh` that synchronously triggers a single upstream fetch (bypassing the background polling cadence), updates the in-memory snapshot on success, and returns the new snapshot payload. The endpoint SHALL share its execution path with the scheduled polling service so that snapshot updates, failure recording, monitor point event detection, and all-alerts detection occur identically regardless of whether the trigger was scheduled or manual.

#### Scenario: Successful manual refresh returns fresh data

- **WHEN** a client issues `POST /api/rescue/refresh` and the upstream responds 2xx within the request timeout
- **THEN** the snapshot SHALL be replaced atomically with the new payload, the fetch timestamp SHALL be updated, the response SHALL be HTTP 200 with a JSON body containing `data` (the new upstream payload) and `meta` (`fetchedAt`, `lastError`, `lastErrorAt`) using the same shape as `GET /api/rescue/latest`

#### Scenario: Manual refresh triggers detectors exactly like scheduled poll

- **WHEN** a manual refresh successfully replaces the snapshot
- **THEN** the system SHALL invoke the monitor point event detector exactly once with the new snapshot data and SHALL invoke the rescue all-alerts detector exactly once, in the same order and with the same semantics as a scheduled poll

#### Scenario: Manual refresh fetch failure

- **WHEN** a client issues `POST /api/rescue/refresh` and the upstream fetch fails (non-2xx response, timeout, network error, or deserialization failure)
- **THEN** the previously stored snapshot SHALL remain unchanged, the failure SHALL be recorded (`lastError` + `lastErrorAt`), and the response SHALL be HTTP 502 Bad Gateway with a JSON body containing an `error` message, `lastError`, and `lastErrorAt`

#### Scenario: Manual refresh during application shutdown

- **WHEN** the host requests shutdown while a manual refresh is in flight
- **THEN** the refresh SHALL honor the cancellation token and abort within a reasonable grace period, and the endpoint SHALL NOT initiate new upstream requests after the stop signal is received

### Requirement: Mutual exclusion between manual and scheduled refresh

The system SHALL serialize manual and scheduled refresh executions so that at most one upstream fetch issued by this service is in flight at any moment. A manual refresh arriving while another refresh (manual or scheduled) is in progress SHALL wait for a bounded time for the in-flight execution to complete; if the wait exceeds the bound, the manual refresh SHALL be rejected with HTTP 429 Too Many Requests rather than initiating a second concurrent upstream call.

#### Scenario: Manual refresh while scheduled poll is running

- **WHEN** a manual refresh arrives while a scheduled poll is currently executing
- **THEN** the manual refresh SHALL wait up to a bounded period (no longer than the upstream request timeout) for the scheduled poll to finish; if the in-flight poll completes within the wait, the manual refresh SHALL proceed to evaluate cooldown and then execute (or reject) according to the standard rules; if the wait elapses, the manual refresh SHALL return HTTP 429 with a `Retry-After` header

#### Scenario: Two concurrent manual refreshes

- **WHEN** two manual refresh requests arrive simultaneously
- **THEN** at most one SHALL acquire the execution slot and proceed; the other SHALL either wait briefly for the first to finish (then be subject to cooldown) or be rejected with HTTP 429; under no circumstance SHALL both issue independent upstream calls

### Requirement: Manual refresh cooldown

The system SHALL enforce a minimum interval between successful manual refreshes (default 15 seconds) to protect the upstream API from being polled excessively. The cooldown SHALL apply only to manual triggers; scheduled polls SHALL NOT be affected. The cooldown duration SHALL be configurable via `RescuePolling:ForceRefreshCooldownSeconds` and SHALL be reloadable at runtime via configuration reload, with invalid values falling back to the default and a warning being logged. Cooldown SHALL be measured from the timestamp of the most recent successful manual or scheduled fetch; a failed manual refresh SHALL NOT extend or reset the cooldown.

#### Scenario: Manual refresh within cooldown window

- **WHEN** a client issues `POST /api/rescue/refresh` and the elapsed time since the most recent successful fetch is less than the configured cooldown
- **THEN** the system SHALL NOT issue an upstream request and SHALL return HTTP 429 with a `Retry-After` header (seconds remaining, rounded up) and a JSON body containing `error` (a human-readable message) and `retryAfterSeconds`

#### Scenario: Manual refresh after cooldown elapses

- **WHEN** the elapsed time since the most recent successful fetch exceeds the configured cooldown
- **THEN** the next manual refresh SHALL be permitted to execute

#### Scenario: Cooldown does not throttle scheduled poll

- **WHEN** the scheduled polling interval elapses while the manual cooldown window is still active
- **THEN** the scheduled poll SHALL proceed normally and SHALL NOT be rejected by the cooldown gate

#### Scenario: Failed manual refresh does not reset cooldown

- **WHEN** a manual refresh executes but the upstream fetch fails
- **THEN** the cooldown timestamp SHALL NOT be updated by this failure, and the next manual refresh SHALL be permitted immediately (subject only to the previous successful fetch's cooldown timestamp)

#### Scenario: Default cooldown when option missing or invalid

- **WHEN** the configuration value `RescuePolling:ForceRefreshCooldownSeconds` is missing, zero, negative, or otherwise invalid
- **THEN** the system SHALL use the default cooldown (15 seconds) and SHALL log a warning if an invalid value was provided

#### Scenario: Cooldown change via configuration reload

- **WHEN** an operator edits `RescuePolling:ForceRefreshCooldownSeconds` while the application is running
- **THEN** subsequent manual refresh requests SHALL evaluate against the updated cooldown value without requiring an application restart
