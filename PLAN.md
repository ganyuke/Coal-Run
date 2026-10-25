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
  - [x] Greybox scene (built by a generator at first; the generator was deleted 10/25 and the scene is now edited by hand)
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
  - [ ] Crashes knock wheels off; pick up and refit (E)
  - [x] Right the cart: E or ram it (done early, 10/24)
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
- **The scene is the source of truth.** `Assets/Scenes/Boiler.unity`, edited by hand (or through the CLI). The greybox generator that first built it is gone (10/25).
  Hierarchy: `Systems` (Game, camera, Coal field), `Room` (Art grouped into Walls / Floors / Return grate / Fixtures, plus Colliders), `Stations` (Lift, cranks, Chute lever, Cart, Operator).
  Prefabs in `Assets/Prefabs`: `Cart`, `Operator` (both get respawned in M4), `Lift crank` (all three cranks are instances: both landings and the platform wheel). New stations go in as prefabs too; the Chute lever becomes a `Floor lever` prefab when the burn lever (M3) needs a second one.

### Smaller calls made while porting (change any of them)

- Room geometry is constants in `Room.cs`, which the scene was laid out from. If you move a floor in the scene by hand, update `Room.cs` too.
- Fixed timestep is 1/60, as in the prototype. There's no substepping yet; fast carts use continuous collision instead. Add substeps if the cart tunnels through things.
- Holding the cart is not a joint any more. A joint also held the cart up by its middle, so a one-wheel cart's skid never touched the floor and it hauled as fast as two wheels (4.4 m/s both). Now the grip only pulls and pushes along the floor (the force in `Move`), and `Hold` keeps the operator's hands on the rim (1.26 centre to centre; a grab from a step away closes in a fraction of a second). The keep-upright torque leaves the first 7° of lean alone so the cart can rest on its skid.
- The haul force is `Tuning.haulGain` (110 per m/s short of the haul pace, was a hard-coded 75) capped at `haulForce` (340). Everything the operator does to the cart is a sideways force through its centre of mass: the haul force, and `Hold`, which keeps their hands exactly on the rim and passes their push or pull through like a rigid rod. The operator's body never touches the cart (it used to shove on the bin's top corner, which rocked the cart over its wheel and loaded or unloaded the skid depending on direction).
- A dragging skid is slow work and doesn't coast. Ground friction is not contact friction (it acted at the floor, below the centre of mass, and rocked the cart over its wheel: pulled skid-first a full cart dug in and wouldn't move). Wheels and skid slide frictionless and `Cart.GroundFriction` applies a sideways force through the centre of mass: `Tuning.rollingFriction` (0.13, the JS wheel/floor value) with two wheels, the average of that and `skidFriction` (0.8) with one, `dragFriction` (1.3) with none. The haul pace is also scaled by `oneWheelHaul` (0.5) / `noWheelHaul` (0.3).
- The skid sparks (`Cart.sparks`, a particle system at its bottom) and has a `scrape` audio slot, both scaled by how fast it slides along the floor (`Cart.Scraping`). Dragging through coal doesn't spark.
- Debug keys (`ShiftManager.DebugCommand`, listed in the F3 readout): F2 wheels 1 → 2 → 0, F4 cart load 0 → 6 → 10 → 12, F5 loose coal back to the bunker, F6 the cart upright beside you. Coal moves to and from the bunker, so the total stays intact.
- `Machine.All` is a static registry that machines join in `OnEnable`, so nothing calls `Find`.

### M2 decisions (10/24)

- Coal counts live on their stations (`Chute.Bunker`, `Cart.Coal`, `PlayerController.Hands`, later `Furnace.Hopper`); `CoalField` owns lumps and piles. `ShiftManager.TotalCoal` sums them and F3 shows the drift ("Coal error"), which should stay 0.
- Pile surfaces collide through one `CustomCollider2D` per floor (one quad per column, rebuilt every 0.1 s when changed). Its shapes are placed relative to the attached Rigidbody2D, or the world when there is none, **not** the collider's own transform: see `Surface.shapeOrigin`.
- Lumps are pooled bodies with no script of their own; `CoalField.StepLumps` runs them all, as in the JS.
- Coal draws in front of the cart (piles 16, lumps 17, cart 15) so a buried cart looks buried.

- The floor by the staff lift and control room door (x ≥ `Room.EntranceStart`) is a return grate: lumps land on it, then go back to the bunker. Before, they vanished mid-air on crossing into that area. It's a position check in `CoalField.StepLumps` rather than a trigger collider, like the rest of the lump rules.

