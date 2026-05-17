## MODIFIED Requirements

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
