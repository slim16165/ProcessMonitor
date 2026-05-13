# Changelog

## Unreleased

- Added Windows-only process tree investigation with snapshot, tree resolution, evidence collection, JSON output, and remediation planning.
- Added owner taxonomy and flat tags for process classification, including IDE, terminal, runtime, and Git-related processes.
- Added interactive commands for process tree inspect, JSON inspect, remediation dry-run, and remediation apply.
- Added annotated process snapshots and snapshot diff to compare baseline and degraded system states.
- Improved snapshot diff normalization to group repetitive Git and shell processes more cleanly.
- Added latest-snapshot versus current-live diff flow for interactive and agent-driven usage.
- Added live health/triage views with CPU, disk, memory, paging, top processes, and bottleneck assessment.
- Added `why-slow` diagnosis with reason codes, evidences, focus filters, and slowdown decision support planning.
- Extended snapshots with optional health data and snapshot diffs with tag deltas plus health-pressure transitions.
