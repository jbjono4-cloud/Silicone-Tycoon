# Design summary

The full, living design doc is here: <https://claude.ai/artifact/LQmY5vYonfsrpmkjyUby5T>. This file keeps the decisions and slice plan next to the code.

## Decisions

| Topic | Decision |
| --- | --- |
| Name | Silicone Tycoon |
| Time | Real time with pause; weekly sim ticks |
| Locations | Real countries and regions; fictional companies |
| Shared code | Copy what can be reused from Console Tycoon's `Core/`; no shared submodule |
| DRAM | Full product line; can be spun off as a subsidiary or kept in house |
| Product depth | DRAM and graphics cards get the same design depth as CPUs/SoCs |
| Starts | Found a new company or take over a pre-existing one; 1970 campaign plus era scenarios |
| Packaging | Full system, chiplets and stacking included |
| Stock market and takeovers | After core gameplay is done |
| Modes | Engineering only; no simplified mode |

## Vertical slice (1970–1985, integrated company)

1. Sim core: calendar, ledger, process nodes, fab with learning curve, yield model, tests, headless runner
2. Design model: blocks, floorplan, derived stats, binning
3. Market: segments, customers, bids, contracts, rival AI
4. Godot shell: main screen, time controls, dashboard, charts
5. Floorplan editor and wafer map
6. Fab and market screens, events
7. Balance pass and playtest

Done when a new player can found a company in 1970, ship at least three chips, survive one bust and finish 1985 with a score.
