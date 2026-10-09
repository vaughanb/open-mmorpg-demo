# Open MMORPG Demo Builder

The editor tools that generate the Open MMORPG demo island: `Assets/OpenMMORPG/Demo`
in the [OpenMMORPG](https://github.com/open-mmorpg/OpenMMORPG) kit repository.

The demo is generated, not hand-placed - apart from one root that is yours, below.
These menu items under **Open MMORPG > Demo**
build it from the source art libraries in `Assets/Plugins` (the Quaternius Universal
Base Characters, Modular Character Outfits, Fantasy Props, Nature and Village packs,
all CC0), so they need those libraries in the project and are of no use to a kit user
who only has the finished demo. That is why they live here, outside the kit folder,
the same way addons install to `Assets/OpenMMORPG_addons`.

Clone this repository to `Assets/OpenMMORPG_DemoBuilder` in the Unity project that
holds the kit:

```
git clone https://github.com/vaughanb/open-mmorpg-demo.git Assets/OpenMMORPG_DemoBuilder
```

Any folder under `Assets` will do - what matters is that the scripts stay inside a
subfolder called `Editor`, which is what makes Unity compile them as editor code. The
paths they write to are fixed (`Assets/OpenMMORPG/Demo/...`), not relative to this.

## Hand edits and the `Authored` root

A generator owns the scene it writes. Three of the menu items below say so in their name -
**Regenerate Island Scene**, **Regenerate Dungeon Scene** and **Regenerate Animation Editing
Scene** each open their scene, destroy every root in it and build it again. Anything moved,
tuned or added by hand in those scenes is gone when they run. The rest of the items are
narrower: they write one root, one prefab or one asset, and leave the scene around it alone.

Two kinds of root survive a regenerate:

- **`Authored`** - yours. No generator ever creates, moves or destroys anything under a root
  with this name, in any of the three scenes. Put hand-placed work here and a regenerate
  leaves it exactly where you put it. It stays switched on for the navmesh bake, so a rock
  dropped in blocks a path and a plank laid down carries one.
- **`Npcs`, `Mounts`, `Wildlife`** - placed by their builders and then nudged by hand, which
  is the point of having them in the scene rather than in a spawner. Kept for the same
  reason, but switched **off** around the navmesh bake: each one stands on the ground with a
  capsule that would otherwise carve a hole where it stands. Hand-placed characters belong
  here rather than under `Authored`.
- **`Shrines`** - the resurrection shrines. Kept like the entity roots, and left **on** for
  the bake like `Authored`, because a shrine is masonry and does not move: its piers are
  supposed to carve. Surviving the wipe and surviving the bake are two separate questions,
  and this root answers them differently from the three above it.

If a generator has eaten hand work that was not under one of those, that is a fault in the
generator: widen it, or move the work to `Authored`.

### Generated art gives way to real art

`Build Skills` draws a stand-in icon per skill — a coloured plate with a white mark — because a
hotbar of blank squares was what the demo looked like with the skills in and no art for them.
**It only draws into a gap.** An icon already sitting at
`Demo/Textures/Icons/Skills/<SkillName>.png` is taken as it is, its pixels untouched, and the
build says how many it kept. The demo's fifteen were replaced with hand-drawn ones on
2026-09-17.

`Open MMORPG > Demo > Regenerate Skill Icons (overwrites hand-drawn icons)` is the way back to
the drawn set. It writes each icon over the path it already had, so the file keeps its GUID and
every skill still points at its own.

One import detail the adopt path handles for you: a PNG dropped into the project imports as a
plain texture, and `LoadAssetAtPath<Sprite>` on one of those returns **null** rather than
failing — which surfaces much later as a blank hotbar slot with nothing in the console to
explain it. The builder corrects the import settings (and only those) on any icon it adopts.

The item, armour and weapon icons under `Demo/Textures/Icons` are **not** written by any step
here — they come from `Tools > Equipment Icon Generator`, which only ever runs when you run it.

### Frozen areas

Four roots on the island are **finished work, and a regenerate no longer builds over them**:
`Village` (with its measured interiors and the watchtower), `BanditCamp`, `Crypt` (the surface
entrance) and `Cliffs`. Their rules have not changed in a long time and what happens to them
now is hand-tuning, so the scene is the source of truth and the code is the record of how they
were made. **Regenerate Island Scene** builds each of them only when its root is missing, and
logs the ones it kept.

`Open MMORPG > Demo > Regenerate Settled Areas (village, camp, crypt, cliffs)` is the way back:
it destroys all four, builds them from the rules and rebakes the navmesh. **Run it after
changing one of their generators** — a full regenerate will not pick the change up, which is
the whole point of the freeze.

One seam worth knowing: the nature scatter keeps off the houses via `InsideBuilding`, which
reads the `HouseLayout` constant rather than the scene. Move a house a long way by hand and a
later regenerate can scatter rocks through where it now stands. Move the constant with it, or
keep the move small.

## Rebuilding the demo

Each step consumes the previous one's output, so run them in this order:

1. `Quaternius > Split Base Bodies Into Equipment Slots`
2. `Quaternius > Fix Foliage Materials` (one-off)
3. `Quaternius > Build Props Materials`, then `Quaternius > Remap Props Materials` (one-off)
4. `Open MMORPG > Demo > Build Weapon Prefabs`
5. `Open MMORPG > Demo > Build Terrain Tree Prefabs`, which also runs `Apply Foliage Wind`
   (below). Run `Apply Foliage Wind` again after `Collect Demo Art`, which brings the
   nature materials in on plain Lit - `Collect` does it for you at its end
6. `Open MMORPG > Demo > Build Items`, then `Build Harvestables`, then `Build Skill
   Effects`, then `Build Skills` (the skills point at the weapon types and missiles the
   item builder writes and at the effect prefabs; they draw their own icons, the ground
   markers the area ones spawn, the aiming circles, and the meteor that falls on Meteor's
   circle). `Build Skill Effects` keeps the sounds `Wire Audio` has put on its prefabs, so it
   is safe to re-run on its own. It also builds Frost Nova's ice and frost, and the ice a
   frozen character stands in, which it adds to the game instance's freeze effects
7. `Open MMORPG > Demo > Build Character Models` (reads the skill list to give each skill
   its own clip; without `Build Skills` first, every skill animates as a spell cast). It
   also puts `HumanoidFootIK` on each humanoid body and lowers the forward jogs, which float
   4.5cm; `Build Foot IK` does only those two, in place, for bodies built before it existed,
   and also puts `QuadrupedFootIK` on the animals - deer, collie, wolf (so the pup) and
   the horse - which `Build Wildlife` and `Build Mounts` add on a rebuild
8. `Open MMORPG > Demo > Build Character Entities`, then `Build Mounts` (which also puts the saddle on the horse;
   `Build Saddle` does only that, in place, and `Dump Saddle Fit Inputs` refreshes what `Art/Saddle/Source~` fits against)
   **Both read two hand-authored template prefabs that no step produces:**
   `Demo/Prefabs/GamePlay/CharacterEntities/BaseCharacter.prefab` and `BaseEnemy.prefab`.
   They are the tuned entity minus its model, and everything player- or enemy-shaped is
   cloned from them. Nothing references them at runtime, so a cleanup sweep can delete one
   unnoticed - `BaseCharacter.prefab` was, until 2026-09-23. The step then says `No template
   at ...` and names the `git checkout` that restores it from the kit repo. No other step
   may write to them: the skin-tone and size sweeps skip `DemoEntityBuilder.IsTemplate`. `BaseEnemy.prefab` was rebuilt on 2026-09-16 by
   taking `DemoBanditMale`, deleting its `Model` child and stripping the three sound
   components the audio wiring adds per entity; rebuilding the deer from it reproduced
   the existing deer transform-for-transform, which is how it was checked.
   The step also gives both player bodies a `DashAttackHandler` set up for Charge (without
   one, Charge's arrival damage never lands), and points the GameInstance prefab at
   `Demo/GameData/DemoEntitySetting.asset`, which stops the kit adding a blank one to every
   monster - that blank handler threw a NullReferenceException on every knockback.
8b. `Open MMORPG > Demo > Build Wildlife` - the deer, the village collie and the wolves.
   After the entities, because it clones `BaseEnemy.prefab`, and **before the island
   scene**, whose spawners reference `DemoWolf.prefab` by asset.
9. `Open MMORPG > Demo > Build Island Terrain`
10. `Open MMORPG > Demo > Regenerate Island Scene` (rewrites `DemoMap.unity`; the `Authored`,
    `Npcs`, `Mounts` and `Wildlife` roots are kept, and the four frozen areas above are built
    only if missing — use `Regenerate Settled Areas` to rebuild those). To put a spawn area
    added to `BuildSpawners` into the map without rebuilding it, run `Add Missing Spawners`,
    which adds only the areas the map lacks and leaves the rest where they stand.
    Every monster spawn area stands its monsters on baked spots - bare ground on the
    navmesh with a path back to the village - from `Bake Monster Spawn Spots (writes
    DemoMap)`. The regenerate, `Rebake Island Navmesh`, `Add Missing Spawners` and `Place
    Wildlife` run it for you; **run it yourself after moving or resizing a spawn area by
    hand**, since its spots move with it. Without spots an area finds its own ground with
    the kit's ray, which stops at the area's own height and misses everything downhill of
    it - the bandit camp spawned almost nothing that way.
11. `Open MMORPG > Demo > Regenerate Dungeon Scene` (rewrites `DemoDungeon.unity`, the crypt
    under the hills, keeping `Authored`; also writes its map info and the `DungeonGate` warp
    entity)
12. `Open MMORPG > Demo > Build NPCs And Quests`
8c. `Open MMORPG > Demo > Build Pet` - the Pup's Collar, which calls the wolf pup.
    **After `Build Wildlife`**, which is where the pup itself is built alongside the wolf
    whose mesh and clips it shares. `Wire Game Database` after, or the pup is a summon the
    client cannot spawn.
10a. `Open MMORPG > Demo > Build Combat Data (elements, ailments, sets)` - three damage
    elements, the three ailments the mage's and ranger's skills leave behind, and set
    bonuses on the three armour sets the island drops. After the items and the skills,
    because it writes onto both; `Wire Game Database` after it, for the element and
    ailment lists. It also points `GameInstance.defaultDamageElement` at `Physical` -
    left null the kit invents a nameless element at runtime, which is why the demo
    worked with no elements and why nothing could resist anything.
10b. `Open MMORPG > Demo > Build Supplies (arrows, scrolls, gems)` - the arrows the bows
    now need, a scroll that returns a player to the shrine they are bound to, three socket
    gems, and the sockets themselves cut into the best weapon and body armour of each
    class's line. Late, like `Build Gear Upkeep`: the arrows are crafted from the timber
    `Build Harvestables` writes. **Follow it with `Build Progression` (the arrow recipe),
    `Build NPCs And Quests` (the pedlar's board) and `Wire Game Database`** - a ranger with
    no arrows cannot fire at all.
11. `Open MMORPG > Demo > Build Guild` - three guild skills and six crests. Independent of
    everything else; only `Wire Game Database` has to follow it, or the two lists stay
    empty and a founded guild has nothing to spend its levels on. The crests are drawn
    into a gap and never painted over, like the skill icons.
11a. `Open MMORPG > Demo > Build Craft Stations` - the cookfire, forge, fletcher's bench
    and apothecary's stall. They live under `Village/Props`, so **`Regenerate Settled
    Areas` removes them and this has to run again**. Each one pins its own
    `sceneObjectId` (`Station_Forge` and so on): a scene building's database key is
    `ChannelId_MapId_SceneObjectId`, and a generated id renumbers when the village is
    rebuilt, which orphans the saved row and used to kill the map server on startup.
    Rebake the navmesh afterwards - the fletcher's bench is a new obstacle.
11b. `Open MMORPG > Demo > Build Player Buildings` - the campfire the player carries as a
    kit and sets down, and the `BuildingItem` that is its kit. After `Build Items` and
    `Build Harvestables` (it burns timber and cooks venison), and **before**
    `Build NPCs And Quests` and `Build Progression`, which put the kit on the pedlar's
    board and in the craft list. `Wire Game Database` last, as ever, or the item exists
    and is not in the database.
11c. `Open MMORPG > Demo > Build Homestead Pieces` - the snap-together house: foundation,
    wall, window wall, doorway, door, roof, strongbox and carpenter's bench, each a
    `BuildingItem` kit with a rendered icon (a PNG already in `Textures/Icons/Items` is
    kept). The bench makes the other kits from timber and stone; the bench kit itself is
    made anywhere from the HUD's Craft window. After `Build Items` and `Build Harvestables`,
    and **before** `Build NPCs And Quests`, whose carpenter (Oswin, on the plot) sells the
    kits. Also turns the controller's build grid off and its turn to 90 degrees.
    Then `Open MMORPG > Demo > Level Homestead Plot (writes terrain and DemoMap)` - cuts the
    terrace on the north-east shore the pieces are built on, reseats whatever stands on it,
    **relays the `Harvestables` root** so no tree grows out of a floor, and rebakes the
    navmesh. Safe to repeat, and `Build Map Server` after it. A full `Build Island Terrain`
    cuts the same terrace on its own - the pad is in the island's height function - and
    the harvest and scatter rules keep off it.
12a. `Open MMORPG > Demo > Build Gear Upkeep` - durability on every weapon, shield and
    piece of armour, the three `ItemRefine` assets that carry the smith's repair and
    refine prices, and what each piece dismantles into. **After `Build Harvestables` and
    `Build Progression`**, not with `Build Items`: gear breaks down into `Stone`, `Timber`
    and `Leather`, and those three are created by those two later steps. Run from inside
    `Build Items` the references are null on a clean rebuild, silently. Re-running
    `Build Items` does not undo it.
12b. `Open MMORPG > Demo > Build Shrines` - the resurrection shrine outside the village,
    which a player binds their spirit to and respawns beside. After the island scene and
    the terrain, because it seats every piece of itself on the ground it finds and clears
    the painted grass from under its own flagstones. It adds stone to the scene, so
    **rerun `Rebake Island Navmesh` after it** - and rerun this after
    `Build Island Terrain`, which repaints the ground cover back through the paving.
12c. `Open MMORPG > Demo > Build Sundries (safe area, warps, tomes)` - the last five item
    types the demo had no example of (a Town Charm, a Passage Stone, a Tome of Insight, a
    Sealed Cache and a Scroll of Mending), plus the `SafeArea` over the village that the
    charm needs to have anywhere to go. **Last of the data steps**: the cache's reward
    table names potions and gems from `Build Items` and `Build Supplies`, and the scroll
    names a skill from `Build Skills`. `Build NPCs And Quests` after it, for the pedlar's
    board and the watchtower guard's warp, then `Wire Game Database`.
    The safe area is a trigger with no renderer, so it needs **no navmesh rebake** - but
    note what it does to the village: nothing inside can be damaged, monsters that wander
    in turn round and go home, duels are refused, and **a campfire cannot be built in
    town**. That last one is the kit's rule, not a broken item.
13. `Open MMORPG > Demo > Wire Game Database` (registers the skills, hands each class its
    four and the Hierophant his three, and points his summon at the cultist entity — which
    has to happen here, because that entity does not exist yet when the skills are built;
    also opens the way between the island and the crypt: the warp portal database, the map
    spawn list and the build settings)
13b. `Open MMORPG > Demo > Wire Demo UI` - points the UI prefabs at the demo's own data.
    After 13, because it needs the armour types to exist. The UI began as a copy of the
    kit's template and a copy brings the layout but not the references: the equipment
    slots had **no armour type at all** (an NRE every time the panel opened), the damage
    number was unassigned, and every gauge Image was `Filled` with **no sprite** - which
    makes Unity ignore `fillAmount` entirely and draw the bar full forever, so HP numbers
    moved while the bars did not.
13c. `Open MMORPG > Demo > Build Minimap` - photographs the island from above into
    `Demo/Textures/MinimapIsland.png`, writes the map bounds onto `BaseMap.asset`, and points
    the minimap camera and the HUD's RawImage at `MinimapRenderTexture.asset`. After the
    island scene (step 10), and again after any rebuild of it, or the map stops matching the
    terrain. The sea is tinted from the heightmap afterwards: photographed alone it is a
    transparent surface over a sand seabed, which reads as more beach. It also runs
    `Build Minimap Quest Markers` (available on its own), which draws the NPC quest markers -
    gold `!` on offer, silver `?` under way, gold `?` ready to hand in - into
    `Demo/Textures/MinimapMarkers/` and puts them on `NpcMiniMapCanvas.prefab`. The template's
    markers were `Text` glyphs that never rendered, so every state drew as the same plain
    disc; a hand-in marker also pins to the minimap's edge when its NPC is out of frame.
13d. `Open MMORPG > Demo > Build Menu Stage` - the home menu: a stone terrace of Quaternius
    pieces with the painted valley behind it, warm lighting, the `OPEN MMORPG` title, and a
    repaint of the menu panels. Rewrites the `MenuStage` root in `01Home.unity` and edits
    `CanvasGlobal`/`CanvasHome`. Independent of the rest; re-run after changing the
    backdrop art.
13e. `Open MMORPG > Demo > Make Windows Draggable` - puts `UIWindowDrag` on every in-game
    window's title bar (the template's `Window/Title`), so windows move by their title, stay on
    screen, come to the front when grabbed, and remember where they were left; a double-click
    on the title puts one back. Added to each **dialog prefab**, not the canvases that nest
    them, so it reaches every canvas without per-instance overrides. The home screens are left
    alone. Safe to re-run; it only adds what is missing.
13f. `Open MMORPG > Demo > Build Character Sheet` - WoW-style character window: the equipment
    slots move out of the Items dialog to either side of a turnable 3D preview of the character
    (`UICharacterPaperDoll`: a copy of the playing character on a stage far below the world,
    filmed into a RenderTexture under its own lights), weapons beneath it, attributes/stats/
    resistances in tabs underneath, and the name, level and class in the header. The Items
    dialog becomes the bag alone; its equipment block is switched off, not deleted. After 13b
    (slot armour types are mapped by slot name, which it keeps). Lays out only when the sheet
    is missing, so hand edits survive; `Regenerate Character Sheet` lays it out again. Every
    run also binds the armour and resistance rows (Physical, Fire, Frost), adds
    `UICharacterStatsReadable` (damage on one line, zero stats shown as zero, each armour value
    with the share of a hit it takes off), and puts a `HideWhileActive` on each slot's icon so
    the slot's name ("Arms") hides while the slot is filled.
13g. `Open MMORPG > Demo > Bind WoW Window Keys` - the windows on World of Warcraft's keys:
    C character, B bag, P skills (the spellbook), L quests, O friends, J guild, Esc system.
    Esc goes through `DemoEscapeKey` rather than the kit's toggle list, so that it first cancels
    a skill being aimed and only otherwise opens the system menu.
    The party window has no WoW equivalent and is on the menu bar only. Also re-applies the
    gameplay keys, with building rotation moved off J/K to [ and ]. Writes the toggle list and
    `DemoEscapeKey` on `CanvasGameplay` and the key settings on `GameInstance`; safe to re-run.
14. `Open MMORPG > Demo > Build Player Controller`
14a. `Open MMORPG > Demo > Build Feedback Effects` - the in-world feedback the template left
    broken: animator controllers for the six damage/heal numbers (they sat at alpha 0 with no
    controller, so none ever showed) and for the level-up flourish; the click-to-move ring
    (`TargetObject.prefab`, a legacy Projector URP never drew), laid to the slope by
    `GroundAlignedMarker`; and the safe-area and vending signs, hung over the nameplate by
    `NameplateSign` instead of lying at the feet. Edits those prefabs in place and points
    the demo controller at the ring, so it can run before or after step 14.
14a2. `Open MMORPG > Demo > Build Footstep Effects` - boot prints in the beach sand and splashes,
    ripples and a wake in the sea (see "Footsteps" below). Draws its textures, builds the shared
    particle hub and puts `FootstepEffects` on both player entities in place; the entity builder
    adds it on a rebuild once the hub exists. Independent of the rest, so any time after step 8.
14b. `Open MMORPG > Demo > Wire Audio` — hooks the clips under `Demo/Audio` up by file-name
    family (Footstep*, SwordSwing*, ArrowFire*, ManHit*/WomanHit*, ...) to the character
    entities, the horse, the character models and the weapon items, and logs the families
    still missing. The entity, mount, model and item builders call the same code, so this
    only needs rerunning when clips are added. The island's ambience beds are built with
    the sea (`Rebuild Sea`).
15. `Open MMORPG > Demo > Collect Demo Art` — last, and after any rebuild: copies every
    library asset the demo still references into `Demo/Art` (textures resampled) and
    rewrites the references, so the demo carries everything it uses. Animation clips are
    the exception and go to **`Demo/Animations`**, extracted one at a time, so that every
    animation the demo owns — extracted, downloaded or generated — is in one folder rather
    than split across two. `Verify Demo Is Self-Contained` reports anything left outside
    and any reference that points at nothing; it counts anything under `Demo/` as inside.

Audits that measure the result and report every fault: `Audit House Interiors`,
`Audit Scene Placement`, `Audit Outfits`.

**The bandits wear the ranger's outfit dyed black** (2026-10-05): in the ranger's own green a bandit
looked exactly like a player ranger. `DemoOutfitPaletteBuilder` writes a recoloured copy of the sheet,
`Demo/Art/Characters/Textures/T_Ranger_Bandit_BaseColor.png`, and `MI_Ranger_Bandit` beside `MI_Ranger` -
never `MI_Ranger` itself, which the players' ranger armour shares. It recolours by hue rather than tinting
(cloth to a cool near-black that keeps its shading, leather a third darker, the pale fittings and the pauldron
blackened to a dull grey); the hue bands are measured off the sheet and documented in the builder.
`Variant.Palette` in `DemoCharacterBuilder` says which bodies wear it, so `Build Character Models` keeps it;
`Open MMORPG > Demo > Build Bandit Colours` puts it on the built models without a rebuild (the entities nest
the models and follow). The texture and the bandit set's icons are generated only where there are none, so
they can be painted by hand; `Regenerate Bandit Colours (overwrites the texture)` recolours them again.

**And they drop that black set, a step worse than the green one a player makes** (2026-10-05). Six items,
`BanditHood` .. `BanditPauldron`, are bandit copies of the ranger pieces made by `DemoItemBuilder` (the
`WithBanditSet` rows of its armour table): the same garment as a prefab variant wearing `MI_Ranger_Bandit`
(`Prefabs/GamePlay/Equipments/Outfits/*_Bandit.prefab`), the ranger piece's icon recoloured with its icon
framing copied, **four-fifths of the armour** (`BanditArmourGrade`; the tooltip rounds, so a jerkin reads 6
against 8) at three-fifths of the price, and so a little less durability and leather back when dismantled.
They have a set of their own, `Bandit's Blacks` (`DemoCombatDataBuilder`), about three-quarters of the ranger's
at each step, and the jerkin takes no gem. Measured live at level one, six pieces: bandit armour 23.2, move 5.10,
attack speed 1.27, crit 9.5%; ranger armour 29, move 5.30, attack speed 1.30, crit 10.5%. Both bandit families
drop them; nothing drops the green set now, **which is made at the Fletcher's Bench** - all six pieces since the
same day, the jerkin, breeches and pauldron having been loot-only until then (`DemoProgressionBuilder`, then
`Build Craft Stations`, which rewrites DemoMap - back it up first). `Open MMORPG > Demo > Build Bandit Set` makes
the items, their upkeep, the set and the loot tables on a built project; the full pipeline makes the same through
`Build Items`, `Build Gear Upkeep`, `Build Combat Data` and `Wire Game Database`. `Build Map Server` after.

**The demo's animation is CC0 only.** Its clips come from Quaternius's two Universal
Animation Libraries, both CC0, which sit outside the kit in `Assets/Plugins/Quaternius/Animations`
(UAL1 and `UAL2/`); `Collect Demo Art` extracts just the clips the demo plays into
`Demo/Animations`. Anything dropped into that folder by hand ships with the kit, so it must
be CC0 too.

`Open MMORPG > Demo > Refresh Skill Animations` re-applies each skill's clip, trigger and
speed from `DemoSkillBuilder` to the character models, and touches nothing else. Use it
after tuning a skill's animation instead of `Build Character Models`, which would need the
whole entity chain run again after it. Then `Collect Demo Art`, for any newly used clip.
`Open MMORPG > Demo > Refresh Weapon Attacks` is the same for the basic attack: it replaces
only each melee and staff set's attack clips on the models (bows are skipped - their attack
depends on whether that character can charge a shot).
`Open MMORPG > Demo > Refresh Action Masks` puts the upper-body masks on the models already
built (see below); `Build Character Models` applies them itself.
`Open MMORPG > Demo > Build Weapon Sheathing` is the same bargain for the draw and sheathe
(see below).

**Action animations are masked so they do not override the legs** (2026-10-03). A kit action
state with no avatar mask plays on every bone, so the legs and hips of a pickup or a hit
reaction replaced the jog's. `DemoAnimationSet.UpperBodyMask` is a humanoid body-part mask
(spine, head, arms, fingers and hand IK on; root, legs and foot IK off) kept at
`Demo/Animations/UpperBodyMask.mask`. Pickup always uses it; hurt, charge, attack and spell
states use it only while moving or airborne (`avatarMaskWhileMoving` / `avatarMaskWhileAirbourne`),
so standing still they play on the whole body as before. Sprint-type skill clips (Charge, Fleet
of Foot) and the ladder states stay full-body. `DemoAnimationSet.ApplyMasks` is the one policy,
shared by the generators and the refresh; a mask already set on a state is never replaced. It is
not the kit's `TopMask` under `Core/Resources`, which is a bone list for the kit's own rig.

**Weapons can be sheathed** (2026-10-04). Characters **spawn sheathed**, an attack, a skill or a
harvest swing draws first, `Z` draws or puts away the weapons by hand, and a drawn weapon is put away again
after 8 seconds with no attack, skill, harvest swing or damage taken and nothing chasing or aiming
(`startSheathed` and `idleSheatheDelay` on `Demo/GameData/DemoEntitySetting.asset`). The mechanism is the kit's own - the
synced `IsWeaponsSheathed` flag, each item's `sheathModels`, the holster clips on
`DefaultAnimations` - so the demo supplies data and one controller rule, nothing in Core.
`DemoSheathBuilder` owns the data: a `SheathBack` socket on `spine_03` of each equipment-driven body,
the draw and sheathe clips (UAL1's `Sword_Enter`/`Sword_Exit`), and each weapon type's pose on that
socket. **Everything rides on the back** because those clips are a draw over the right shoulder and
neither library has a hip draw. The socket's origin is *measured*: it is where the right hand's weapon socket is
at the hold of `Sword_Enter` (0.65s), per body. **The weapons are not hung from that point** (they were until
2026-10-05: the point is 0.74m above the hips, above the top of the head, so the hilt stood behind the head, the
blade lay along the reach at 45 degrees with its tip half a metre out, and the staff and bow towered over the
character). The socket's frame is the body's, read in the idle (X to the character's left, Y up, Z out of the
back), and `DemoSheathBuilder.TryCarry` lays each weapon at a realistic spot as an offset from the hand's point: the
sword's grip 12cm lower over the right shoulder with the blade 25 degrees off vertical to the left hip, an axe or
pick head at the right shoulder blade, the staff's middle on the spine at mid-back (head over the right shoulder,
foot by the left knee), the bow's handle on the spine at the shoulder blades (upper limb over the left shoulder),
and the shield centred at the small of the back outside the sword's blade with the hilt over its rim. The draw
clips are untouched, so the weapon moves at the swap; some part of every weapon is within 7-13cm of where the hand
closes (the shield 21cm). **How far off the back each rides is measured against nine outfits in six poses**
(the back surface baked into a heightfield, each weapon's vertices tested against it), and the target is
**closeness, not clearance**: searched for a safety margin, every weapon stood 6-9cm off the back and read as floating.
The user set both bows by hand (2026-10-05, 9cm in) and preferred a little clipping into armour to that, so the rest
match the bow: standing, on the peasant and ranger outfits, the nearest point dips about 1cm in; plate and robes
clip 6-8cm. Judge a change by side-view renders on several outfits.
**A sheath pose already on an item is kept** by `Build Items` and `Build Weapon Sheathing` - they may be hand edits,
as the bows' are. `Open MMORPG > Demo > Reset Weapon Sheath Poses (overwrites hand edits)` rewrites them all from
`DemoSheathBuilder` (whose bow figures are the user's, so a reset reproduces them), and is the way to apply a change
to the carries there.
**The bow has a draw of its own** - the hand-authored `Bow_Unsheath`/`Bow_Sheath` in `Demo/Animations` (never
regenerate them): the left hand, which holds a bow, reaching over the *left* shoulder. So a second socket,
`SheathBackLeft`, is measured from that clip the way `SheathBack` is from the sword's, the bow is carried from it the
mirror-image way across the back, and the clips are written on the Bow weapon set's holster, which the kit looks up by
weapon type before falling back to the default pair.
`DemoWeaponSheathing` (kit side, `Demo/Scripts`) is on every player entity, on every peer, added by
`DemoEntitySetting` as the entity wakes: **the sheathed start has to be set before the spawn**, when a sync
field only stores the value and the spawn baseline carries it to everyone, so no client sees a weapon in a hand
that then goes away. `BowEquipmentEntity` ignores a bow that is stowed, and the character sheet's paper doll
always shows the weapons drawn.
**The kit's weapon swap needed three repairs**, made in the demo's model class `SeatAwarePlayableCharacterModel`
(`SetEquipItems`), not in Core. The kit swaps the weapon models partway through the clip and believes the old ones are
shown until then, so: a draw asked for before a sheathe had swapped was lost (the sheathe finished anyway, and the
character fought empty-handed with the flag saying drawn - "sometimes I cannot unsheath"); any appearance update before
the swap restarted the clip ("it plays twice"); and every draw and sheathe played on both action layers, because the kit
records what is shown with `CharacterItem.Clone()`, which gives an empty hand a new unique id, so an empty left hand
always "changed" - a bow's draw ran the sword's reach underneath, and the first swing was hidden under the rest of
the draw. Now a change taken back before the swap stops the gesture and leaves the weapon where it is, a repeat is left
to the routine already running, empty hands compare as empty, and a swing or cast stops the holster layer. The
controller also draws for the kit's own way into a fight (a second left click on an enemy or a node locks on and
closes in without asking for the weapon, so a sheathed character walked up and stood there), and a Z press made
mid-swing, mid-draw or mid-sheathe is kept and played when the hands are free instead of being dropped.

The draw and sheathe **sounds** are the `WeaponUnsheath*` and `WeaponSheath*` families in `Demo/Audio`, played by
`WeaponSheathSounds` on each player entity (wired by `Wire Audio`, or by `Build Weapon Sheathing`). It listens to
the synced sheathed flag, so every client hears every player, and times itself off the same holster clip the model
swaps the weapon by: a draw sounds the instant the weapon reaches the hand, a sheathe 0.22s before it is on the back
(the clip's loud part is the blade sliding home, which ends with the weapon seated). It makes no sound where the
kit plays no motion - a spawn, mounting or dismounting, a character with nothing in its hands.

`Open MMORPG > Demo > Build Weapon Sheathing` gives it to what is already built - the two player models'
socket and holsters, the items' `sheathModels`, the key - and is safe to repeat; the full build
(`Build Items`, `Build Character Models`, `Build Player Controller`) produces the same. Then
`Collect Demo Art` for the two clips, and `Build Map Server` if the MMO flow is in use. The holsters
are generated each run, not hand-tuned.

**A bow comes with a quiver on the back, and its arrows run out** (2026-10-05). The Malagen quiver is a
second model on both bow items - in `equipmentModels` and `sheathModels` alike, on `SheathBack`, in one
pose - so the kit shows it whenever a bow is carried, drawn or put away or in the other weapon set, and
nothing decides that at run time. `DemoWeaponBuilder.BuildQuiver` (part of `Build Weapon Prefabs`, or
`Open MMORPG > Demo > Build Quiver` on its own, which also writes it onto the bows) builds `Quiver.prefab`:
the model is authored *worn* - leaning 40 degrees, a metre and a half up - so its axis is measured (the
vertices' principal direction) rather than stood up by a fixed turn, its origin is the middle of the mouth,
and twelve nested `Arrow.prefab`s stand in it, fanned, staggered, each rolled at random (seeded).
`QuiverArrows` on it shows one arrow for each the character has, up to twelve: the quiver empties over the
last dozen shots. The count is `QuiverArrowCount`'s: the owner counts their own bag (it is synced to them),
and everyone else reads a byte the server syncs (count + 1, so 0 means "not heard" and shows a full quiver),
because the bag reaches its owner only. The component is added by `DemoEntitySetting`, so **`Build Map
Server`** after changing it - against a server built before it, other players' quivers show full. An arrow
leaves the quiver when the hand takes it -
`BowEquipmentEntity` says when one is in the fist - not when the server spends it at the loose.
**The quiver lies under the bow.** It hangs from the right shoulder (mouth on the spine below the neck, nocks
behind the right shoulder where the hand reaches) to the left hip at 28 degrees; the bow crosses it the other
way, so the bow was lifted 3cm off the user's hand-set pose (`BowOverQuiver`) to rest on it - no quiver whose
arrows could be reached fitted under the bow where it was. The pose was searched like the weapons' (eight
bodies, idle and a jog: within about a centimetre of the everyday outfits, clear of plate and robes). A pose set
by hand on the quiver's **sheathed** entry is kept and copied to the hand set.
**The Bandit Archers wear one too, for show** (user's call: it never empties - a monster has no bag and its shots
cost nothing). `DemoEntityBuilder.WearQuiver` bakes it beside the bow in `ArmMonster`, at the bow item's quiver pose,
on a `SheathBack` socket it measures on a scratch copy of the body (the archers' models have none), and takes the
counting component off. `Build Quiver` re-bakes both archers without the entity chain. **And every archer drops 3-5
arrows**: `MonsterSpec.Spoils` writes a rate-1 drop, which the kit hands out before rolling the rest, and raises the
drop count from 1-3 to 2-4 so the other loot is as it was. `Open MMORPG > Demo > Refresh Monster Drops` rewrites only
the drop tables; `Build Map Server` after, since drops are rolled on the server.

**The mage's staff is a melee weapon, and Intelligence is spell power** (2026-09-23). The staff
swings (`Sword_Heavy_A`) instead of firing the Arcane Bolt missile as a free, uncooled basic
attack; the spells carry the damage. `Build Progression` owns the attribute side, because the
attributes do not exist until it runs: it gives Arcane Bolt, Frost Nova and Meteor damage per
point of Intelligence (`SpellPower`) and the staffs their Intelligence, plus a level of Arcane
Bolt on the Elder Staff (`Foci`). So after `Build Items` or `Build Skills`, run `Build Combat Data`
(elements live on the skills) and `Build Progression` again.

Casting in melee needs two more things, both in place. **A hit only sometimes breaks a cast**:
`Build Character Entities` swaps each player's and humanoid monster's skill component for
`InterruptChanceUseSkillComponent`, which rolls `CastInterruptChance` (35%) per hit instead of the kit's
certainty. And **Frost Nova is the mage's crowd control**: one burst that freezes everything
within 4.5m for three seconds (the kit's Freeze ailment - no moving, attacking or casting), which
is the window for a Meteor and a Bolt.

`Open MMORPG > Demo > Import Mixamo Animations` is **for local use only**. Mixamo lets its
animations ship inside a finished game but not as raw files in an engine template, which is
what this demo is - so its output goes to `Assets/Animations/Mixamo/Edited`, outside the kit,
and nothing the demo ships names a Mixamo clip. Six did until 2026-09-23; they were replaced
from UAL2 and UAL1 and the hand-edited originals moved to that folder. To try one locally,
point a skill's `Clip` in `DemoSkillBuilder` at it: it resolves, and `Verify Demo Is
Self-Contained` will then report the demo as reaching outside itself, which is the reminder
not to ship it. Drop the FBX in `Assets/Animations/Mixamo`
(they are ~31MB each because Mixamo includes the skinned mesh), add it to the table in
`DemoMixamoImport`, and it sets the rig, throws away the 43 library takes Mixamo hands back
with every download, applies the orientation offset and extracts one `.anim` into
`Assets/Animations/Mixamo/Edited`. `Discard Staged Mixamo FBXs` then deletes the sources. The tool's table
is the record of which clip came from which download, so **rename clips there rather than in
the Project window** — a rename in the editor leaves the table pointing at the old name, and
the next run writes that name back as a second asset.

The `Quaternius >` items are the library's own tools and live beside the library in
`Assets/Plugins/Quaternius/Editor`.

## The island goes magenta from far away

If the whole island turns solid magenta once you are more than ~220 m from it (`Terrain.basemapDistance`),
the editor's copy of URP's far-terrain shader is broken, not the island: `Build Map Server` switches the
editor to the Dedicated Server subtarget, where that shader is imported as an empty stub, and switching back
does not reload it (nothing in a scene references it - the terrain material only names it as a dependency).
`TerrainShaderRepair` (moved to the kit's `Demo/Editor` on 2026-10-07, since anyone building a map server needs it) notices the stub after the editor is back on the Player subtarget and reimports it by
itself; `Open MMORPG > Demo > Repair Terrain Shaders` does the same by hand. Nothing on disk is wrong, so
there is nothing to rebuild.

## Wind

The trees, bushes and grass sway. `FoliageWind` (the `Wind` object in `DemoMap`, built by
`DemoSceneBuilder.BuildWind`) sets a handful of shader globals each frame; strength, direction,
how far trees and grass lean and the weather-like rise and fall of the breeze are all fields on it,
live in the editor. `Open MMORPG > Demo > Apply Foliage Wind` puts the art on the shaders and is safe
to repeat; `Restore Stock Terrain Detail Shader` is the way back. With no `FoliageWind` in a scene the
wind is off and every material renders as plain Lit, which is why the menu hillside (that has its
own `WindSway`) and the dungeon need nothing.

Three kinds of thing, three routes, because Unity draws them three ways:

- **Trees** - the harvestable nodes. The pack's bark and canopy materials are switched in place to
  `OpenMMORPG/Demo/Wind Foliage` (URP Lit with a vertex wind step). A vertex bends by its height
  above the tree's pivot, squared; canopies also shiver.
- **Bushes and plants** are terrain *trees*, so they take a material too, but a `_Small` copy of
  it (`Leaves_Small`, `Flowers_Small`, `Grass_Small`, `Leaves_NormalTree_Small`) - the same
  `Leaves_NormalTree` is a twelve-metre canopy on one prefab and a one-metre bush on another, and
  those want opposite strengths. Only the flattened `Prefabs/Terrain` prefabs are repointed.
  Hand-tuning a `_Small` material is safe: reruns never overwrite an existing one.
- **Grass, clover, ferns, flowers** are terrain *details*, which the terrain welds into patch
  meshes and draws with URP's own hidden detail shader, **ignoring the prefab's material
  entirely**. No material can move them, and the patch has no per-plant pivot to measure from. So
  `Apply Foliage Wind` points URP's `UniversalRenderPipelineRuntimeTerrainShaders.terrainDetailLitShader`
  (a setting in the URP Global Settings asset) at `Demo/Shaders/DemoWindTerrainDetail.shader`, a
  copy of the stock one with the same wind, and `FoliageWind` uploads the terrain's heights as a
  texture so a blade can measure from the ground. It is a project-wide setting; it is inert on any
  terrain without a `FoliageWind`.

Not done on purpose: GPU-instancing the details (it renders under URP here, and lets a material
carry the wind, but draws the meadow far denser and makes every blade cast a shadow), and motion
vectors for the wind (it is slow enough that blurring it is pointless).

## Weather

Showers come and go on their own. `WeatherSystem` (the `Weather` object in `DemoMap`) decides when it
rains, makes the rain, plays its sound, and hands the cloud to `DayNightSkyCycle` and the storm to
`FoliageWind`. `Open MMORPG > Demo > Build Weather` adds it to the open scene, or repairs it, and
**leaves every value you have already set alone**; `Rebuild Weather (resets its settings)` starts it
over. A full scene regeneration builds it with the rest of the lighting.

- **When.** Pure function of the server's clock (`ServerTimestamp`, the same offset-corrected time the
  day cycle rides on; the local UTC clock with no network). Time is cut into periods
  (`periodMinutes`, 15), each period is dealt a shower or not (`showerChance`, 0.6) and the shower's
  start, length (3-8 min) and peak come from a hash of the period number and `seed`. Nothing is sent
  and nothing is stored, so every player sees the same sky, a player who logs in mid-shower walks into
  it, and a restart does not reroll it. About a fifth of the time is rain and a third is overcast.
  `Open MMORPG > Demo > Weather > Log Forecast` prints the next showers in local time.
- **Cloud first.** The sky closes over `cloudLeadSeconds` before the first drop and clears
  `cloudLagSeconds` after the last. `DayNightSkyCycle.Overcast` greys the sky (a desaturation in
  `Demo/DayNightSkybox`, not just a tint - a darker blue sky is still not an overcast one), the sun,
  the ambient and the fog colour by one rule, and takes the sun's intensity, shadow strength and the
  fog distances with it. It now **owns the sun's shadow strength and the linear fog distances**
  (`clearShadowStrength`, `clearFogStart/End`) the way it already owned the fog colour: tune them on the
  cycle, not on the light or in the Lighting window. At zero cloud every grade is an identity.
  `previewOvercast` shows the grey day in the editor without playing; leave it at 0 when saving.
- **The drops are placed, not simulated.** Each is cast down its own column from above the camera, so
  it ends on the ground, a roof, a wall or the sea surface (a trigger volume, so a plane at
  `seaLevel`) with exactly the lifetime it takes to arrive. Particle-system collision would stop rain
  where the physics world says things are, which for a trigger sea and a village of mesh colliders is
  not where anyone expects.
- **Rain stays out of the houses.** A drop is never made if the point it would land on has a roof over
  it (slanted rain under an eave or through a doorway is dropped), and a streak is shortened by its
  head lead - Unity draws a stretched billboard's leading end **10.1 ms of travel ahead of the
  particle** (24 cm at 24 m/s, measured with `BakeMesh`), so a drop given its exact lifetime pokes 24 cm
  into whatever it hits. Splash rings are queued by the emitter and placed on the hit point, and only
  on surfaces within about 25 degrees of level: a death sub-emitter puts the ring where the particle is
  when it dies, up to a whole frame of fall past the surface, which sank rings into roofs at random and
  left them floating inside the watchtower. Measured over eight vantage points: streaks drawn inside a
  roofed, walled room 0.05% -> 0.001%; rings inside one 1.5% -> 0%.
- **Sound.** The loop (`RainAndThunder`, found by file-name family like every other clip; the thunder is
  in the clip) is a 2D bed on the ambient volume slider, faded with the rain and started at a random
  point each shower so the thunder does not crack at the same second every time. A roof overhead halves
  it and low-passes it to 1.7 kHz (five up-rays from the listener, the character, not the camera); under
  the sea it is muffled harder. The `Nature` bed is ducked under the rain through
  `AmbientSoundLoop.RuntimeGain`, a property that is never serialised - writing `baseVolume` for this
  saved a ducked volume into the scene the first time it was tried in the editor.
- **Wind.** `FoliageWind.StormBoost` makes the trees sway harder in the rain, and the rain leans along
  `FoliageWind.Direction`.
- **Testing.** In play mode, `Open MMORPG > Demo > Weather > Rain Now / Clear Skies / Back To The
  Schedule` override the schedule (they ease over `easeSeconds`). Out of play mode `WeatherSystem.Tick`
  can be stepped by hand with `streaks.Simulate` and photographed; the Scene view does not animate it.

Not done on purpose: wet ground (it would mean a global wetness input in every ground shader), separate
thunder and lightning (the clip carries its own thunder, and a flash timed to a looping clip would be
wrong the day the clip changes), and weather in the crypt (it has no sky to close over).

## Footsteps

Boot prints in the beach sand, and splashes, ripples and a wake in the sea. `Open MMORPG > Demo > Build
Footstep Effects` draws the four textures it needs (`FootPrint`, `FootPrintNormal`, `WaterRing`, `WaterDrop` in
`Demo/Textures`, **only if missing** - delete one to redraw it), builds `FootstepEffectsHub.prefab` and puts
a `FootstepEffects` component on both player entities in place; `Build Character Entities` adds it on a
rebuild once the hub exists. Nothing is shipped from outside: it is all arithmetic, so there is no licence
to check. The tuning lives on the component (`FootstepEffects`) and the hub's colours; a rebuild of the hub
keeps the entities pointing at it, but **does not reset values already set on the component** - to take new
defaults, remove the component from the entity prefabs and run the step again.

- **It reads the feet, not the footstep sound.** The kit's footstep sound is a timer that knows nothing of
  where the feet are. `FootstepEffects` runs just after the kit's IK slot and reads each foot's three sole
  contact points (the ones `HumanoidFootIK` measures off the idle) against the ground under them: a foot
  **plants** when the nearest comes within 2.5 cm of the ground and has **stayed** there 0.05 s, and **lifts**
  when it is clear by 5 cm. The same events fall out of a walk, a jog, a strafe, a crouch, a landing and
  another player's synced animation, with nothing networked.
- **Two prints per step, and why (fixed the same day).** Measured on the beach, each foot printed twice,
  9-32 cm and a tenth of a second apart. Two causes, both on the downhill: the clearance was the world-lowest
  contact point against the ground straight under it, and that point flips heel to toe as the foot rolls, with
  the ground under the toe a few centimetres lower - so one footfall read as plant, lift, plant. Clearance is
  now each point's height along the ground's normal (one ray under the middle of the sole). And the jog brushes
  the sand once before it lands: the heel dips to a centimetre while still sweeping back at several metres a
  second, lifts 10-14 cm, and lands for real 30 cm back. That brush lasts a couple of frames, so a plant must
  hold for 0.05 s before it counts, and the print is laid from the settled pose. A foot also cannot print again
  within 30 cm of where it last planted. Re-measured: one print per foot per step, walking and jogging.
- **The beach.** A planted foot over terrain asks the splat map how much of the `Island_Sand` layer is under
  it (`FootstepEffectsHub.LayerWeight`, bilinear between the half-metre texels), and that is the print's
  strength - a print fades out where the sand gives way to grass instead of stopping at a line. Sand within
  1.2 m of the sea is wet (darker, crisper), and so are the boots of anyone who has just been in the water
  (six wet prints a soaked boot). In the surf, within a quarter metre of the sea level, a print lasts six
  seconds; dry sand keeps it thirty. A hard run kicks sand up; a landing from a real drop hits harder.
- **The sea is where it is drawn.** The physics sea is a flat trigger volume, but `Demo/StylizedWater` lifts and
  drops the drawn surface by up to 18 cm, and on a beach shelving at about 1 in 6 that moves the visible waterline
  a metre either way. Judged against the flat level, a foot standing on sand the swell had just uncovered was in
  the water and splashed. `StylizedWaterSurface` is a CPU copy of the shader's swell (`MakeWarp` + `WaveTrain`,
  height only) on the same clock - URP's `_Time.y` is `Time.time` in play - and every wet-or-dry question asks it,
  off the `seaMaterial` the builder wires (`Island_Sea.mat`). Checked by floating markers at its heights in play
  mode: the drawn water cuts every one at its equator, crest or trough, while markers at the flat level float
  clear or drown. **Change the shader's swell and that file must change with it.** Water under 3 cm deep
  (`splashDepth`) throws nothing, and the swell washing over a foot that is standing still only rings: measured
  standing at the very edge for 1,500 frames, no splash and no ring.
- **The sea's effects.** Going under splashes in proportion to the foot's speed and the depth; coming out sheds
  drops; a foot planted on the bed under water, which never comes up at knee depth, makes a ring; a body standing
  in the water breathes a slow ripple. Swimming is the same for hands and feet breaking the surface, plus foam
  laid behind the chest, widening rings and a little bow spray at speed.
- **One shared hub, not one effect per print.** A print has to outlive the foot that made it, so it cannot be a
  pooled `GameEffect` recycled in two seconds. Seven world-space particle systems that nothing plays by itself
  (`AlwaysSimulate`, or a looping system with no particles has empty bounds and is paused whenever the camera
  looks away) are shared by everyone in the scene; each has a particle cap and a full one drops the newest. A print
  is a flat quad laid along the terrain's normal 3 cm above it (the terrain mesh is coarser than its height map),
  **lit**, so it darkens with the evening instead of glowing. URP decals were not used: they need the depth
  texture, which is a project setting the demo does not own.
- **Depth.** The print is coloured like the sand itself (the pressed middle a little darker) and its relief is a
  normal map baked from a height field: the sole pressed 2 cm in with steep walls, a low ridge of pushed-up sand
  round it, the tread standing up in the bottom. The sun then lights one wall and shades the other, and the
  ridge catches the light, so the hollow reads as a hollow and turns with the time of day. It took tangents on
  the quads (`RecalculateTangents`, which also gets the mirrored left foot right), the renderer's Tangent vertex
  stream and `_NORMALMAP` on `MI_FootPrint`; the prints also receive shadows, or one under the character's own
  shadow stays sunlit. **The print systems are not GPU-instanced.** Mesh particles instance by default, and then
  the custom vertex streams describe per-instance data: with Tangent in the list, only the first print in each
  system drew and every other one - alive, opaque, in view - drew nothing. It looked like prints fading at
  distance until one was recoloured red in place and still did not appear. Past a few metres the normal map's
  mips flatten the relief, so the pressed middle is also darker (half the sand's brightness) to carry the print.
- **Scale.** Judge it in the real game view. Drops sized for a close-up read as petals at the camera's 4 m;
  the first pass was invisible there. Final: drops 3-8 cm, rings 0.9-2 m, print quad 36 x 17 cm (a 29 x 13 cm sole plus its rim).
- **Off for** a headless server, a menu preview (the entity is disabled), the dead, riders, climbers, and anyone
  more than 35 m from the camera. Not given to monsters, NPCs, animals or the horse.
- **Testing.** The LAN harness works: spawn at (-39, 1.6, 58) on `VerdantIsle`, controller off, `KeyMovement`
  forward toward +z. The sea starts at z = 64; swimming at z = 72. `Weather > Clear Skies` first, or the grey day
  hides the prints. The shore foam in the water shader is solid white (and stays white at night), so a white
  ring on it is invisible - the rings read over the green and blue water further out.

- **Sound.** The `WaterSplash` family (Wire Audio, and Build Footstep Effects on a rebuild) plays through
  `OneShotSound.PlayAt` where a foot goes into the sea and on each knee-deep wading step - quieter and a little
  higher for a gentle step, fuller for a run or a landing, one sound for two feet in one frame. While a body is
  wading the kit's dry footstep is turned down to nothing (the kit's own `MuteFootstepSound` is buff-driven and
  read-only, but its footstep source's volume is never set by the kit, so it is borrowed and always put back).
  Swimming keeps the `SwimStroke` sounds and makes no splash sounds. Measured: sand 0 splashes and dry steps at full
  volume; wading 4 splashes in 4 steps with the dry step silent; swimming 0 splashes and strokes back at full volume;
  standing at the very edge 1,200 frames with the swell over the boots, 0.

- **The dry footstep is played from the plant, not the kit's timer** (2026-10-06, "the footsteps seem to play
  twice for every one step"). The kit fires its footstep every `stepDelay / MoveAnimationSpeedMultiplier` seconds
  whatever the feet are doing, and the delays Wire Audio writes (0.36 s jog, 0.55 walk, 0.28 sprint) are not the
  clips' steps (0.47, 0.67, 0.33 s), so the sound ran fast and drifted across the footfalls. With `syncStepSounds`
  on (the default) `FootstepEffects` sets those three delays to never, on the running instance only, and calls the
  kit's own `PlaySound` on each step it detects - so the clips, volume, pitch spread, sfx mute and the wading mute
  are still the kit's. Swimming (no plants) and crawling (hands and knees) keep the timer, and **so does
  crouching**: that clip drives the soles 5-8 cm below the ground, so with the IK holding them up only one foot
  ever reads as landing (measured: six plants in 8 s, all the same foot), and a sound per plant would leave every
  other crouch step silent. An entity without `FootstepEffects` - every NPC, monster and animal - keeps the timer
  too. Measured live in the LAN harness, timer against sync, 5-6 s of each gait on a flat run: the timer played
  14 sounds for 10 jog steps, 14 for 10 sprint steps and dropped or doubled steps in all three; synced, every
  step interval has exactly one sound (jog 10 plants, 10 sounds; walk 9 and 9; sprint 12 and 12), on the same
  frame as the plant. Wading still mutes the dry step and plays the splash (3 plants, 3 muted calls, the kit
  source at volume 0); swimming still plays the kit's strokes. The plant is confirmed after `plantConfirm`
  (0.05 s), so the sound lands about 50 ms after first contact.
- **The jog's footfall is two contacts.** Replayed offline (IK off) on both `Jog_Fwd` clips and seen live, a jog
  gives four plants a cycle, not two: the heel touches at a centimetre while the foot is still skating back at
  2 m/s, rises to 7 cm, and the ball lands 0.2 s later. Neither contact is a still stance, so `minStepInterval`
  (0.35 s per foot) keeps the first and drops the second; it also stops the jog laying a pair of prints per step.
  The sprint has one clean contact a step. `FootstepEffects.onStep` is raised for each step kept, for a test or a
  later listener to count.

Not done on purpose: hoofprints for the horse, wet-sand sheen, and prints on grass or earth.

## License

MIT, see `LICENSE`. The demo's art is credited in `Demo/CREDITS.md` in the kit.
