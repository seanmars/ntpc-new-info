## MODIFIED Requirements

### Requirement: Monitor point event Discord handler
The system SHALL register an `IHandleMessages<MonitorPointEventDetected>` implementation that, for each delivered bus message, formats a human-readable Discord message describing the monitor point hit and sends it via `IDiscordNotifier` exactly once, and SHALL isolate notifier failures so that the bus does NOT retry or dead-letter the message.

#### Scenario: One Discord message per delivered bus message
- **WHEN** the in-process bus delivers a single `MonitorPointEventDetected` to the handler
- **THEN** the handler SHALL invoke `IDiscordNotifier.SendAsync` exactly once

#### Scenario: Message content
- **WHEN** the handler formats a `MonitorPointEventDetected`
- **THEN** the resulting Discord message SHALL include the monitor point name, the count of matched features, and the detection time
- **AND** for each emitted matched feature the message SHALL include, when the corresponding value is present in the feature `Properties`: the disaster type (`fireType`, falling back to `title`), the location (`endPointInfo`), the event id (`featureId` if present, else `lat,lng`), the distance in meters, the feature type (`type`), the data source (`dataSource`), and a summary of dispatched vehicles from `caseList` (`startPointInfo` values)
- **AND** any `Properties` value that is missing, empty, or unreadable SHALL be omitted from the message WITHOUT throwing

#### Scenario: Dispatched vehicle cap
- **WHEN** a matched feature's `caseList` contains more than 5 entries
- **THEN** the message SHALL list at most the first 5 `startPointInfo` values for that feature and SHALL represent the remainder with a single `...其他 N 車` indicator

#### Scenario: Feature cap
- **WHEN** a `MonitorPointEventDetected` carries more than 5 matched features
- **THEN** the message SHALL emit detailed content for at most the first 5 features and SHALL represent any remaining features with a single `...and N more` line

#### Scenario: Message length safeguard
- **WHEN** appending another matched feature's detailed content would push the message beyond a soft cap below Discord's 2000-character limit
- **THEN** the handler SHALL stop emitting further feature detail and SHALL represent the not-yet-emitted features with a single `...and N more` line, so the sent message stays within Discord's limit

#### Scenario: Notifier failure does not poison the bus
- **WHEN** `IDiscordNotifier.SendAsync` throws
- **THEN** the handler SHALL catch the exception, SHALL log it at Warning level (with `MonitorPointId` and target `ChannelId` fields, but NEVER the bot token), and SHALL return `Task.CompletedTask` so Rebus does NOT retry or dead-letter the message

#### Scenario: Coexists with the logging handler
- **WHEN** the bus delivers a `MonitorPointEventDetected`
- **THEN** BOTH the existing logging handler AND the Discord handler SHALL receive the message; failure of one handler SHALL NOT prevent the other from processing it
