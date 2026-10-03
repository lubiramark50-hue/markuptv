# MarkUpTV Live Pipeline Harness

A console test harness that executes the **real production resolution code**
(`Models/LiveSource.cs`, `Services/LiveCatalogService.cs`,
`Services/LiveCatalogProvider.cs`, `Services/StreamProbeService.cs`,
`Services/LiveResolutionService.cs`, `Models/CommunityModels.cs` are compiled
straight from the app project) against the **real catalogue asset** and **real
live streams**. No backend and no UI are needed, so the playback pipeline can be
verified on any machine or in CI.

## Run it

```bash
cd tools/LivePipelineHarness
dotnet run
```

Exit code `0` means every check passed; `1` means at least one failed.

## What it checks

| # | Test | Evidence produced |
|---|---|---|
| 1 | Catalogue loads through the production service | source / league / browse counts |
| 2 | African league fixture resolves to relevant free sources | the ordered plan and each source's region |
| 3 | Premier League fixture resolves | BBC iPlayer + schedule companion present |
| 4 | Self-test streams probe as live | real HTTP calls, `#EXTM3U` confirmed, latency per stream |
| 5 | Dead source is skipped, working source plays first | probe reason (`unreachable`), ordering |
| 6 | Channel path (World Channels -> player) | verified stream + latency |
| 7 | Plan ordering invariant | verified direct > unverified > embed > browse |
| 8 | Community records round-trip | fields preserved, `[JsonIgnore]` members excluded |

## Bugs this harness has already caught

1. `LiveSource.IsDirectlyPlayable` excluded diagnostic entries, which made the
   in-app **playback self-test show "no source"** (a dead feature) and blocked
   probing of the self-test streams.
2. League matching was too loose: a Kenyan fixture resolved to 14 sources
   including UK, Italian, Spanish and French broadcasters. Matching is now
   bidirectional containment, so the same fixture resolves to 5 relevant
   sources.

## Requirements

- .NET 10 SDK
- Network access for the probe tests (tests 4-6). Without network those checks
  fail while the rest still run.
