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
5. `Open MMORPG > Demo > Build Terrain Tree Prefabs`
6. `Open MMORPG > Demo > Build Items`, then `Build Harvestables`, then `Build Skill
   Effects`, then `Build Skills` (the skills point at the weapon types and missiles the
   item builder writes and at the effect prefabs; they draw their own icons and the ground
   markers the area ones spawn)
7. `Open MMORPG > Demo > Build Character Models` (reads the skill list to give each skill
   its own clip; without `Build Skills` first, every skill animates as a spell cast)
8. `Open MMORPG > Demo > Build Character Entities`, then `Build Mounts`
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
    only if missing — use `Regenerate Settled Areas` to rebuild those)
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
    transparent surface over a sand seabed, which reads as more beach.
13d. `Open MMORPG > Demo > Build Menu Stage` - the home menu: a stone terrace of Quaternius
    pieces with the painted valley behind it, warm lighting, the `OPEN MMORPG` title, and a
    repaint of the menu panels. Rewrites the `MenuStage` root in `01Home.unity` and edits
    `CanvasGlobal`/`CanvasHome`. Independent of the rest; re-run after changing the
    backdrop art.
14. `Open MMORPG > Demo > Build Player Controller`
14a. `Open MMORPG > Demo > Build Feedback Effects` - the in-world feedback the template left
    broken: animator controllers for the six damage/heal numbers (they sat at alpha 0 with no
    controller, so none ever showed) and for the level-up flourish; the click-to-move ring
    (`TargetObject.prefab`, a legacy Projector URP never drew), laid to the slope by
    `DemoGroundMarker`; and the safe-area and vending signs, hung over the nameplate by
    `DemoNameplateSign` instead of lying at the feet. Edits those prefabs in place and points
    the demo controller at the ring, so it can run before or after step 14.
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

**The demo's animation is CC0 only.** Its clips come from Quaternius's two Universal
Animation Libraries, both CC0, which sit outside the kit in `Assets/Plugins/Quaternius/Animations`
(UAL1 and `UAL2/`); `Collect Demo Art` extracts just the clips the demo plays into
`Demo/Animations`. Anything dropped into that folder by hand ships with the kit, so it must
be CC0 too.

`Open MMORPG > Demo > Refresh Skill Animations` re-applies each skill's clip, trigger and
speed from `DemoSkillBuilder` to the character models, and touches nothing else. Use it
after tuning a skill's animation instead of `Build Character Models`, which would need the
whole entity chain run again after it. Then `Collect Demo Art`, for any newly used clip.

`Open MMORPG > Demo > Import Mixamo Animations` is **for local use only**. Mixamo lets its
`Open MMORPG > Demo > Refresh Weapon Attacks` is the same for the basic attack: it replaces
only each melee and staff set's attack clips on the models (bows are skipped - their attack
depends on whether that character can charge a shot).

**The mage's staff is a melee weapon, and Intelligence is spell power** (2026-09-23). The staff
swings (`Sword_Heavy_A`) instead of firing the Arcane Bolt missile as a free, uncooled basic
attack; the spells carry the damage. `Build Progression` owns the attribute side, because the
attributes do not exist until it runs: it gives Arcane Bolt, Frost Nova and Meteor damage per
point of Intelligence (`SpellPower`) and the staffs their Intelligence, plus a level of Arcane
Bolt on the Elder Staff (`Foci`). So after `Build Items` or `Build Skills`, run `Build Combat Data`
(elements live on the skills) and `Build Progression` again.
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

## License

MIT, see `LICENSE`. The demo's art is credited in `Demo/CREDITS.md` in the kit.
