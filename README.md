# Silicone Tycoon

A semiconductor industry tycoon, from a small chip company in 1970 to today: design chips at floorplan level, build fabs, win customers and survive the node race. Engineering-only — every system shows its real inputs.

Design doc: <https://claude.ai/artifact/LQmY5vYonfsrpmkjyUby5T> (decisions and slice plan in [docs/DESIGN.md](docs/DESIGN.md)).

## Status: milestone 2 — chip design model

The vertical slice covers 1970–1985 as a newly founded integrated company. Milestones 1 and 2 are the headless simulation:

- **Calendar**: weekly ticks (4 per month, 48 per year), real time with pause comes with the UI.
- **Deterministic RNG** that saves with the game, so a loaded save continues exactly.
- **Ledger and finance**: cash vs profit (capex, depreciation), monthly books, bank loans, overdraft game over.
- **Process nodes** 10 µm → 1.5 µm with density, clock, wafer size, cost and defect data.
- **Node race**: research programmes with a hidden slip roll; more budget cuts the slip risk; slips keep costing money.
- **Fabs** in real-world locations: quote, subsidies, construction, ramp, wafer starts, lots, running costs, depreciation.
- **Learning curve**: defect density falls faster with volume and engineering spend.
- **Yield model**: dies per wafer, clustered-defect (negative binomial) yield, Monte Carlo lot sampling.
- **Saves**: whole game state as versioned JSON.
- **Chip design** (milestone 2): blocks from `data/blocks.json` (cores, RAM with spare rows, ROM, memory interfaces, I/O rings, graphics units) placed on a floorplan in mm. Analysis checks overlaps, I/O on the die edge, pins vs pads vs package, die size and power against the package; works out transistors, area, clock (slowest block plus wire delay from each core to its partners), power (Σ activity × transistors × C × V² × f), compute/graphics/control performance, dies per wafer and cost per good die.
- **Design house**: auto-placement that's valid but loose, with a fee and a 5% clock penalty.
- **Binning**: Monte Carlo defects dropped on the floorplan — empty silicon is harmless, spare rows repair RAM, extra cores and graphics units are harvested, anything else kills the die — plus per-die speed spread and a bin table.
- **Design lifecycle**: design teams, design time from transistor count and verification effort, tape-out and mask sets, first silicon, validation that finds most bugs, steppings, release; fabs make released chips and bin every lot.

## Building and testing

You need the **.NET 8 SDK** (and **Godot 4.7 .NET** for the game itself).

```
dotnet test tests/SiliconeTycoon.Sim.Tests
dotnet run --project tools/SimRunner -- 15 1
```

The runner plays a scripted company for 15 years (seed 1): it designs a chip for each new node, steps it until it can ship, makes and bins it, and prints one line per quarter plus notable news and a table of its chips. There's no market yet, so dies sell at a placeholder price.

To open the game project: Godot project manager → **Import** → `project.godot` → **Build**. If Godot reports an SDK version mismatch, change `Godot.NET.Sdk/4.7.0` in `SiliconeTycoon.csproj` to match your editor.

## Code layout

| Folder | What's there |
| --- | --- |
| `src/Sim/` | Plain C# simulation, no Godot types. `Core/` calendar, RNG, ledger, finance, state, saves · `Fab/` fabs, learning curve, process research · `Design/` yield model, chip designs, analysis, binning, design teams · `Data/` data definitions and loader · `Simulation.cs` the weekly loop |
| `data/` | JSON game data (nodes, locations, blocks, packages) — see `data/README.md` |
| `tests/SiliconeTycoon.Sim.Tests/` | xUnit tests; compiles `src/Sim` directly |
| `tools/SimRunner/` | Headless console run of the sim |

Code copied from Console Tycoon's `Core/` (calendar lookup, money formatting, reputation, loans, JSON saves) is marked in comments.

## Roadmap (vertical slice)

1. ✅ Sim core
2. ✅ Design model: blocks, floorplan, derived stats, binning, design lifecycle
3. Market: segments, customers, bids, contracts, rival AI
4. Godot shell: main screen, time controls, dashboard, charts
5. Floorplan editor and wafer map
6. Fab and market screens, events
7. Balance pass and playtest
