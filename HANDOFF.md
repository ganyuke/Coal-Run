# Coal Run: handoff (current as of v0.3, 10/28)

For whoever picks the project up: how it fits together, where to change things, and what to watch out for. `PLAN.md` has the full decision history (what was tried, scrapped, superseded); `SESSION-LOG.md` is the story of how it got built. Read this one first.

Unity 6000.5.9f1, URP 2D, new Input System. Scope so far is Day 1 plus Day 2 of the five days in the one-pager.

## Run, test, ship

- **Play** `Assets/Scenes/Boiler.unity`. It starts in the Day 1 training with the cart parked. Controls: A/D or arrows walk, double-tap A/D dash, W/S take stairs at the doors or crank the lift at a crank, Space grips the cart, Shift heaves a one-wheel cart from its skid end, E interacts (hold it to light the fire or bolt on a wheel), F shovels (aim with the mouse, release to throw), mouse drag works levers and the lift's wheel, click throws a carried wheel or body, Enter begins the shift, R restarts or moves on from the report, Esc pauses (and shows these controls; Q then quits a desktop build).
- **Debug keys** (also listed top left when you press F3): F2 knock a wheel off, F4 cart load, F5 loose coal back to the bunker, F6 cart to you, F7 replay the training, F8 skip into the shift, F9 the shift bot plays (press again to stop).
- **The shift bot** (`Dev/ShiftBot.cs`) plays Day 1 using only the player's controls, and prints a report to the console (`Logs/ShiftBot.csv` has samples). Use it after any physics or balance change: it should finish with no deaths and a "Coal error" near 0. It varies from run to run (last runs: 167 and 176 kWh against a target of 175), so judge a change on several runs, not one.
- **Build** with `python3 Tools/release.py 0.3` (optionally `--target WebGL`, `--background`, `--release` to make a GitHub release). It builds in a separate Unity process, so the editor can stay open, and puts zips in `dist/<version>/`. Only saved assets are included.
- **Unity CLI**: `CLAUDE.md` (local only) has the working recipes for driving the editor from a terminal. Not needed for normal work.

## Where things live

```
Assets/
  Scenes/Boiler.unity     The one scene; edited by hand (the generator that built it is gone).
  Scripts/Core/           ShiftManager (the hub), Tuning, DayConfig, Lines, InputReader, Room, GameLayers, CameraRig
  Scripts/Stations/       One MonoBehaviour per thing in the room (below)
  Scripts/UI/             NotebookView, InstrumentPanel, FrostVignette, DebugHud, VersionLabel
  Scripts/Audio/          AudioDirector
  Scripts/Dev/            ShiftBot
  Editor/                 AudioSetup, ArtApplier, GeneratedArtImporter, Builder (see "Editor tools")
  Data/                   Tuning.asset, Day1.asset, Day2.asset, Lines.asset, physics materials
  Prefabs/                Cart, Operator, Chute, Furnace, Floor lever, Lift crank, Tip cradle, Wheel, lamps
  Art/Generated/          The sprites. Made by Tools/ArtGen, not drawn by hand
  Audio/Synth, Kenney/    Synthesised sounds (Tools/AudioGen) and CC0 impacts
Tools/                    ArtGen (sprites), AudioGen (sounds), release.py (builds)
```

**Scene hierarchy.** `Systems` (Game = the `ShiftManager`, camera, Coal field, Notebook and its UI, Lights, Post volume, Audio), `Room` (Art grouped by kind, and Colliders), `Stations` (Lift, the three cranks, Chute and its lever, Cart, Operator, Tip cradle and lever, Furnace and Burn lever, Tutorial, Lift shaft dressing, Axle installer).

**Stations** (`Scripts/Stations/`):

| Area | Classes |
|---|---|
| The operator | `PlayerController` (split into partial files: `.Dash`, `.Carrying`, `.Animation`, `.Shovel`, `.Life`, `.Prompts`) |
| The cart and the lift | `Cart`, `Lift`, `LiftCrank`, `LiftShaft` (dressing only), `StaffLift` |
| Coal | `CoalField` (pooled lumps, piles as heightmaps), `Chute`, `TipCradle`, `HopperFeed` (picture only) |
| Furnace and weather | `Furnace`, `Storm`, `FreezeLamp`, `RedPhone` |
| Levers and mouse | `Machine` (base class), `FloorLever`, `MachineInput`, `NotchDial` |
| People and story | `Tutorial`, `AxleInstaller`, `Speaker` (speech above heads), `Notebook` (the task list), `ShovelPickup`, `Corpse`, `CorpseBody` |
| Light | `LightFlicker` |

## How it fits together