### Shovel aim (10/24, your request)

- Releasing F throws toward the cursor (`Controls.aim`, read by `InputReader` from its camera). The launch velocity is solved so the coal passes through the cursor, with a flight time that grows with distance (near = quick toss, far = lob), capped at `Tuning.throwSpeed` (10), past which it falls short. The operator turns to face the throw.
- No more instant fill: coal goes in the cart only if the lumps land in the bin (`CoalField.StepLumps` already caught them). Lumps of one throw scatter at `Tuning.throwSpread` (0.25) of the chute's scatter.
- Lumps lose sideways speed on the ground (`CoalField.groundDrag`, 30), so they thud where they land instead of rolling about 3 units on.
- Every other way coal leaves your hands (grabbing a machine, changing lanes, a full shovel) also throws at the cursor.

### Heave, dash and lurch (10/24, your design)

- **Heave (Shift):** holding a one-wheel cart from its skid end, Shift lifts that end like a wheelbarrow (`Cart.Heave`): an upward force at the operator's hands, capped at `Tuning.heaveForce` (300). It rises at `heaveRate` (70°/s empty, about a third of that full) up to `heaveMax` (30°). Letting go **drops** it (no controlled lowering), and a hard landing jolts coal out: `joltSpill` (2 coal per m/s over `joltSpeed` 1.4, for an overfilled cart, in proportion to the load), split over both rims. Shift and not W because W/S crank the lift.
- Measured in Shift-seconds (pushing from rest for 3 s, coal cleaned up between runs): skid up in 0.1 s, coal lost from 0.5–0.6 s held. 0.25 on / 0.25 off loses 1.2 at 6 coal, 0.35 / 0.25 loses 2–3.5. But fast tapping (0.2 on / 0.05–0.1 off) keeps it barely up and loses nothing: 2.1 m/s full against 0.5 dragged. **Open: still a free speed-up for fast tappers.**
- With the skid off the floor it rolls on its wheel: rolling friction only, and the haul pace is `heaveHaul` (0.85) of two wheels'. A full cart goes 2.2 m/s heaved against 0.5 dragged.
- **Spill:** tilted past the spill angle either way, coal pours out over the low end at `spillRate` (8/s per 10° past it), as lumps. The spill angle depends on the load (`Cart.SpillAngle`), since heaped coal goes first: `spillAngleEmpty` (25°) for an all but empty cart down to `spillAngleFull` (6°) at `overfillCapacity`, so 9° full, 15.5° at 6 coal. Holding Shift for 1.5 s: 12 coal → 8.7, 10 → 7.2, 6 → 4.9. Feathering 0.35 s on/off loses nothing (an overfilled cart only reaches ~4.6°). Spill applies to any tilt, not only heaving, so a crash that tips the cart will spill too (M4). A no-wheel cart rests 23° over on its bin corner, so its spill angle is measured from there.
- **Dash (double-tap A/D, not holding):** a run-up. It starts at `dashStartSpeed` (3, or your walking speed if faster) and speeds up to `dashSpeed` (10) over `dashTime` (0.3 s), about 2 m, then `dashCooldown` 0.6 s. Running into the end of the cart is an inelastic hit through its centre of mass (`PlayerController.Ram`), and the operator bounces back and staggers. The ram stays live while the dash's speed carries past its end. So the further back you start, the harder you hit, up to the dash's range: one wheel empty slides 0.24 m from a 0.1 m gap, 0.5 from 0.5, 0.85 from 1, 1.1 from 1.5, 0.7 from 2, and nothing from 2.4 or more.
- **Lurch (double-tap while holding, pushing or pulling):** a `lurchImpulse` (100 N·s) kick shared by cart and operator, and for 0.3 s neither holds the cart back to hauling pace. It also bounces a one-wheel cart's skid off the floor (`Cart.Hop`, `lurchHop` 190°/s of tilt for an empty cart, scaled down by mass), so it glides on its wheel until the skid lands: empty peaks at 11° (under `spillAngle`). Gain over plain hauling in 1.2 s: empty +1.3 m (2.0 → 3.3), full +0.6 m (0.6 → 1.15). Without the hop the skid's friction ate the kick in 0.3 s: +0.8 and +0.4.
- Double taps are detected from `isPressed` edges in `InputReader.Update` (window 0.25 s), not `wasPressedThisFrame`, so they work from the CLI too. `PlayerController.Dash(dir)` is public for testing.
- **Dash tech:** *short dash*: tapping the other way mid-dash stops it dead (your call, like pressing down to fast-fall), so how soon you tap sets the distance. Letting go of the key does nothing, since a double tap is usually quick. *Dash-grab*: holding Space while dashing into the cart takes hold on contact and passes the hit through `Hold`, so you come out holding a moving cart (1.7 m/s, one wheel empty). That one needed no code: it falls out of the grip rules. (A dash dance, tapping the other way to flip into a reverse dash, was tried and replaced by the short dash.)
- Sound names: `lurch`, `dash`, `ram`, `spill`.
- **Righting the cart** (pulled forward from M4): E next to an overturned cart (more than ~45° over) stands it up where it lies, as the JS `rightCart` does: it must have landed and not be buried past 0.8. Or ram it: a dash into an overturned cart spins it the way it was hit (`Tuning.rightingSpin`, 250°/s for a full-speed hit on an empty cart) with a small hop. From the right side, one ram stands a cart on its side back up; from the wrong side it goes upside down. Upside down takes two or three rams. A cart on its side spills its load anyway (past `spillAngle`).

