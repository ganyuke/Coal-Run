# Coal Run: jam plan

Goal: Day 1 + Day 2, playable start to finish, in Unity. The design is in `Coal Run JS/furnace-game-design.md`.
The JS prototype in `Coal Run JS/src/simulation.ts` is the reference: when a feature's behaviour is unclear, check what it does there.

Dates assume the jam ends Friday 10/30. Move them if it doesn't.

## Milestones

Each milestone ends with something you can play. Don't start the next one until the current one plays.

- [x] **M1 · Walk, cart, lift** (Sat 10/24)
  - [x] Core: ShiftManager hub, Tuning + DayConfig assets, InputReader, layers
  - [x] Operator walk, cart grip/push/pull, one or two wheels (F2)
  - [x] Freight lift: mouse spinner on the wheel, W/S at a crank, landing cranks, safety stops
  - [x] Greybox scene generator
  - [x] **Playtest:** is pushing the cart onto the lift and cranking up fun on its own? Tune `Tuning.asset`.
  - [x] Tighter interaction reach, tunable under Tuning > Reach
- [x] **M2 · Coal** (Sat 10/24, a day early)
  - [x] CoalField: lump pool (Rigidbody2D), pile heightmaps per surface, pile colliders, slumping
  - [x] Cart absorbs lumps; burial slows the cart; bulldozing
  - [x] Shovel (F), throwing into the cart, step-up onto piles, operator burial
  - [x] Chute lever in the control room (lane 1), analog pull
  - [ ] **Playtest:** fill the cart from the lever without burying it; dig a buried cart out; haul through a spill
- [ ] **M3 · Furnace and the shift** (Mon 10/26)
  - [ ] Tip cradle + tip handle, pour into the hopper, spill the wrong way
  - [ ] Furnace: burn lever notches, heat model, output meter
  - [ ] Shift clock, forecast, complete / frozen
- [ ] **M4 · Things going wrong** (Tue 10/27)
  - [ ] Crashes knock wheels off; pick up and refit (E); right the cart
  - [ ] Death and the replacement worker; red phone
  - [ ] Tutorial: helper, stuck lever, avalanche, dig out
- [ ] **M5 · Day 2 and the real UI** (Wed 10/28)
  - [ ] Day flow: Day 1 → memo → Day 2 (DayConfig per day)
  - [ ] Axle installer beat
  - [ ] Notepad with the forecast graph, objective line, menus (replace DebugHud)
- [ ] **M6 · Art and sound** (Thu 10/29)
- [ ] **M7 · Build, playtest, submit** (Fri 10/30)

## Architecture (decided 10/24)

- **Stations + one hub.** One MonoBehaviour per thing: `PlayerController`, `Cart`, `Lift`, `LiftCrank`, and later `Chute`, `TipCradle`, `Furnace`, `CoalField`.
  `ShiftManager.Instance` holds the shared state (phase, clock, day, references between stations).
  Stations get their references through serialized fields. UI and audio subscribe to `ShiftManager` events (`OnSound`, …).
- **Tuning in ScriptableObjects.** `Assets/Data/Tuning.asset` (physics, rates) and `Day1.asset` / `Day2.asset` (forecast, wheels, target).
  Edit them in the Inspector. Changes made during Play mode stick.
- **One InputReader.** All key bindings live in `InputReader.cs`. Mouse machinery goes through `MachineInput` → `Machine` subclasses.
- **Generated greybox, then edited by hand.** `Coal Run > Build Greybox Scene` builds `Assets/Scenes/Boiler.unity` once.
  Running it again replaces the scene (it asks first). It never overwrites the Data assets.

### Smaller calls made while porting (change any of them)

- Room geometry is constants in `Room.cs`, shared by the generator and the code. If you move a floor in the scene by hand, update `Room.cs` too.
- Fixed timestep is 1/60, as in the prototype. There's no substepping yet; fast carts use continuous collision instead. Add substeps if the cart tunnels through things.
- The cart grip is a `SpringJoint2D` (frequency 5, damping 0.95), the Unity equivalent of planck's soft distance joint.
- The operator only collides with the cart while holding it, done by toggling `Collider2D.excludeLayers`.
- `Machine.All` is a static registry that machines join in `OnEnable`, so nothing calls `Find`.

### M2 decisions (10/24)

- Coal counts live on their stations (`Chute.Bunker`, `Cart.Coal`, `PlayerController.Hands`, later `Furnace.Hopper`); `CoalField` owns lumps and piles. `ShiftManager.TotalCoal` sums them and F3 shows the drift ("Coal error"), which should stay 0.
- Pile surfaces collide through one `CustomCollider2D` per floor (one quad per column, rebuilt every 0.1 s when changed). Its shapes are placed relative to the attached Rigidbody2D, or the world when there is none, **not** the collider's own transform: see `Surface.shapeOrigin`.
- Lumps are pooled bodies with no script of their own; `CoalField.StepLumps` runs them all, as in the JS.
- Piles are drawn as one stretched sprite per column until the art pass.
- Death is a placeholder (`ShiftManager.Die` respawns at the staff lift at once). M4 adds the replacement delay.

## Open questions

- Real UI: uGUI (Canvas) or UI Toolkit? uGUI is the safer bet for a jam.
- Day 2 forecast and target are placeholders (Day 1's storm, 6° colder).
- Design doc open items: lift momentum, power loss.
