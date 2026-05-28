## MODIFIED Requirements

### Requirement: MonitorPointEventDetected message contract
The system SHALL define a `MonitorPointEventDetected` message type carrying enough information for a handler to identify which monitor point was hit, which rescue features matched, and when the detection occurred, so that handlers can act without re-querying the snapshot or the monitor point store.

#### Scenario: Message schema
- **WHEN** the detector publishes a `MonitorPointEventDetected`
- **THEN** the message SHALL contain: `MonitorPointId` (string), `MonitorPointName` (string), `MonitorPointLatitude` (double), `MonitorPointLongitude` (double), `MonitorPointRadius` (int, meters), `MatchedFeatures` (non-empty list, each entry containing `FeatureId` (string?), `Latitude` (double), `Longitude` (double), `Distance` (double, meters), and `Properties` (raw JSON node or null)), `DetectedAt` (DateTimeOffset), and `SnapshotFetchedAt` (DateTimeOffset)

#### Scenario: One message per hit monitor point
- **WHEN** a single poll iteration finds, for N distinct monitor points (N >= 1), at least one feature that is new since that monitor point's previous successful detection
- **THEN** the detector SHALL publish exactly N messages, one per such monitor point, each containing only the features that newly hit that specific monitor point this iteration
- **AND** a monitor point whose current hits were ALL already notified in a previous iteration SHALL NOT receive a message

## ADDED Requirements

### Requirement: Notification deduplication per monitor point
The system SHALL track, per monitor point, the set of feature ids it has already notified, and SHALL only include in a published `MonitorPointEventDetected` the features whose ids are new for that monitor point since its previous successful detection. The system SHALL key deduplication state by `(monitorPointId, featureId)` so that the same feature hitting different monitor points is evaluated independently. Deduplication SHALL be applied in the detector so that all subscribers (logging and Discord handlers) observe the same new-only stream. The state MAY be held in memory only.

#### Scenario: Persisting hit is not re-notified
- **WHEN** a feature hit a monitor point in a previous successful detection and the same feature (same feature id) is still within that monitor point's radius in the current detection
- **THEN** that feature SHALL NOT be included again for that monitor point, and if it is the monitor point's only current hit, no message SHALL be published for that monitor point

#### Scenario: New hit is notified
- **WHEN** a feature is within a monitor point's radius in the current detection and its feature id was not among that monitor point's previously notified ids
- **THEN** that feature SHALL be included in a published `MonitorPointEventDetected` for that monitor point

#### Scenario: Feature leaves and returns
- **WHEN** a feature that was previously notified for a monitor point is absent from that monitor point's hits in a later detection, and then hits that monitor point again in a subsequent detection
- **THEN** the returning hit SHALL be treated as new and notified again

#### Scenario: Independent per monitor point
- **WHEN** the same feature hits two different monitor points
- **THEN** each monitor point SHALL evaluate that feature against its own previously notified ids independently (a notification for one monitor point SHALL NOT suppress it for the other)

#### Scenario: Deleted monitor point state is dropped
- **WHEN** a monitor point that previously had notified ids no longer exists in the monitor point store
- **THEN** the detector SHALL discard that monitor point's deduplication state, so re-creating a monitor point starts with an empty notified set

#### Scenario: Feature without a feature id
- **WHEN** a matched feature has no resolvable feature id
- **THEN** the detector SHALL treat it as new on every detection (it SHALL NOT participate in deduplication)