### Lift jams (10/24, your call)

- The lift no longer refuses to move into the cart or the operator. It only brakes for the ends of the shaft; anything in the way is hit at full speed and the lift jams against it with a bang (`Lift.Jam`). It's kinematic, so it still stops just short of really pushing into them.
- **Up with the cart hanging off the open side:** it rises until the overhang is just under the upper floor's slab (0.4 thick, so the stop is 0.42 under its top) and jams. The cart gets `Cart.Knock(speed × liftKnock)` (`liftKnock` 0.5; the knock uses `joltSpill`): at full crank speed 10 coal → 6.5, and the cart's left tilting on the edge. From the bottom there is no special stop any more: whatever hangs off onto the lower floor tips and slides off as it rises.
- **Down onto the cart:** jams on top of it, 10 → 7 at full crank speed.
- **Onto the operator (down on them, or up with them hanging off the side):** `liftHit` (1 injury per m/s of impact), then `liftCrush` (3 a second) while the crank keeps turning against them. 7 kills, healing 0.8 a second: hit at 3 m/s gives 4.6, dead about 1.5 s later if you keep cranking. The death message says "Crushed by the lift" or "Crushed against the floor" (`PlayerController.Hurt` now takes a cause).
- `CoalField.ReturnAll()` sends every lump and pile back to the bunker, for tests.

### Floor levers (10/24, your request)

- Levers are `FloorLever` machines: a stick pivoting on the floor, dragged left (0) to right (1), ±`Tuning.leverAngle` (35°). The chute *has* a `FloorLever` and reads `lever.Value` instead of being a machine itself, so the M3 burn lever can be another `FloorLever` (with `springReturn` off, plus notches).
- Spring-return levers roll back at a steady `Tuning.leverReturn` (0.5 travel/s, so 2 s from full). The chute keeps pouring while it rolls back, about 5 coal from full open, so letting go early is part of the timing.
- Holding a machine doesn't lock movement any more; you walk at `Tuning.machineWalk` (0.35 of walking speed), within the machine's `WalkRange`. Cranks: 0.6 either way of where you took hold. Floor lever: the mouse moves it freely, and walking pulls it along by however far you move. You can't walk past the knob's furthest left/right position plus `standOff` (0.3) on the side you grabbed from, so at the ends you stand beside it. Only a moving cursor drags the lever, so a still mouse doesn't fight walking.
- Grabbing a lever mid-roll doesn't make it jump to the cursor (grab offset).
- Audio slots on each lever, empty `AudioSource`s on child objects named `Audio: …`: `grab` (one-shot), `drag` (loop while pushed by hand), `rollBack` (loop while rolling back), `stop` (one-shot at either end). Loops get louder with speed. Drop clips on them in the inspector.
- Piles are drawn as one stretched sprite per column until the art pass.
- Death is a placeholder (`ShiftManager.Die` respawns at the staff lift at once). M4 adds the replacement delay.

## Open questions

- Real UI: uGUI (Canvas) or UI Toolkit? uGUI is the safer bet for a jam.
- Day 2 forecast and target are placeholders (Day 1's storm, 6° colder).
- Design doc open items: lift momentum, power loss.
