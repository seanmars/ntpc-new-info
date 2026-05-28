# monitor-point-event-alerts Specification

## Purpose
TBD - created by archiving change add-monitor-point-event-alerts. Update Purpose after archive.
## Requirements
### Requirement: In-process message bus for monitor point alerts
The system SHALL host an in-process message bus implemented with Rebus and the in-memory transport, expose `IBus` for publishing, and auto-register any `IHandleMessages<MonitorPointEventDetected>` handlers from the WebApi assembly.

#### Scenario: Bus and handlers are available at startup
- **WHEN** the application starts
- **THEN** the DI container SHALL resolve `IBus`, and at least the built-in logging handler `IHandleMessages<MonitorPointEventDetected>` SHALL be wired so that any `IBus.Publish<MonitorPointEventDetected>` invocation reaches the handler in-process

#### Scenario: No external broker required
- **WHEN** the application is started in any environment (Development, Production, Test)
- **THEN** the bus SHALL operate over an in-memory transport bound to a single in-process `InMemNetwork`, requiring no external broker/connection string

### Requirement: MonitorPointEventDetected message contract
The system SHALL define a `MonitorPointEventDetected` message type carrying enough information for a handler to identify which monitor point was hit, which rescue features matched, and when the detection occurred, so that handlers can act without re-querying the snapshot or the monitor point store.

#### Scenario: Message schema
- **WHEN** the detector publishes a `MonitorPointEventDetected`
- **THEN** the message SHALL contain: `MonitorPointId` (string), `MonitorPointName` (string), `MonitorPointLatitude` (double), `MonitorPointLongitude` (double), `MonitorPointRadius` (int, meters), `MatchedFeatures` (non-empty list, each entry containing `FeatureId` (string?), `Latitude` (double), `Longitude` (double), `Distance` (double, meters), and `Properties` (raw JSON node or null)), `DetectedAt` (DateTimeOffset), and `SnapshotFetchedAt` (DateTimeOffset)

#### Scenario: One message per hit monitor point
- **WHEN** a single poll iteration finds, for N distinct monitor points (N >= 1), at least one feature that is new since that monitor point's previous successful detection
- **THEN** the detector SHALL publish exactly N messages, one per such monitor point, each containing only the features that newly hit that specific monitor point this iteration
- **AND** a monitor point whose current hits were ALL already notified in a previous iteration SHALL NOT receive a message

### Requirement: Hit detection rule for monitor points
The system SHALL determine that a rescue feature "hits" a monitor point WHEN the great-circle (haversine) distance between the monitor point coordinates and at least one coordinate point belonging to the feature's geometry is less than or equal to the monitor point `radius` in meters.

#### Scenario: Point geometry within radius
- **WHEN** a feature has `geometry.type == "Point"` and the haversine distance from the monitor point to `geometry.coordinates` is <= `radius`
- **THEN** the feature SHALL be reported as matched for that monitor point, with `Distance` equal to the computed haversine distance in meters

#### Scenario: Point geometry outside radius
- **WHEN** a feature has `geometry.type == "Point"` and the haversine distance to the monitor point is > `radius`
- **THEN** the feature SHALL NOT be reported as matched for that monitor point

#### Scenario: Multi-vertex geometry (LineString/Polygon/MultiPolygon)
- **WHEN** a feature has a geometry whose coordinates expand to multiple vertices
- **THEN** the feature SHALL be reported as matched if ANY vertex distance is <= `radius`; the reported `Distance`, `Latitude` and `Longitude` SHALL correspond to the closest vertex among those within range

#### Scenario: Missing or unparseable geometry
- **WHEN** a feature has no geometry, a null geometry, an unsupported geometry type, or invalid coordinates
- **THEN** the detector SHALL skip that feature WITHOUT throwing, and SHALL log a warning identifying the snapshot fetch time and the feature index

#### Scenario: GeoJSON coordinate order
- **WHEN** the detector reads `coordinates` from any GeoJSON geometry
- **THEN** the detector SHALL interpret the order as `[longitude, latitude]` (per RFC 7946), NOT `[latitude, longitude]`

### Requirement: Trigger event detection after each successful poll
The system SHALL invoke the monitor point event detector exactly once after each successful rescue data poll iteration (i.e., after the snapshot has been updated with success), and SHALL NOT invoke it on failed iterations.

#### Scenario: Successful poll triggers detection
- **WHEN** a polling iteration completes successfully and the snapshot is replaced with new data
- **THEN** the detector SHALL be invoked once with that new snapshot data, and any resulting hits SHALL be published to the bus before the next poll cycle is awaited

#### Scenario: Failed poll skips detection
- **WHEN** a polling iteration fails (timeout, non-2xx, deserialization error, etc.)
- **THEN** the detector SHALL NOT be invoked for that iteration, and no `MonitorPointEventDetected` messages SHALL be published as a result of that iteration

#### Scenario: No monitor points configured
- **WHEN** the detector runs but the monitor point store is empty
- **THEN** the detector SHALL complete without publishing any messages and SHALL NOT throw

#### Scenario: No hits for any monitor point
- **WHEN** the detector runs against a snapshot whose features have no point within any monitor point's radius
- **THEN** no `MonitorPointEventDetected` messages SHALL be published

### Requirement: Detection errors must not break the poll loop
The system SHALL isolate detector failures so that an exception thrown during detection or publication does NOT propagate into the polling service's main loop, and SHALL NOT prevent the snapshot from remaining available or the next polling iteration from being scheduled.

#### Scenario: Detector throws
- **WHEN** the detector throws while parsing features, computing distances, or publishing to the bus
- **THEN** the polling service SHALL log the error at Error level, SHALL leave the just-written successful snapshot intact, and SHALL proceed to await the next interval tick normally

### Requirement: Built-in logging handler
The system SHALL register a default `IHandleMessages<MonitorPointEventDetected>` implementation that logs every received message at Information level using structured logging, with no other side effects.

#### Scenario: Handler logs the message
- **WHEN** the bus delivers a `MonitorPointEventDetected` to the default handler
- **THEN** the handler SHALL emit a single Information-level log entry that includes the monitor point id, name, the count of matched features, and the detection timestamp, using structured logging fields (NOT only a pre-formatted string)

#### Scenario: Handler is non-intrusive
- **WHEN** the default handler processes a message
- **THEN** it SHALL NOT perform HTTP calls, file I/O, or any external side effect beyond writing to the configured `ILogger`

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

