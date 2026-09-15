# Open MMORPG Demo Builder

The editor tools that generate the Open MMORPG demo island: `Assets/OpenMMORPG/Demo`
in the [OpenMMORPG](https://github.com/open-mmorpg/OpenMMORPG) kit repository.

The demo is generated, not hand-placed. These menu items under **Open MMORPG > Demo**
build it from the source art libraries in `Assets/Plugins` (the Quaternius Universal
Base Characters, Modular Character Outfits, Fantasy Props, Nature and Village packs,
all CC0), so they need those libraries in the project and are of no use to a kit user
who only has the finished demo. That is why they live here, outside the kit folder,
the same way addons install to `Assets/OpenMMORPG_addons`.

Clone this repository to `Assets/OpenMMORPG_DemoBuilder` in the Unity project that
holds the kit.

## Rebuilding the demo

Each step consumes the previous one's output, so run them in this order:

1. `Quaternius > Split Base Bodies Into Equipment Slots`
2. `Quaternius > Fix Foliage Materials` (one-off)
3. `Quaternius > Build Props Materials`, then `Quaternius > Remap Props Materials` (one-off)
4. `Open MMORPG > Demo > Build Weapon Prefabs`
5. `Open MMORPG > Demo > Build Terrain Tree Prefabs`
6. `Open MMORPG > Demo > Build Items`, then `Build Harvestables`
7. `Open MMORPG > Demo > Build Character Models`
8. `Open MMORPG > Demo > Build Character Entities`, then `Build Mounts`
9. `Open MMORPG > Demo > Build Island Terrain`
10. `Open MMORPG > Demo > Build Island Scene` (rewrites `DemoMap.unity`; the `Npcs` root is kept)
11. `Open MMORPG > Demo > Build Dungeon Scene` (rewrites `DemoDungeon.unity`, the crypt under
    the hills; also writes its map info and the `DungeonGate` warp entity)
12. `Open MMORPG > Demo > Build NPCs And Quests`
13. `Open MMORPG > Demo > Wire Game Database` (also opens the way between the island and the
    crypt: the warp portal database, the map spawn list and the build settings)
14. `Open MMORPG > Demo > Build Player Controller`
14b. `Open MMORPG > Demo > Wire Audio` — hooks the clips under `Demo/Audio` up by file-name
    family (Footstep*, SwordSwing*, ArrowFire*, ManHit*/WomanHit*, ...) to the character
    entities, the horse, the character models and the weapon items, and logs the families
    still missing. The entity, mount, model and item builders call the same code, so this
    only needs rerunning when clips are added. The island's ambience beds are built with
    the sea (`Rebuild Sea`).
15. `Open MMORPG > Demo > Collect Demo Art` — last, and after any rebuild: copies every
    library asset the demo still references into `Demo/Art` (textures resampled, the
    animation clips extracted one by one) and rewrites the references, so the demo
    carries everything it uses. `Verify Demo Is Self-Contained` reports anything left
    outside and any reference that points at nothing.

Audits that measure the result and report every fault: `Audit House Interiors`,
`Audit Scene Placement`, `Audit Outfits`.

The `Quaternius >` items are the library's own tools and live beside the library in
`Assets/Plugins/Quaternius/Editor`.

## License

MIT, see `LICENSE`. The demo's art is credited in `Demo/CREDITS.md` in the kit.
