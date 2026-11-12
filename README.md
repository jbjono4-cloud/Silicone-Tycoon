# Silicone Tycoon

A semiconductor industry tycoon, from a small chip company in 1970 to today: design chips at floorplan level, build fabs, win customers and survive the node race. Engineering-only — every system shows its real inputs.

Design doc: <https://claude.ai/artifact/LQmY5vYonfsrpmkjyUby5T> (decisions and slice plan in [docs/DESIGN.md](docs/DESIGN.md)).

## Status: milestone 1 — sim core

The vertical slice covers 1970–1985 as a newly founded integrated company. Milestone 1 is the headless simulation core:

- **Calendar**: weekly ticks (4 per month, 48 per year), real time with pause comes with the UI.
- **Deterministic RNG** that saves with the game, so a loaded save continues exactly.
- **Ledger and finance**: cash vs profit (capex, depreciation), monthly books, bank loans, overdraft game over.
- **Process nodes** 10 µm → 1.5 µm with density, clock, wafer size, cost and defect data.
- **Node race**: research programmes with a hidden slip roll; more budget cuts the slip risk; slips keep costing money.
- **Fabs** in real-world locations: quote, subsidies, construction, ramp, wafer starts, lots, running costs, depreciation.
- **Learning curve**: defect density falls faster with volume and engineering spend.
- **Yield model**: dies per wafer, clustered-defect (negative binomial) yield, Monte Carlo lot sampling.
- **Saves**: whole game state as versioned JSON.

## Building and testing

You need the **.NET 8 SDK** (and **Godot 4.7 .NET** for the game itself).

```
dotnet test tests/SiliconeTycoon.Sim.Tests
dotnet run --project tools/SimRunner -- 15 1
```

The runner plays a scripted company for 15 years (seed 1) and prints one line per quarter, plus notable news. There's no market yet, so dies sell at a flat placeholder price.

To open the game project: Godot project manager → **Import** → `project.godot` → **Build**. If Godot reports an SDK version mismatch, change `Godot.NET.Sdk/4.7.0` in `SiliconeTycoon.csproj` to match your editor.

## Code layout

| Folder | What's there |
| --- | --- |
| `src/Sim/` | Plain C# simulation, no Godot types. `Core/` calendar, RNG, ledger, finance, state, saves · `Fab/` fabs, learning curve, process research · `Design/` yield model · `Data/` data definitions and loader · `Simulation.cs` the weekly loop |
| `data/` | JSON game data (nodes, locations) — see `data/README.md` |
| `tests/SiliconeTycoon.Sim.Tests/` | xUnit tests; compiles `src/Sim` directly |
| `tools/SimRunner/` | Headless console run of the sim |

Code copied from Console Tycoon's `Core/` (calendar lookup, money formatting, reputation, loans, JSON saves) is marked in comments.

## Roadmap (vertical slice)

1. ✅ Sim core
2. Design model: blocks, floorplan, derived stats, binning
3. Market: segments, customers, bids, contracts, rival AI
4. Godot shell: main screen, time controls, dashboard, charts
5. Floorplan editor and wafer map
6. Fab and market screens, events
7. Balance pass and playtest
