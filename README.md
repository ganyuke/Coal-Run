# Coal Run

Coal Run is a Unity 6 game that puts you in the boots of a boilerworker in the dead of winter. You've been condemed to the basement of your office building to man the furnace as a blizzard sets in. You must keep the furnace fed for as long as possible, lest you freeze to death.

## Original one pager document

### Overview

You are in the boiler room of a bunker. It's getting really cold outside. You have plenty of coal, but that coal needs to be in the furnace to keep the place warm. Get the coal into the furnace before the blizzard arrives in full force, or freeze.

Fixed 2D side-view game focused on the repetitive task of hauling coal from the coal chute to the furnace's funnel in a single room, a bit like Overcooked, with lots of player agency to screw it up. Starting with a crappy cart and a broken wheel, each day (a total of five days), the player gets a new upgrade to make their lives easier: a new wheel, sensors to dispense coal, increased crank speed, among others. At the same time, the storm worsens. Each day, there is a forecast, helping the player determine when they can relax or use the time to clean up the map (coal can spill and obstruct cart movement!), or need to pick up the pace, or panic when the temperature starts to drop. The player controls the burn rate of coal. The burn rate needs to be increased to increase the temperature of the room, counteracting the blizzard's freeze. But at the same time, the furnace can burn out if they run out of coal so the player must adjust their pace if they want to survive.

### Controls

Mouse and keyboard.

- **Dragging/Pushing the cart:** the player must click on the cart to interact with it. They must be standing at its sides. The cart can be moved left or right with A or D, respectively.
- **Moving coal:** the player must click to interact with a shovel on the wall and use mouse movement to physically shove and throw coal out of the way.
- **Cranking the elevator wheel:** The elevator isn't powered, so you need to crank it yourself. The crank operates like spinners in osu!, giving advantages to those with good wrist control.
- **Coal dispenser lever:** the lever is fully analog. with a left-to-right pull. The amount of coal dispensed depends on how far the lever is pulled, done by checking how far the player moves their mouse right while holding mouse 1 down, so players need to be careful about pulling it all the way lest they bury their cart.
- **Tilting the cart into the fuel funnel:** tilting the cart is also fully analog, done in the same manner as the coal dispenser lever. The tilt can also be moved in the direction opposite of the funnel, letting players screw themselves over if they so choose.
- **Entering control rooms:** the coal dispenser lever is behind a glass control room. Entering the room requires standing at a doorway and using W to enter and S to leave.
- **Furnace burn rate lever:** the burn rate is a vertical lever that reads mouse 1 control up and down, snapping to different levels of burn.

### Art assets

- **Bunker background:** blizzard background in window to indicate weather, industrial, dark concrete aesthetic for the entire facility. rusted, metallic freight elevator.
- **HUD:** immersive-ish elements, notebook to show forecast, temperature targets.
- **Coal:** visuals for levels of coal in the cart (empty, almost full, full), coal piles on the ground, individual coal pieces, coal level in the furnace, coal in the funnel.
- **Furnace:** furnace, furnace fire, visual temperature indicator

### Audio assets

- **Blizzard:** generic wind sounds that increase in intensity as the storm worsens and eases
- **Furnace:** generic fire crackling that also increase and decrease in intensity
- **Cart:** screeching and thumping as the cart rolls or drags across the ground. Metal pipe falling noise for when it hits the ground hard and breaks apart. Sounds for repairing the cart.
- **Coal:** sounds for stepping on coal, coal settling, coal being shoveled, coal being poured/dispensed
- **Elevator:** clicks for cranking the elevator, the elevator moving
- **Levers:** creaks and screeches for levers being pulled

### Concerns

- **Coal behavior:** I would love if coal had a bit of physics so that they could impede the cart if not cleaned up, crush the player, get everywhere, but physics can be expensive and too many physics objects can cause physics to explode things everywhere. Maybe I need a mechanic to collect coal into piles.
- **Elevator crank speed:** need to set some kind of limit and handle the player standing on the elevator as they crank it. somehow need to counteract physics launching the player if they crank it up or down too fast, which is a problem in game engines like Roblox's, at least.
- **Keeping the in-between interesting:** the endpoints (coal dispenser lever and furnace fuel funnel) require thinking, but somehow need to make the dragging interesting if this action will be repeated over and over. I want the player to keep thinking while they are dragging the cart.
- **Balancing the weather:** must determine how to make the weather difficult without making it impossible to fill the chute in time or too easy that it's boring, or too unpredictable that it is unfun. The latter is mitigated by the forecast, but I'm unsure if weather alone is enough to punish the player for taking their time or cutting corners or making mistakes.

## Deviations from one pager

### Changes from existing content

MVP project (v0.6) was cut down to only two days, with the second wheel concept added to the second day (perhaps the days should be flipped considering how intense day 1 is 🤔).

Several controls were changed, including the burn rate leveer which was changed to use the same floor lever as the rest of the levers in the game (tip lever and chute lever). Clicking the cart to drag was changed to holding space and using movement controls.

HUD is still relatively immersive with sticky notes for control hints and a notebook design for the current task. The rest of the HUD was moved to the environment itself, mainly the television on ground floor.

Blizzard, furnace, cart, coal, and elevator noises were implemented. The lever sound was scrapped.

Coal is partially physics when in motion. THey are converted into discrete piles when they land.

The elevator crank is unconstrained when cranked using the mouse like in osu!. Controlling the crank with W/S is eased. If you want to launch your character, you can still do it if you crank with the mouse like crazy, which is a nice tradeoff I think.

The in-between is interesting thanks to the introduction of the "heave" mechanic where you can lift the side of the cart with a missing wheel. The player must tap shift in such a rhythm where the cart's skid lifts off the ground but not hold it long enough that coal starts to pour out.

Shovel mouse movement was changed from completely mouse controlled to holding the F key. Players can still aim the direction that the shoveled coal is thrown.

### Added content

Special effects and post-processing, darkening the scene as the blizzard progresses (and the intensity of the storm is reflected in the window), point lights, and bloom (the latter of which makes my eyes bleed). Lots and lots of particle effects.

Red phone at the start warns the player of dipping temperatures. Television shows the forecast, thermometer, and furnace target. It also announces player respawns and interesting events (see below).

Getting killed (falling, crushed, etc.) produces a corpse that you can toss into the furnace for fuel.

Cart tech: you can heave, shove, ram, and yank the cart around. The effectiveness of these moves depend on how much coal is inside the cart.

## AI disclosure

The project as a whole was built heavily by a mix of Claude Opus 5.5, Claude Sonnet 5.5, and GPT 6.1 Sol. The original game design was devised through a back-and-forth of concepts with GPT 5.6 Thinking and later Claude Opus 5.5. All C# modules were initally writen by Opus, Sonnet, and Sol. The arrangement and production of bespoke art assets (including textures and audio) were handled by Opus and Sonnet.

The original map design was devised by a fleshbag in Excalidraw then rendered into SVG by GPT 6 Astra. A mix of GPT 6 Astra and later Claude Opus 5.5 turned the skeleton map design and gameplay into a flawed JavaScript prototype. Claude Opus 5.5 was used to port the JavaScript prototype (retaining mostly the map design and object coordinates) into Unity through Unity CLI.

Implementation was done almost entirely by Claude under the supervision of a fleshbag. See [SESSION-LOG.md](/SESSION-LOG.md) for a Claude-santized overview of the Claude conversations involving implementation.See [PLAN.md](/PLAN.md) for indirect directives given to Claude by the fleshbag.

