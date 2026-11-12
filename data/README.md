# Game data

Plain JSON, loaded by `GameData.FromJson` / `GameData.FromFolder`. Keep these files comment-free: Godot's JSON importer rejects comments.

All values are **game-tuning approximations**, not sourced historical figures. Money is in **1970 dollars**; the sim multiplies by `Calendar.Inflation(year)` when it charges anything.

## nodes.json

| Field | Meaning |
| --- | --- |
| `FeatureNm` | Feature size in nanometres |
| `Year` | Year the node reaches production historically; research can start up to about two years earlier |
| `Requires` | Previous node that must be ready first |
| `LogicDensity`, `SramDensity` | Transistors per mm² |
| `MaxClockMHz`, `Voltage` | Speed ceiling and supply voltage |
| `WaferMm` | Wafer diameter |
| `WaferCost` | Materials per wafer start |
| `MaskSetCost` | One full mask set (used from milestone 2) |
| `CycleWeeks` | Wafer start to finished wafer |
| `D0Launch`, `D0Mature` | Defects per cm² when new and when mature |
| `LearningMonths` | Time constant of the learning curve at normal volume |
| `FabCostPer1000` | Capex for 1,000 wafer starts a month (scales with capacity^0.85) |
| `BuildMonths`, `RampMonths` | Construction and ramp times |
| `ResearchCost`, `ResearchMonths`, `SlipRisk` | Process research budget, planned length and chance of slipping at 1× budget |

## locations.json

Indexes are relative to the United States = 1.0. `Subsidy` is the share of fab capex paid by grants. `QuakeRisk` and `PoliticalRisk` are annual disruption chances (used once events arrive in milestone 6). `From`/`To` limit the years you can build there.

## blocks.json

| Field | Meaning |
| --- | --- |
| `Kind` | `Core`, `Ram`, `Rom`, `MemoryInterface`, `Io` or `Graphics` |
| `LogicTransistors`, `SramTransistors` | Area comes from these over the node's logic and SRAM densities |
| `FixedAreaMm2` | Pads and analog at 10 µm; shrinks only with feature size^0.35 |
| `Activity` | Share of transistors switching each clock (power) |
| `LogicDepth` | Critical path relative to the node; top clock = node MaxClockMHz / LogicDepth |
| `Pins` | I/O rings: pads supplied. Other blocks: pins needed |
| `Ipc`, `Graphics`, `BytesPerClock`, `RamBytes`, `RomBytes` | Performance inputs |
| `RepairCapacity` | Defects spare rows can repair |
| `Disableable` | A defect switches the block off instead of killing the die (harvesting) |

## packages.json

`Pins`, `MaxPowerW` and `MaxDieMm2` limit what fits; `Cost` (1970 dollars) is added per chip; `Yield` is the share surviving assembly.

## segments.json

`Demand` (chips a month), `Price` (1970 dollars per chip) and `Need` (performance a competitive chip has, in the segment's `Metric`: `Control`, `Compute` or `Graphics`) are `[[year, value], ...]` tables. `OpenShare` is the share bought through distributors; `PriceSensitivity` and `PerfWeight` shape how buyers score chips; `MaxPowerW` is what customers' products can cool.

## customers.json

`Share` of the segment each buys, `CycleMonths` (product generation and contract length), `LeadMonths` from award to first delivery, `PriceSensitivity` (multiplies the segment's), `Loyalty` (score bonus for the current supplier). Shares plus the segment's `OpenShare` must not exceed 100%.

## rivals.json

`NodeLagYears` behind history's node dates, `PriceFactor` (normal price as a share of the going price; never below cost × 1.1, and down to half in a price war), starting `Rep`, `StartShare` (starting capacity as a share of demand in its segments), `MaxShare` (the most of that demand it will build capacity for), design `Engineers`, and the `Segments` they compete in.
