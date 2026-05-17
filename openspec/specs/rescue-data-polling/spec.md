# rescue-data-polling Specification

## Purpose
TBD - created by archiving change add-rescue-data-polling. Update Purpose after archive.
## Requirements
### Requirement: Periodic upstream polling
The system SHALL periodically fetch the rescue layer dataset from the configured upstream URL (`https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue` by default) as a background activity, independent of incoming HTTP requests.

#### Scenario: First poll after startup
- **WHEN** the application starts and the polling service is registered
- **THEN** the service SHALL perform an initial fetch immediately (without waiting for the first interval) so the cache is populated as soon as possible

#### Scenario: Recurring polls
- **WHEN** the configured interval elapses after a previous poll completes
- **THEN** the service SHALL issue another fetch to the upstream URL

#### Scenario: Application shutdown
- **WHEN** the host requests shutdown
- **THEN** the polling service SHALL stop within a reasonable grace period and SHALL NOT initiate new requests after the stop signal is received

### Requirement: Runtime-adjustable polling interval
The system SHALL allow the polling interval to be changed at runtime without restarting the application.

#### Scenario: Default interval
- **WHEN** no interval is explicitly configured
- **THEN** the polling service SHALL use a default interval of 5 minutes

#### Scenario: Interval change via configuration reload
- **WHEN** an operator edits the polling interval in the configuration source (e.g., `appsettings.json`) while the application is running
- **THEN** the next scheduled wait SHALL use the updated interval value, no application restart required

#### Scenario: Invalid interval value
- **WHEN** the configured interval is missing, zero, negative, or otherwise invalid
- **THEN** the service SHALL fall back to the default 5-minute interval and log a warning

### Requirement: In-memory snapshot of the latest data
The system SHALL keep the most recent successful response body in memory together with the timestamp of when it was fetched, SHALL make this snapshot available to API handlers in a thread-safe manner, and SHALL, after the snapshot is updated by a successful poll, hand off the new snapshot data to the monitor point event detector exactly once so that hit messages can be published to the in-process message bus before the next poll cycle is scheduled.

#### Scenario: Successful fetch updates the snapshot
- **WHEN** a polling iteration receives an HTTP 2xx response from the upstream
- **THEN** the snapshot SHALL be replaced atomically with the new payload and the fetch timestamp SHALL be updated to the time of completion

#### Scenario: Failed fetch preserves the previous snapshot
- **WHEN** a polling iteration fails (non-2xx response, timeout, network error, or deserialization failure)
- **THEN** the previously stored snapshot SHALL remain unchanged and the failure SHALL be recorded (last error message + timestamp) for diagnostics

#### Scenario: Successful fetch triggers monitor point event detection
- **WHEN** a polling iteration successfully replaces the snapshot
- **THEN** the polling service SHALL invoke the monitor point event detector exactly once with the new snapshot data, before awaiting the next polling interval tick

#### Scenario: Detector failure does not corrupt snapshot or stop polling
- **WHEN** the monitor point event detector throws while processing a successful snapshot
- **THEN** the snapshot SHALL remain the just-written successful payload, the error SHALL be logged at Error level, and the polling service SHALL proceed to wait for the next interval tick normally

### Requirement: Read API for the latest rescue data
The system SHALL expose an HTTP endpoint that returns the cached rescue data and metadata about its freshness.

#### Scenario: Cached data is available
- **WHEN** a client issues `GET /api/rescue/latest` after at least one successful poll
- **THEN** the response SHALL be HTTP 200 with a JSON body containing the cached upstream payload under a `data` field and metadata (`fetchedAt` timestamp, source URL) under a `meta` field

#### Scenario: No data available yet
- **WHEN** a client issues `GET /api/rescue/latest` before the first successful poll has completed
- **THEN** the response SHALL be HTTP 503 Service Unavailable with a JSON body containing an error message and, if available, the last error encountered

#### Scenario: Endpoint never proxies upstream synchronously
- **WHEN** the endpoint receives a request
- **THEN** it SHALL serve only from the in-memory snapshot and SHALL NOT make an outbound HTTP call to the upstream as part of the request handling

### Requirement: Resilient HTTP client behavior
The system SHALL apply a bounded timeout and a descriptive User-Agent on the upstream call so that the polling loop cannot hang indefinitely and the upstream operator can identify the caller.

#### Scenario: Upstream is slow
- **WHEN** the upstream takes longer than the configured request timeout (default 30 seconds)
- **THEN** the polling iteration SHALL abort the request, log a timeout, leave the snapshot unchanged, and reschedule the next iteration normally

#### Scenario: Identifiable User-Agent
- **WHEN** the polling iteration sends a request to the upstream
- **THEN** the request SHALL include a `User-Agent` header identifying this backend service (e.g., `ntpc-new-info-backend/<version>`)