- **`ShiftManager.Instance` is the hub.** It holds the phase (`Tutorial, Briefing, Shift, Leaving, Replacement, Complete, Frozen`), the clock, the temperature, deaths, and references to every station. Stations get their references through serialized fields, never `Find`. Useful properties: `Playing`, `InShift`, `CanAct`, `Ended`, `Freezing`, `Chill`.
- **Events.** `ShiftManager.Sound("name", cooldown, amount)` raises a named sound; `AudioDirector` plays the cue with that name. `ShiftManager.Say(text, seconds, speaker)` makes someone talk. `OnPhase` fires on each phase change.
- **Text.** Every line the player reads is in `Assets/Data/Lines.asset` (grouped: speech, phone, training, tasks, prompts, levers, sheets). Don't hardcode new text in a script: add a field to `Lines.cs`.
- **Tuning.** Numbers that change the feel are in `Assets/Data/Tuning.asset`, grouped under headers (World, Reach, Cart, Crashes and wheels, Heave and ram, Coal, Lift, Levers, Tip cradle, Furnace, Freezing to death, Leaving, Shift). Changes made in Play mode stick.
- **Input.** All keys are in `InputReader.cs`. A press is latched in `Update` and handed out in the next `FixedUpdate`.
- **Physics.** Fixed step 1/60, gravity -18 (set in `ShiftManager.Awake`). Layers Terrain, Player, Cart, Coal, Part, Pile; which pairs collide is set in `GameLayers.ConfigureCollisions`. The control room (lane 1) only collides with Terrain.
- **Coal is conserved.** Every unit is in exactly one place (bunker, cart, hands, loose, hopper, burned). The F3 readout's "Coal error" must stay near 0; it is the quickest sign you broke something.

## PlayerController, file by file

`PlayerController` is the operator, and it is big (1,247 lines across seven files, one class). It is a single `MonoBehaviour` on the `Operator` prefab, split with `partial` purely so it can be read in pieces: **every file shares the same private fields**, so a change in one file can affect another. All the Inspector fields (sprites, anchors, sparks, scoop box, walking numbers) are declared at the top of `PlayerController.cs`, and that is the only file with serialized fields.

**The frame loop** (`PlayerController.cs`):
- `FixedUpdate` is the game logic, in this order: tick timers, bail out if the operator can't act (`CanAct`) or is climbing, handle a lane change, light the fire (E held at the firebox), `Interact` (E pressed), throw a carried wheel, then `Fit` (bolting on a wheel), `Move`, `Effects`, `Shovel`, clamp the control-room lane, `CheckBurial`.
- `Update` is only the look: wheel, body, flint, shovel and arm poses, the walk bob, shiver and crouch (`PlayerController.Animation.cs`).

| File | Lines | What is in it | Start reading at |
|---|---|---|---|
| `PlayerController.cs` | 415 | Inspector fields and state; `FixedUpdate`; `Move` (walking by forces, step-up onto piles, hauling the cart); `Effects` (coal dust, fall damage); cart helpers (`CartNear`); `Tether` (walking while holding a lever); the cart grip (`TakeGrip`, `Hold`, `ReleaseGrip`); lanes and the stairs (`ChangeLane`, `Climb`) | `FixedUpdate`, then `Move` |
| `.Dash.cs` | 108 | Double-tap dash, the short-dash cancel, the ram into the cart, the dust a dash kicks up | `Dash`, `Ram` |
| `.Carrying.cs` | 201 | E: `Interact` picks what E does (right the cart, put down, pick up, hook the cradle, clock out); wheels (pick up, throw, `Fit`); bodies (pick up, throw); `Fly` is the tween that moves a wheel into the hands | `Interact` |
| `.Shovel.cs` | 226 | Digging: `Shovel` (fill from the scoop box, throw), `ScoopTarget`/`ScoopRect` (which column of coal), `Bulldoze` (pushing coal with the cart), `ThrowHands`, `ThrowVelocity`; and the drawing of it: `HeldShovel`, `Arms`, `Reach` (the arms follow the handle) | `Shovel`, `ScoopTarget` |
| `.Animation.cs` | 90 | `Update`, the walk bob and lean, the carried wheel, flint and steel | `Update` |
| `.Life.cs` | 118 | `Hurt` and `CheckBurial` (injury), `Kill` (leaves a body), `Respawn`, `WaitInLift`, `Stand`/`Glide` (the clock-out), `Hide` | `Hurt`, `Kill` |
| `.Prompts.cs` | 89 | `Prompts()`: the key hints for the sticky notes, chosen by what is in reach | `Prompts` |

**State that the files share** (declared in `PlayerController.cs` or next to the code that owns it): `Body` (the Rigidbody), `Lane` (0 work floor, 1 control room), `Facing`, `Holding` and `Machine` (gripping the cart or a lever), `Hands` (coal on the shovel), `CarryingWheel` and the carried body, `Injury`, `Burial`, `Stagger`, `Climbing`. The dash and lurch use `_dashFor`, `_lurchFor`, `_dashCooldown`. The pose uses `_poseLean`, `_poseCrouch`, `_pace`, `_stride`.

