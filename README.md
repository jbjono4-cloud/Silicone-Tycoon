# Silicone Tycoon

A semiconductor industry tycoon, from a small chip company in 1970 to today: design chips at floorplan level, build fabs, win customers and survive the node race. Engineering-only — every system shows its real inputs.

Design doc: <https://claude.ai/artifact/LQmY5vYonfsrpmkjyUby5T> (decisions and slice plan in [docs/DESIGN.md](docs/DESIGN.md)).

## Status: milestone 3 — market

The vertical slice covers 1970–1985 as a newly founded integrated company. Milestones 1–3 are the headless simulation:

- **Calendar**: weekly ticks (4 per month, 48 per year), real time with pause comes with the UI.
- **Deterministic RNG** that saves with the game, so a loaded save continues exactly.
- **Ledger and finance**: cash vs profit (capex, depreciation), monthly books, bank loans, overdraft game over.
- **Process nodes** 10 µm → 1.5 µm with density, clock, wafer size, cost and defect data.
- **Node race**: research programmes with a hidden slip roll; more budget cuts the slip risk; slips keep costing money.
- **Fabs** in real-world locations: quote, subsidies, construction, ramp, wafer starts, lots, running costs, depreciation.
- **Fab retooling and closure**: re-equip a fab for a newer node at about 55% of a new fab's cost (70% if the wafer size changes) and 60% of the build time, with learning starting again and most old equipment written off; or close a fab, salvaging 15% of its book value.
- **Learning curve**: defect density falls faster with volume and engineering spend.
- **Yield model**: dies per wafer, clustered-defect (negative binomial) yield, Monte Carlo lot sampling.
- **Saves**: whole game state as versioned JSON.
- **Chip design** (milestone 2): blocks from `data/blocks.json` (cores, RAM with spare rows, ROM, memory interfaces, I/O rings, graphics units) placed on a floorplan in mm. Analysis checks overlaps, I/O on the die edge, pins vs pads vs package, die size and power against the package; works out transistors, area, clock (slowest block plus wire delay from each core to its partners), power (Σ activity × transistors × C × V² × f), compute/graphics/control performance, dies per wafer and cost per good die.
- **Design house**: auto-placement that's valid but loose, with a fee and a 5% clock penalty.
- **Binning**: Monte Carlo defects dropped on the floorplan — empty silicon is harmless, spare rows repair RAM, extra cores and graphics units are harvested, anything else kills the die — plus per-die speed spread and a bin table.
- **Market** (milestone 3): three segments (calculators and controllers, home computers, games consoles) with demand, price and performance curves, including the 1983 console crash. Each month's demand splits between design-win contracts, an open distribution market and the rest of the industry.
- **Customers** (11, fictional, in real countries) run requests for quotes each product cycle; bids are scored on performance against need, price against the going rate, reputation, loyalty and delivery risk. Winners get contracts for the customer's share of the segment, with yearly price-downs, penalties for short deliveries and cancellation after three bad months. Short orders are topped up from better bins (down-binning).
- **Open market**: list a chip bin at a price; distributors split demand by performance, price and reputation.
- **Rivals**: Meridian Integrated and Kestrel Devices reach nodes on history's schedule plus their lag and slips, design real chips with the same maths as you, bid on requests, grow capacity and start price wars when their fabs sit idle.
- **Design lifecycle**: design teams, design time from transistor count and verification effort, tape-out and mask sets, first silicon, validation that finds most bugs, steppings, release; fabs make released chips and bin every lot.

## Building and testing

You need the **.NET 8 SDK** (and **Godot 4.7 .NET** for the game itself).

```
dotnet test tests/SiliconeTycoon.Sim.Tests
dotnet run --project tools/SimRunner -- 15 1
```

The runner plays a scripted company for 15 years (seed 1) against the real market: a chip per segment per node, listings at cost plus a margin, bids on every request it can meet. It prints one line per quarter (cash, revenue, reputation, contracts, market share by segment, rivals' revenue), notable news, and tables of its chips and the rivals'.

To open the game project: Godot project manager → **Import** → `project.godot` → **Build**. If Godot reports an SDK version mismatch, change `Godot.NET.Sdk/4.7.0` in `SiliconeTycoon.csproj` to match your editor.

## Code layout

| Folder | What's there |
| --- | --- |
| `src/Sim/` | Plain C# simulation, no Godot types. `Core/` calendar, RNG, ledger, finance, state, saves · `Fab/` fabs, learning curve, process research · `Design/` yield model, chip designs, analysis, binning, design teams · `Data/` data definitions and loader · `Market/` inventory, open market, RFQs, contracts, rival AI · `Simulation.cs` the weekly loop |
| `data/` | JSON game data (nodes, locations, blocks, packages, segments, customers, rivals) — see `data/README.md` |
| `tests/SiliconeTycoon.Sim.Tests/` | xUnit tests; compiles `src/Sim` directly |
| `tools/SimRunner/` | Headless console run of the sim |

Code copied from Console Tycoon's `Core/` (calendar lookup, money formatting, reputation, loans, JSON saves) is marked in comments.

## Roadmap (vertical slice)

1. ✅ Sim core
2. ✅ Design model: blocks, floorplan, derived stats, binning, design lifecycle
3. ✅ Market: segments, customers, bids, contracts, rival AI
4. Godot shell: main screen, time controls, dashboard, charts
5. Floorplan editor and wafer map
6. Fab and market screens, events
7. Balance pass and playtest
