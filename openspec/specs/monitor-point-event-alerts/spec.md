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
- **WHEN** a single poll iteration finds matches for N distinct monitor points (N >= 1)
- **THEN** the detector SHALL publish exactly N messages, one per monitor point, each containing only the features that hit that specific monitor point

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