**Who calls the operator from outside** (the public surface, so you know what to keep stable): `Hurt` (the lift, the coal field), `Kill`, `Respawn`, `Stand`, `WaitInLift` (the shift manager), `ReleaseGrip` (the shift manager, the cradle, the mouse input), `ThrowHands` (the mouse input), `Prompts` (the notebook), and the read-only state `Position`, `Lane`, `Holding`, `Hands`, `Machine`, `CarryingWheel`, `Facing`, `Scooped`, `Climbing`, `Voice`, `Visible` (the shift manager, the coal field, the furnace, the levers, the tutorial, the debug readout and the bot).

**Where to change a behaviour**
- Walking speed, haul force, the cart's feel: `Move` here plus `Tuning` (World, Cart).
- What E does: `Interact` in `.Carrying.cs`; the hint text is `Lines.prompts`, chosen in `.Prompts.cs`.
- Shovel reach and how it scoops: the `scoopBox` Inspector field and `ScoopTarget`; its speed is `Tuning.shovelRate` and `handCapacity`; the throw is `ThrowVelocity`.
- What kills or hurts: `Hurt`, `CheckBurial`, fall damage in `Effects`; the numbers are in `Tuning` (`safeFall`, `fallHurt`).
- Freezing makes them faster: `Rush` in `PlayerController.cs`.
- Anything visual about the operator: `.Animation.cs` and the arm code in `.Shovel.cs`.

**If you want to split it for real** (it is the obvious next cleanup, and a later pass should not be rushed before the jam deadline): the cleanest first cuts are `Prompts` (it only reads state and returns a list) and the shovel (`.Shovel.cs`), as plain helper classes owned by `PlayerController`, so the prefab and the outside callers listed above stay the same. Run the shift bot (F9) several times after each step.

## "I want to change X": what to touch

| I want to… | Do this |
|---|---|
| Reword anything the player sees or hears | `Assets/Data/Lines.asset`. The report's text is under Sheets (headline, body, "offices froze first"), the phone's under Phone |
| Make the cart, walking, lift, coal, levers feel different | `Assets/Data/Tuning.asset`, under the matching header |
| Change a day's temperature, target, wheels, hopper, memo | `Assets/Data/Day1.asset` or `Day2.asset` (forecast is a list of time/temperature points, linear between them) |
| Add Day 3 | Create a day with Assets > Create > Coal Run > Day, set its forecast and target, add it to the `Days` list on `Systems/Game` (`ShiftManager`). The report button already says "Next day" while there is one. Anything day-specific (like the installer) hangs off a `DayConfig` field, see `AxleInstaller` |
| Change the shift length | `Tuning.shiftLength` and the last forecast point's time (both 600 s) |
| Change how much heat or power a coal makes | `Tuning.kWhPerCoal`; the burn rates and what each notch holds are `Tuning.burnRates` and `Tuning.heldTemperatures`; the lever has 5 notches in the scene, so changing that count means changing the lever too |
| Change when the phone calls | `RedPhone`: `ringBelow` (13) and `desperateBelow` (7); the 0° call is `Tuning.freeze`. Each call is said once a shift |
| Change the body-burning reward | `Tuning.bodyOutput` (kWh) and `Tuning.bodyHeat` (degrees); how long it burns is `CorpseBody.burnTime` |
| Change the freeze alarm | Volume on the `alarm` cue in `AudioSetup.cs`; number of rings is `FreezeLamp.alarmRings`; the sound is `alarm()` in `Tools/AudioGen/make_audio.py` |
| Change the report's layout | `NotebookView.Sheet` and `NotebookView.Fit` (the headline grows to its text, the figures sit at the foot, the body shrinks to fit); the `Stats` text is under `Systems/Notebook UI/Book/Page/Sheet` |
| Change a key binding | `InputReader.cs`, then the prompts text in `Lines.prompts` and the pause card's list in `Lines.pause` |
| Change the pause card | Its words are `Lines.pause`; its look is `Systems/Pause UI` in the scene; the behaviour is `PauseMenu` (Esc stops the game and the sound, Esc again carries on) |
| Add a new lever or crank | Subclass `Machine` (implement `CanOperate`, `Drag`), or reuse `FloorLever` by dropping the `Floor lever` prefab in and having your station read `lever.Value`. Machines register themselves in `OnEnable` |
| Add a new station | A MonoBehaviour in `Scripts/Stations` (namespace `CoalRun.Stations`), a serialized field for it on `ShiftManager`, make it a prefab. Use `_game.Sound` and `_game.Say`, not direct audio calls |
| Move a floor or wall | The scene colliders are `Room/Colliders` (box colliders). **Also change `Room.cs`**: coal logic reads those constants (floors y 2 and 9.5, walls x 0.35 and 31.7) |
| Change how coal behaves | `Tuning` under Coal; `CoalField` for slumping, burial and the return and hopper grates |
| Change a sprite | Edit the python in `Tools/ArtGen/` (e.g. `make_coal.py`), run it from the repo root with `Tools/ArtGen/.venv/bin/python`, let Unity import. `GeneratedArtImporter` sets the import settings |
| Add or change a sound | Edit `Tools/AudioGen/make_audio.py` and run it (it writes into `Assets/Audio/Synth`), then add or change the cue in the table at the top of `Assets/Editor/AudioSetup.cs` and use Coal Run > Rebuild audio table |
| Change the lighting or the look | `Systems/Lights` and `Post volume` in the scene; the storm's strength, light and snow follow the forecast in `Storm` |
| Change the tutorial | `Tutorial` (stages and conditions), `Lines.training` (what the helper says) |
| Change the tasks the notebook shows | `Notebook.Needs`; the wording is in `Lines.tasks` |

