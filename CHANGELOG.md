# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-05-13

### Added
- Windows-only process tree investigation with snapshot, tree resolution, evidence collection, JSON output, and remediation planning
- Owner taxonomy and flat tags for process classification, including IDE, terminal, runtime, and Git-related processes
- Interactive commands for process tree inspect, JSON inspect, remediation dry-run, and remediation apply
- Annotated process snapshots and snapshot diff to compare baseline and degraded system states
- Latest-snapshot versus current-live diff flow for interactive and agent-driven usage
- Live health/triage views with CPU, disk, memory, paging, top processes, and bottleneck assessment
- `why-slow` diagnosis with reason codes, evidences, focus filters, and slowdown decision support planning
- Extended snapshots with optional health data and snapshot diffs with tag deltas plus health-pressure transitions

### Improved
- Snapshot diff normalization to group repetitive Git and shell processes more cleanly