## Things that will bite you

- **Rebuilding the audio table replaces `Systems/Audio`.** Levels set by hand in the Inspector are lost, and master volume and voice count go back to defaults. The table in `AudioSetup.cs` now matches the scene (it was missing `relay` and `alarm` before 0.3), so change the table first, then rebuild. The menu item asks before it does it.
- **`ArtApplier` is a leftover tool** for swapping sprites onto scene objects from the editor CLI, one group at a time. It isn't a menu item and a human doesn't need it; edit sprites on the scene objects directly.
- **Walls and floors are box colliders on purpose.** They used to be zero-thickness lines, and a shifting pile could push the operator through. Keep them boxes.
- **Statics across Play sessions.** The training plays once per session and the day number is static (`ShiftManager._trained`, `_dayIndex`); they are reset on entering Play mode, but only because of the `RuntimeInitializeOnLoadMethod` there. Keep that if domain reload is switched off.
- **Don't recompile while playing.** It leaves Play mode stale; stop first.
- **Fonts.** The two TextMeshPro SDF font assets (`Assets/Fonts`) grow when new characters appear (dynamic atlas) and show up as big modified files. They are safe to commit or to clear; they rebuild from the fonts.
- **Importing the generated art** adds a WebGL block to every `.png.meta` the first time a newer Unity opens them. That's why 86 metas changed in one commit.
- **Heavy files go through Git LFS** (art, audio, fonts). Check `git lfs version` works before you commit, or binaries land in plain git.
- **The bot is the only balance test.** There has been no human playtest of a full run on 0.3.

## What was cleaned up for 0.3

- `PlayerController` was 1,188 lines; it is now a main file plus six partial-class files by concern. No behaviour changed (every original line is still there).
- The notebook's sticky notes used to look up their text component and build a string every frame; they now cache it and rewrite only when the prompt changes. The prompt list and the speech layout list are reused instead of allocated each frame.
- `ShiftManager.Playing` and `InShift` replace six copies of the same phase test. `Tuning.kWhPerCoal` replaces a hardcoded 1.5. A dead field was removed. The `Swinging Lamp` prefab and objects are `Swinging lamp` like the others. `__pycache__` is git-ignored everywhere, and a Unity recovery scene and a crash dump were deleted.
- `AudioSetup` has a menu item and now lists every cue in the scene.

## Still open

- **From the one-pager:** days 3 to 5 and their upgrades (the weigh dispenser, crank gearing or hoist, brake options; see `furnace-game-design.md` in the JS prototype's folder), a storm curve for those days, and the two controls that differ from the text (the cart grip is Space, not a click; the shovel is F with a mouse-aimed throw, not a mouse shove). Details in `SESSION-LOG.md`.
- **Unreferenced assets** (nothing in the scene or any prefab uses them; safe to delete if you want a smaller repo): `Art/Generated/dash.png` and `pip.png`, `Audio/Synth/lift_hum_loop.wav` and `roll_loop.wav`, `Data/Physics/Wall.physicsMaterial2D`, and 16 spare Kenney impact clips in `Audio/Kenney` (mining, soft, tin, wood and a plate set; keep its `LICENSE.txt`).
- **Empty audio slots.** The levers and the cart carry child objects named `Audio: …` with empty AudioSources from an earlier design. The `AudioDirector` plays everything now; merging or removing them was left as post-jam cleanup (see PLAN.md).
- **Tuning left as it was.** The 175 and 190 kWh targets and the 13° and 7° phone thresholds are bot- and guess-based; adjust them after a real playtest.
