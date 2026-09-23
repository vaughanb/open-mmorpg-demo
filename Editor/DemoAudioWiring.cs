using System.Collections.Generic;
using System.Linq;
using Insthync.AudioManager;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Hooks the clips under Demo/Audio up to everything in the kit that can play one.
    ///
    /// Clips are found by file name: a family is a prefix followed by a number
    /// (Footstep1.wav, Footstep2.wav ...), and every family the kit has a slot for is
    /// listed in <see cref="Hooks"/>. Drop a clip in with the right name, run
    /// `Open MMORPG > Demo > Wire Audio`, and it is live; the log then says which families
    /// are still empty. The entity, mount, model and item builders call the same
    /// functions, so a rebuilt asset comes out wired too.
    ///
    /// Where the sounds go: footsteps are the kit's footstep component, one clip set for
    /// every gait; swings are the audio slots on the sword and axe attack animations, played
    /// as the swing starts; shots and casts are the weapon item's launch clip, played the
    /// moment the missile leaves, which for a bow is well after the animation starts; hurt
    /// grunts are the demo's own component on the character's HP; deaths are the kit's death
    /// component; the island's ambience beds are built by DemoSceneBuilder.
    /// </summary>
    public static class DemoAudioWiring
    {
        public const string AudioDir = "Assets/OpenMMORPG/Demo/Audio";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";
        private const string ModelDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterModels";
        private const string HorsePath = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/Vehicles/DemoHorse.prefab";
        private const string LevelUpEffectPath = "Assets/OpenMMORPG/Demo/Prefabs/Effects/LevelUpEffect.prefab";

        private const string GameInstancePath = "Assets/OpenMMORPG/Demo/Prefabs/GameInstance.prefab";
        private const string ItemDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Items";

        public const string Footstep = "Footstep";
        public const string HorseStep = "HorseStep";
        public const string DeerStep = "DeerStep";
        public const string DogStep = "DogStep";
        public const string WolfStep = "WolfStep";
        public const string WolfGrowl = "WolfGrowl";
        public const string WolfYelp = "WolfYelp";
        public const string DeerHurt = "DeerHurt";
        public const string WeaponHit = "Hit";

        /// <summary>
        /// How loud a hurt grunt or a death cry plays, before the player's SFX setting scales it.
        ///
        /// **Not 1.** These are close-mic voice recordings against footsteps and swings that are
        /// incidental noise, so at equal gain a single hit made the character shout over the whole
        /// mix. Cutting the voices rather than lifting everything else keeps the SFX slider
        /// meaning what it says.
        /// </summary>
        private const float VoiceVolume = 0.45f;
        public const string LevelUp = "LevelUp";
        public const string SwimStroke = "SwimStroke";
        public const string SwordSwing = "SwordSwing";
        public const string PunchSwing = "PunchSwing";
        public const string ArrowFire = "ArrowFire";
        public const string SpellCast = "SpellCast";
        public const string ShieldBash = "ShieldBash";
        public const string SkillImpact = "SkillImpact";
        public const string Shout = "Shout";
        public const string ManHit = "ManHit";
        public const string WomanHit = "WomanHit";
        public const string ManDeath = "ManDeath";
        public const string WomanDeath = "WomanDeath";
        public const string AmbientNature = "AmbientNature";
        public const string OceanWaves = "OceanWaves";
        public const string CryptAmbience = "CryptAmbience";
        public const string Underwater = "Underwater";

        /// <summary>
        /// The music, named by the pieces themselves rather than by a numbered family.
        ///
        /// Every other sound in the demo is one of a set of interchangeable takes - any
        /// Footstep will do for any step - so it is found by a prefix and a number. A piece
        /// of music is not interchangeable with anything: it is written for one place, and
        /// its name is what anyone would look for. <see cref="Matches"/> already accepts a
        /// name that is the whole prefix, so these need no special handling, and adding a
        /// second piece anywhere is a matter of putting its file name in that place's set.
        /// </summary>
        public const string MenuTheme = "Theme du Chevalier";
        public const string IslandTheme = "Ruined Temple";
        public const string DungeonTheme = "Cavernous Droning";

        /// <summary>Played on a loop under the menu and the character screens.</summary>
        public static readonly string[] MenuMusic = { MenuTheme };

        /// <summary>Played now and then out on the island, with long silences between.</summary>
        public static readonly string[] IslandMusic = { IslandTheme };

        /// <summary>Played on a loop down in the crypt.</summary>
        public static readonly string[] DungeonMusic = { DungeonTheme };

        /// <summary>Every music set, for the passes that care what is music and not where it plays.</summary>
        private static readonly string[][] AllMusic = { MenuMusic, IslandMusic, DungeonMusic };

        /// <summary>
        /// How loud the music plays before the player's BGM setting scales it.
        ///
        /// The menu gets more because nothing else is making a sound there; out on the island
        /// the music has to sit under the ambience beds (0.5 and 0.8), the footsteps and the
        /// fighting rather than over them.
        /// </summary>
        public const float MenuMusicVolume = 0.6f;
        public const float IslandMusicVolume = 0.4f;

        /// <summary>
        /// The crypt's, quieter again. It plays **under a bed that never stops** - the
        /// CryptAmbience loop at 0.6 - in an enclosed space where there is nowhere for
        /// either to go, so it gets less room than the island's music, which at least has
        /// silences to arrive out of.
        /// </summary>
        public const float DungeonMusicVolume = 0.35f;

        /// <summary>
        /// One stroke per swim cycle: the Swim_Fwd_Loop clip is 1.33 s and the stroke
        /// clips run 1.2-1.5 s, so a shorter delay stacks strokes on top of each other.
        /// </summary>
        private const float SwimCycle = 1.333f;

        private struct Hook
        {
            public string Prefix;
            public string Purpose;
            /// <summary>One named file rather than a numbered family - the music. Changes what a MISSING line asks for.</summary>
            public bool Single;
        }

        /// <summary>Every clip family something in the demo can play, in the order the log reports them.</summary>
        private static readonly Hook[] Hooks =
        {
            new Hook { Prefix = Footstep, Purpose = "footsteps for every character, all gaits" },
            new Hook { Prefix = HorseStep, Purpose = "the horse's hooves" },
            new Hook { Prefix = DeerStep, Purpose = "the deer's hooves (falls back to a lightened HorseStep)" },
            new Hook { Prefix = DogStep, Purpose = "the collie's paws (falls back to a lightened Footstep)" },
            new Hook { Prefix = WolfStep, Purpose = "the wolf's paws (falls back to a lightened Footstep)" },
            new Hook { Prefix = WolfGrowl, Purpose = "the wolf's bite (falls back to the human punch swings)" },
            new Hook { Prefix = WolfYelp, Purpose = "the wolf hurt and dying (silent until provided)" },
            new Hook { Prefix = DeerHurt, Purpose = "the deer hurt and dying (silent until provided)" },
            new Hook { Prefix = SwimStroke, Purpose = "swimming strokes (silent until provided)" },
            new Hook { Prefix = SwordSwing, Purpose = "sword and axe swings" },
            new Hook { Prefix = PunchSwing, Purpose = "unarmed swings" },
            new Hook { Prefix = ArrowFire, Purpose = "bow shots, on the loose" },
            new Hook { Prefix = SpellCast, Purpose = "staff casts, on the launch, and the mage's bolt and heal" },
            new Hook { Prefix = ShieldBash, Purpose = "the warrior's Shield Bash (falls back to PunchSwing)" },
            new Hook { Prefix = SkillImpact, Purpose = "Frost Nova and Meteor landing (falls back to SpellCast)" },
            new Hook { Prefix = WeaponHit, Purpose = "any weapon landing on a target, via the default damage hit effects" },
            new Hook { Prefix = Shout, Purpose = "the warrior's Rallying Cry (silent until provided)" },
            new Hook { Prefix = LevelUp, Purpose = "the level-up chime (silent until provided)" },
            new Hook { Prefix = ManHit, Purpose = "hurt grunts, male characters" },
            new Hook { Prefix = WomanHit, Purpose = "hurt grunts, female characters" },
            new Hook { Prefix = ManDeath, Purpose = "death cries, male characters" },
            new Hook { Prefix = WomanDeath, Purpose = "death cries, female characters" },
            new Hook { Prefix = AmbientNature, Purpose = "island ambience loop" },
            new Hook { Prefix = OceanWaves, Purpose = "shore loop, fades with distance from the waterline and with height" },
            new Hook { Prefix = CryptAmbience, Purpose = "dungeon ambience loop" },
            new Hook { Prefix = Underwater, Purpose = "loop while the camera is under the sea" },
            new Hook { Prefix = MenuTheme, Purpose = "menu music, on a loop", Single = true },
            new Hook { Prefix = IslandTheme, Purpose = "island music, now and then", Single = true },
            new Hook { Prefix = DungeonTheme, Purpose = "crypt music, on a loop", Single = true },
        };

        [MenuItem("Open MMORPG/Demo/Wire Audio")]
        public static void Wire()
        {
            int characters = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { EntityDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!System.IO.Path.GetFileNameWithoutExtension(path).StartsWith("Demo"))
                    continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // The animals are in this folder too, and they are not people: wired as
                    // characters they take a bootfall and a man's hurt grunt.
                    if (!WireIfAnimal(root, System.IO.Path.GetFileNameWithoutExtension(path)))
                        WireCharacter(root, IsFemale(root));
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    characters++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            bool horse = false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HorsePath) != null)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(HorsePath);
                try
                {
                    WireHorse(root);
                    PrefabUtility.SaveAsPrefabAsset(root, HorsePath);
                    horse = true;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            int models = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ModelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var model = root.GetComponent<PlayableCharacterModel>();
                    if (model != null && WireModel(model))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        models++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            int weapons = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponItem", new[] { ItemDir }))
            {
                var item = AssetDatabase.LoadAssetAtPath<WeaponItem>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null)
                    continue;
                WireWeaponItem(item);
                weapons++;
            }

            bool levelUp = WireLevelUpEffect();
            WireHitEffect();

            int streamed = StreamMusic();
            DemoDatabaseWiring.PreloadAudio();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoAudioWiring)}] Wired audio into {characters} characters, " +
                      $"{(horse ? "the horse, " : "")}{models} models, {weapons} weapons" +
                      $"{(levelUp ? " and the level-up effect" : "")}. " +
                      "The island's ambience is built with the sea (Rebuild Sea), and its music with it; "
                      + "the menu's music with the menu stage (Build Menu Stage)."
                      + (streamed > 0 ? $" Set {streamed} music track(s) to stream." : "") + "\n" + Report());
        }

        /// <summary>
        /// The level-up chime.
        ///
        /// This one exists to **clear** as much as to set. `LevelUpEffect.prefab` came from the
        /// kit's template holding a reference to one of the kit's own demo clips, and that clip
        /// was deleted when the demo was trimmed - leaving not a null but a *missing* reference,
        /// which is worse: `GameEffect.Play` indexes the array and hands the dead object to
        /// `AudioSource.PlayClipAtPoint`, which throws `MissingReferenceException` from inside
        /// an RPC. **The whole `RpcOnLevelUp` dies with it**, so the level-up effect never plays
        /// and the client logs two errors every time a character levels (found 2026-09-16 - the
        /// first time anyone had levelled in this demo, because combat had never worked before).
        ///
        /// Writing the array unconditionally replaces the corpse with an honest empty one. Drop
        /// a `LevelUp1.wav` into the audio folder and it wires itself with no change here.
        /// </summary>
        private static bool WireLevelUpEffect()
        {
            return WireEffectSounds(LevelUpEffectPath, LevelUp);
        }

        /// <summary>
        /// The impact sound every weapon shares, written onto **whatever the game instance
        /// actually points at**.
        ///
        /// Resolved rather than hardcoded, and that is not fussiness: the obvious guess is
        /// `Effects/HitEffect.prefab`, which exists, carries a `GameEffect` and a particle system,
        /// and is **not what the demo uses** - `defaultDamageHitEffects` points at
        /// `Effects/Skills/FX_HitPhysical`, written there by `DemoSkillEffectBuilder`. Wiring the
        /// obvious one produced a silent, correct-looking prefab that nothing plays. Reading the
        /// pointer costs one prefab load and cannot be wrong.
        ///
        /// That list is the demo's entire hit feedback: the kit falls back to it for any damage
        /// whose element names no effect of its own, and the demo defines no `DamageElement` at
        /// all, so every weapon against every target comes through here.
        /// </summary>
        private static bool WireHitEffect()
        {
            var instance = AssetDatabase.LoadAssetAtPath<GameObject>(GameInstancePath);
            GameInstance gameInstance = instance == null ? null : instance.GetComponentInChildren<GameInstance>(true);
            if (gameInstance == null)
            {
                Debug.LogWarning($"[{nameof(DemoAudioWiring)}] No GameInstance at \"{GameInstancePath}\" to read the hit effects from.");
                return false;
            }
            var serialized = new SerializedObject(gameInstance);
            SerializedProperty effects = serialized.FindProperty("defaultDamageHitEffects");
            if (effects == null || effects.arraySize == 0)
            {
                Debug.LogWarning($"[{nameof(DemoAudioWiring)}] The game instance names no default damage hit effects, " +
                                 "so a weapon landing on a target has nothing to play a sound through.");
                return false;
            }
            bool wrote = false;
            for (int i = 0; i < effects.arraySize; ++i)
            {
                Object effect = effects.GetArrayElementAtIndex(i).objectReferenceValue;
                if (effect == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(effect);
                if (!string.IsNullOrEmpty(path))
                    wrote |= WireEffectSounds(path, WeaponHit);
            }
            return wrote;
        }

        /// <summary>
        /// Writes a clip family onto every <see cref="GameEffect"/> in an effect prefab.
        ///
        /// The array is written **unconditionally**, empty family or not, because that is what
        /// clears a dead reference as well as setting a live one - and a dead `AudioClip` here is
        /// not merely silent: `GameEffect.Play` indexes the array and hands the destroyed object
        /// straight to `PlayOneShot`.
        /// </summary>
        private static bool WireEffectSounds(string effectPath, string family)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(effectPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[{nameof(DemoAudioWiring)}] No effect prefab at \"{effectPath}\".");
                return false;
            }
            AudioClip[] clips = Clips(family);
            GameObject root = PrefabUtility.LoadPrefabContents(effectPath);
            try
            {
                bool wrote = false;
                foreach (GameEffect effect in root.GetComponentsInChildren<GameEffect>(true))
                {
                    var serialized = new SerializedObject(effect);
                    SerializedProperty array = serialized.FindProperty("randomSoundEffects");
                    if (array == null)
                        continue;
                    array.arraySize = clips.Length;
                    for (int i = 0; i < clips.Length; ++i)
                        array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    wrote = true;
                }
                if (wrote)
                    PrefabUtility.SaveAsPrefabAsset(root, effectPath);
                return wrote;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Adds the gate that keeps the death cry quiet until the character's health has synced.
        ///
        /// Paired with every `CharacterDeathSoundComponent` this builder adds, because the fault is
        /// in that component rather than in any one entity: it assumes a character starts alive,
        /// and a just-spawned one reads as dead until the server's first sync. See
        /// <see cref="MultiplayerARPG.Demo.DemoDeathSoundGate"/>.
        ///
        /// Removed again wherever the death component is removed - the two go together, and a gate
        /// left behind on an entity with no death sound is a component that does nothing but look
        /// like it might.
        /// </summary>
        private static void GateDeathSound(GameObject root)
        {
            GetOrAdd<MultiplayerARPG.Demo.DemoDeathSoundGate>(root);
        }

        private static void UngateDeathSound(GameObject root)
        {
            var gate = root.GetComponent<MultiplayerARPG.Demo.DemoDeathSoundGate>();
            if (gate != null)
                Object.DestroyImmediate(gate);
        }

        /// <summary>The clips of one family, in name order. Empty when none are provided.</summary>
        public static AudioClip[] Clips(string prefix)
        {
            var found = new List<KeyValuePair<string, AudioClip>>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!Matches(name, prefix))
                    continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                    found.Add(new KeyValuePair<string, AudioClip>(name, clip));
            }
            return found.OrderBy(f => f.Key, System.StringComparer.Ordinal).Select(f => f.Value).ToArray();
        }

        /// <summary>
        /// The tracks of a music set, in the order the set names them, skipping any that
        /// have not been provided. A set with no files at all builds no player at all, so
        /// the scenes are silent rather than carrying a component that can never sound.
        /// </summary>
        public static AudioClip[] MusicClips(string[] families)
        {
            var found = new List<AudioClip>();
            foreach (string family in families)
                found.AddRange(Clips(family));
            return found.ToArray();
        }

        /// <summary>Whether a clip file name is one of the music tracks.</summary>
        public static bool IsMusic(string name)
        {
            foreach (string[] set in AllMusic)
            {
                foreach (string family in set)
                {
                    if (Matches(name, family))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Sets the music to **stream** from disk, and keeps it out of the preload pass.
        ///
        /// Every other clip in the demo is a second of a footfall and is loaded whole, up
        /// front, because the first play of a clip that is not resident stalls the main
        /// thread. Music is the opposite case: these two tracks are 2:27 and 3:35, which
        /// decompressed is about 64 MB of PCM held for the whole session - more than the
        /// demo's entire art budget - to save a stall nobody would notice under a piece of
        /// music that fades in over two seconds.
        ///
        /// So they are the one exception: `Streaming` load type, decoded a buffer at a time
        /// as they play, never preloaded. <see cref="DemoDatabaseWiring.PreloadAudio"/> asks
        /// <see cref="IsMusic"/> and leaves them alone, or it would undo this on every run.
        /// </summary>
        /// <summary>
        /// The Vorbis quality the music is re-encoded at.
        ///
        /// The rest of the folder is at 1.0, which is right for a half-second footfall where
        /// the file is small whatever you do to it. A three-and-a-half minute track at 1.0 is
        /// about 13 MB in the build; at 0.5 - roughly 128 kbit/s - it is a third of that, and
        /// the two tracks together stay inside what the demo can afford to ship. The sources
        /// are 190 kbit/s mp3, so this is a second encode either way and the ceiling is not
        /// the setting.
        /// </summary>
        private const float MusicQuality = 0.5f;

        private static int StreamMusic()
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsMusic(System.IO.Path.GetFileNameWithoutExtension(path)))
                    continue;
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                    continue;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                if (settings.loadType == AudioClipLoadType.Streaming && !settings.preloadAudioData &&
                    importer.loadInBackground && Mathf.Approximately(settings.quality, MusicQuality))
                    continue;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.preloadAudioData = false;
                settings.quality = MusicQuality;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = true;
                importer.SaveAndReimport();
                ++changed;
            }
            return changed;
        }

        /// <summary>
        /// The clips a skill plays, in three descending preferences: **a family named
        /// after the skill itself**, then the generic family the skill asks for, then
        /// whatever stands in for that family when it is empty.
        ///
        /// The skill's own name comes first so that a spell can be given its own voice by
        /// dropping `ArcaneBolt.wav` into the audio folder - no code, no rewiring, and no
        /// decision about it taken in advance. Every skill still declares a generic family
        /// as well, so the ones nobody has recorded yet are not silent; `Mend` plays the
        /// common cast because there is no `Mend.wav`, and would stop the moment there is.
        ///
        /// The two stand-in families work the same way one level down: a skill set built
        /// before anyone records a shield clunk still makes a noise rather than none, and
        /// the moment a ShieldBash1.wav appears it takes over.
        ///
        /// Name matching is <see cref="Matches"/>, so "Meteor" takes Meteor.wav and
        /// Meteor2.wav but not MeteorImpact.wav.
        /// </summary>
        public static AudioClip[] SkillClips(string skillName, string prefix)
        {
            if (!string.IsNullOrEmpty(skillName))
            {
                AudioClip[] own = Clips(skillName);
                if (own.Length > 0)
                    return own;
            }
            if (string.IsNullOrEmpty(prefix))
                return new AudioClip[0];
            AudioClip[] clips = Clips(prefix);
            if (clips.Length > 0)
                return clips;
            switch (prefix)
            {
                case ShieldBash: return Clips(PunchSwing);
                case SkillImpact: return Clips(SpellCast);
                default: return clips;
            }
        }

        /// <summary>"Footstep" matches Footstep.wav and Footstep12.wav, not FootstepWet.wav.</summary>
        private static bool Matches(string name, string prefix)
        {
            if (!name.StartsWith(prefix))
                return false;
            return name.Length == prefix.Length || char.IsDigit(name[prefix.Length]);
        }

        /// <summary>
        /// Footsteps, a hurt grunt and, when clips exist, a death cry on a character entity.
        /// The model's gender picks the voice. Without a family's clips the slot is left
        /// silent rather than empty: the kit's footstep component plays a null clip as an
        /// error every step, so a silent set is one that never comes due.
        /// </summary>
        internal static void WireCharacter(GameObject root, bool female)
        {
            AudioClip[] steps = Clips(Footstep);
            var footstep = GetOrAdd<CharacterFootstepSoundComponent>(root);
            footstep.audioSource = Source(root, "_FootstepAudioSource");
            footstep.settingType = AudioComponentSettingType.Sfx;
            footstep.moveFootstepSettings = Steps(steps, 0.36f);
            footstep.walkFootstepSettings = Steps(steps, 0.55f);
            footstep.sprintFootstepSettings = Steps(steps, 0.28f);
            footstep.crouchFootstepSettings = Steps(steps, 0.6f);
            footstep.crawlFootstepSettings = Steps(steps, 1f);
            footstep.swimFootstepSettings = Steps(Clips(SwimStroke), SwimCycle);
            EditorUtility.SetDirty(footstep);

            var hurt = GetOrAdd<MultiplayerARPG.Demo.DemoHurtSoundComponent>(root);
            hurt.clips = Clips(female ? WomanHit : ManHit);
            hurt.volume = VoiceVolume;
            EditorUtility.SetDirty(hurt);

            AudioClip[] death = Clips(female ? WomanDeath : ManDeath);
            var deathComponent = root.GetComponent<CharacterDeathSoundComponent>();
            // Only on something that can actually die.
            //
            // `CharacterDeathSoundComponent` is a `BaseGameEntityComponent<BaseCharacterEntity>`,
            // and an `NpcEntity` is **not** a `BaseCharacterEntity` - NPCs are not characters
            // in the kit's hierarchy. Put it on one anyway and `Entity` resolves to null, so
            // the very first line of its `Start` - `if (!Entity.IsClient)` - throws, the NPC's
            // initialisation dies with it, and **the NPC never spawns**. Six NPCs, six
            // NullReferenceExceptions a map start, and an empty village.
            //
            // This bit the demo for real: the component is only added when `ManDeath*` and
            // `WomanDeath*` clips exist, so it appeared the day those clips were provided
            // (2026-09-15) and took every villager, guard, innkeeper, keeper, elder and
            // patrol with it.
            bool canDie = root.GetComponent<BaseCharacterEntity>() != null;
            if (!canDie && deathComponent != null)
            {
                UngateDeathSound(root);
                Object.DestroyImmediate(deathComponent);
                deathComponent = null;
            }
            if (death.Length > 0 && canDie)
            {
                deathComponent = GetOrAdd<CharacterDeathSoundComponent>(root);
                deathComponent.audioSource = Source(root, "_DeathAudioSource");
                deathComponent.settingType = AudioComponentSettingType.Sfx;
                deathComponent.soundData = new CharacterDeathSoundComponent.DeathSoundData { randomAudioClips = death };
                EditorUtility.SetDirty(deathComponent);
                GateDeathSound(root);
            }
            else if (deathComponent != null)
            {
                UngateDeathSound(root);
                Object.DestroyImmediate(deathComponent);
            }
        }

        /// <summary>
        /// The demo's four-legged entities and the gait clips their step cadence comes from.
        /// Kept here rather than in the wildlife builder so that <see cref="Wire"/> can tell
        /// an animal from a person while walking the entity folder.
        ///
        /// The lengths are the clips' own, read off the FBXs (30 fps): deer walk 1.167s and
        /// gallop 0.533s, collie walk 1.067s and gallop 0.567s.
        /// </summary>
        private struct Animal
        {
            public string Prefab, Family, Fallback;
            public float WalkClip, GallopClip, Volume, Pitch;

            /// <summary>
            /// The model prefab, and the families for its bite and its voice. Empty means the
            /// animal has none, which is not the same as having none *yet*: an empty family is
            /// never looked up, where a named one with no clips falls back.
            ///
            /// <c>Death</c> is separate from <c>Hurt</c> but **falls back to it**, so an animal
            /// with only hurt clips still cries out when it dies rather than dropping in silence,
            /// and dropping a `...Death1.wav` in later wins with no change here.
            /// </summary>
            public string Model, Attack, Hurt, Death;
        }

        private static readonly Animal[] Animals =
        {
            // 0.90m at the shoulder against the horse's 1.55m: up a third in pitch, and
            // well down in weight.
            // No `Attack`: the deer is a NoHarm entity and never bites anything, so the punch
            // clips its model still carries are on an animation that never plays.
            new Animal { Prefab = "DemoDeer", Family = DeerStep, Fallback = HorseStep,
                WalkClip = 1.167f, GallopClip = 0.533f, Volume = 0.55f, Pitch = 1.30f,
                Hurt = DeerHurt },
            // A paw is softer and lighter than a boot, and the dog is knee-high.
            new Animal { Prefab = "DemoVillageDog", Family = DogStep, Fallback = Footstep,
                WalkClip = 1.067f, GallopClip = 0.567f, Volume = 0.38f, Pitch = 1.45f },
            // The wolf runs the collie's rig and so the collie's gait clips, but it stands
            // a full metre to the dog's 0.79 - so the same paw, carried heavier and pitched
            // down towards a bootfall rather than up away from one.
            // The only animal in the demo with a voice of its own. The growl goes on its bite
            // and the yelps on being hurt and on dying - a wolf's death cry *is* a yelp, so one
            // family serves both and the kit picks at random from it either way.
            new Animal { Prefab = "DemoWolf", Family = WolfStep, Fallback = Footstep,
                WalkClip = 1.067f, GallopClip = 0.567f, Volume = 0.46f, Pitch = 1.12f,
                Model = "WolfModel", Attack = WolfGrowl, Hurt = WolfYelp },
        };

        /// <summary>
        /// Wires an animal's footsteps by prefab name, for the wildlife builder to call as it
        /// assembles one. Silently does nothing for a name that is not an animal's.
        /// </summary>
        internal static void WireAnimalEntity(GameObject root, string prefabName)
        {
            WireIfAnimal(root, prefabName);
        }

        /// <summary>Wires the prefab as an animal and returns true, or leaves it and returns false.</summary>
        private static bool WireIfAnimal(GameObject root, string prefabName)
        {
            foreach (Animal animal in Animals)
            {
                if (animal.Prefab != prefabName)
                    continue;
                WireAnimal(root, animal.Family, animal.Fallback,
                           animal.WalkClip, animal.GallopClip, animal.Volume, animal.Pitch,
                           animal.Hurt, animal.Death);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The deer's hooves and the collie's paws.
        ///
        /// Neither has a clip family of its own yet, so each borrows the nearest one it has
        /// and is **lightened to suit the animal**: the deer takes the horse's hooves, the
        /// collie the human bootfall, both pitched up and turned down. Pitch stands in for
        /// size here — a smaller body rings higher — and a deer is 0.90m at the shoulder
        /// against the horse's 1.55m. The shift is deliberately short of that whole ratio,
        /// which would sound like a sped-up tape rather than a lighter animal.
        ///
        /// Drop `DeerStep1.wav` or `DogStep1.wav` into the audio folder and that family wins
        /// instead, at its own weight, with no change needed here.
        ///
        /// Cadence is measured off the gait clips rather than guessed. A quadruped's walk is
        /// four evenly spaced footfalls per cycle, so the walk delay is the clip's length
        /// over four. A gallop's four beats come in a burst followed by a suspension, which
        /// an even timer cannot reproduce at all — spaced truly it buzzes — so it runs at
        /// half that rate and reads as a rhythm.
        ///
        /// An animal is not a person: no hurt grunt and no death cry, so neither of those
        /// components is added. That also keeps <see cref="Wire"/> from handing the deer a
        /// man's voice, which is what it did before these two were split out.
        /// </summary>
        internal static void WireAnimal(GameObject root, string family, string fallbackFamily,
                                        float walkClipLength, float gallopClipLength,
                                        float volume, float pitch,
                                        string hurtFamily = null, string deathFamily = null)
        {
            AudioClip[] steps = Clips(family);
            bool own = steps.Length > 0;
            if (!own)
            {
                steps = Clips(fallbackFamily);
                // Borrowed clips get the lightening; its own would already be the right animal.
            }
            else
            {
                volume = 1f;
                pitch = 1f;
            }

            float walk = walkClipLength / 4f;
            float gallop = gallopClipLength / 2f;

            var footstep = GetOrAdd<CharacterFootstepSoundComponent>(root);
            footstep.audioSource = Source(root, "_FootstepAudioSource");
            footstep.settingType = AudioComponentSettingType.Sfx;
            footstep.walkFootstepSettings = Steps(steps, walk, volume, pitch);
            footstep.moveFootstepSettings = Steps(steps, gallop, volume, pitch);
            footstep.sprintFootstepSettings = Steps(steps, gallop * 0.85f, volume, pitch);
            footstep.crouchFootstepSettings = Steps(steps, walk, volume, pitch);
            footstep.crawlFootstepSettings = Steps(steps, walk, volume, pitch);
            // Neither of these swims anywhere in the demo, and the stroke clips are a
            // person's arms through water.
            footstep.swimFootstepSettings = Steps(new AudioClip[0], SwimCycle);
            EditorUtility.SetDirty(footstep);

            WireAnimalVoice(root, hurtFamily, deathFamily);
        }

        /// <summary>
        /// A hurt sound and a death cry for an animal that has clips of its own.
        ///
        /// This used to be flatly refused - "an animal is not a person: no hurt grunt and no
        /// death cry" - and that was right while the only thing on offer was a man's voice, which
        /// is what the deer was given before the two were split. It is wrong once the animal has
        /// its **own** family. So the rule is not "animals are silent", it is **"nothing borrows a
        /// voice"**: an animal with no `Voice` family still gets neither component, and the deer
        /// and the collie are unchanged.
        ///
        /// One family covers hurt and death because a wolf's death cry is a yelp like any other,
        /// and the kit picks at random from the set in both cases.
        ///
        /// The `BaseCharacterEntity` guard is not paranoia - see <see cref="WireCharacter"/>:
        /// `CharacterDeathSoundComponent` resolves `Entity` to null on anything that is not one
        /// and throws in its first line of `Start`, taking the entity's whole initialisation with
        /// it. A monster **is** a character in the kit's hierarchy, so the wolf passes; the check
        /// costs nothing and stops the next animal from being an NPC.
        /// </summary>
        private static void WireAnimalVoice(GameObject root, string hurtFamily, string deathFamily)
        {
            AudioClip[] hurtClips = string.IsNullOrEmpty(hurtFamily) ? new AudioClip[0] : Clips(hurtFamily);
            AudioClip[] deathClips = string.IsNullOrEmpty(deathFamily) ? new AudioClip[0] : Clips(deathFamily);
            // An animal with only hurt clips still cries out as it dies. Silence there reads as a
            // bug, and a second bleat is much closer to right than nothing.
            if (deathClips.Length == 0)
                deathClips = hurtClips;

            var hurt = root.GetComponent<MultiplayerARPG.Demo.DemoHurtSoundComponent>();
            if (hurtClips.Length > 0)
            {
                hurt = GetOrAdd<MultiplayerARPG.Demo.DemoHurtSoundComponent>(root);
                hurt.clips = hurtClips;
                hurt.volume = VoiceVolume;
                EditorUtility.SetDirty(hurt);
            }
            else if (hurt != null)
            {
                Object.DestroyImmediate(hurt);
            }

            var death = root.GetComponent<CharacterDeathSoundComponent>();
            bool canDie = root.GetComponent<BaseCharacterEntity>() != null;
            if (deathClips.Length > 0 && canDie)
            {
                death = GetOrAdd<CharacterDeathSoundComponent>(root);
                death.audioSource = Source(root, "_DeathAudioSource");
                death.settingType = AudioComponentSettingType.Sfx;
                death.soundData = new CharacterDeathSoundComponent.DeathSoundData { randomAudioClips = deathClips };
                EditorUtility.SetDirty(death);
                GateDeathSound(root);
            }
            else if (death != null)
            {
                UngateDeathSound(root);
                Object.DestroyImmediate(death);
            }
        }

        /// <summary>Hooves on the horse; a mount cannot be hurt, so nothing else.</summary>
        internal static void WireHorse(GameObject root)
        {
            AudioClip[] hooves = Clips(HorseStep);
            var footstep = GetOrAdd<CharacterFootstepSoundComponent>(root);
            footstep.audioSource = Source(root, "_FootstepAudioSource");
            footstep.settingType = AudioComponentSettingType.Sfx;
            footstep.moveFootstepSettings = Steps(hooves, 0.3f);
            footstep.walkFootstepSettings = Steps(hooves, 0.5f);
            footstep.sprintFootstepSettings = Steps(hooves, 0.24f);
            footstep.crouchFootstepSettings = Steps(hooves, 0.5f);
            footstep.crawlFootstepSettings = Steps(hooves, 0.5f);
            footstep.swimFootstepSettings = Steps(Clips(SwimStroke), SwimCycle);
            EditorUtility.SetDirty(footstep);
        }

        /// <summary>
        /// Swing clips onto the attack animations of a character model: the sword and axe
        /// sets get the sword swings, the unarmed set the punch swings. Ranged weapons play
        /// nothing here - their sound is on the item, at the launch.
        /// </summary>
        internal static bool WireModel(PlayableCharacterModel model)
        {
            AudioClip[] swings = Clips(SwordSwing);
            // **An animal's bite is not a punch.** The default attack animations are the
            // fist ones for a person, and the wolf was going through this pass with everything
            // else - so it bit with five human knuckle impacts. An animal with its own attack
            // family uses that instead; one without still falls back to the punches, which is
            // better than the silence it would otherwise have.
            AudioClip[] punches = Clips(PunchSwing);
            AudioClip[] bite = AnimalAttackClips(model.name);
            AudioClip[] unarmed = bite.Length > 0 ? bite : punches;
            var serialized = new SerializedObject(model);
            SetAttackClips(serialized.FindProperty("defaultAnimations.rightHandAttackAnimations"), unarmed);
            SetAttackClips(serialized.FindProperty("defaultAnimations.leftHandAttackAnimations"), unarmed);
            SerializedProperty weapons = serialized.FindProperty("weaponAnimations");
            for (int i = 0; weapons != null && i < weapons.arraySize; i++)
            {
                SerializedProperty element = weapons.GetArrayElementAtIndex(i);
                Object type = element.FindPropertyRelative("weaponType").objectReferenceValue;
                if (type == null || (type.name != "Sword" && type.name != "Axe"))
                    continue;
                SetAttackClips(element.FindPropertyRelative("rightHandAttackAnimations"), swings);
                SetAttackClips(element.FindPropertyRelative("leftHandAttackAnimations"), swings);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(model);
            return true;
        }

        /// <summary>
        /// The launch clip of a ranged weapon: arrows for bows, casts for staffs. The kit
        /// picks one with Random.Range(0, Length - 1), whose upper bound is exclusive, so
        /// the last entry can never play; it is repeated so every clip gets its turn.
        /// </summary>
        internal static void WireWeaponItem(WeaponItem item)
        {
            var serialized = new SerializedObject(item);
            Object type = serialized.FindProperty("weaponType").objectReferenceValue;
            string prefix = type == null ? null : type.name == "Bow" ? ArrowFire : type.name == "Staff" ? SpellCast : null;
            AudioClip[] clips = prefix == null ? new AudioClip[0] : Clips(prefix);
            SerializedProperty settings = serialized.FindProperty("launchClipSettings");
            if (settings == null)
                return;
            int count = clips.Length > 1 ? clips.Length + 1 : clips.Length;
            settings.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty element = settings.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("audioClip").objectReferenceValue = clips[Mathf.Min(i, clips.Length - 1)];
                element.FindPropertyRelative("minRandomVolume").floatValue = 0.85f;
                element.FindPropertyRelative("maxRandomVolume").floatValue = 1f;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
        }

        /// <summary>
        /// The attack clips for a **model** prefab that belongs to an animal, or none.
        ///
        /// Matched on the model's name rather than the entity's, because this pass walks the model
        /// folder and never sees the entity that will wear it.
        /// </summary>
        private static AudioClip[] AnimalAttackClips(string modelName)
        {
            foreach (Animal animal in Animals)
            {
                if (animal.Model != modelName || string.IsNullOrEmpty(animal.Attack))
                    continue;
                return Clips(animal.Attack);
            }
            return new AudioClip[0];
        }

        private static void SetAttackClips(SerializedProperty animations, AudioClip[] clips)
        {
            if (animations == null)
                return;
            for (int i = 0; i < animations.arraySize; i++)
            {
                SerializedProperty audio = animations.GetArrayElementAtIndex(i).FindPropertyRelative("audioClips");
                if (audio == null)
                    continue;
                audio.arraySize = clips.Length;
                for (int k = 0; k < clips.Length; k++)
                    audio.GetArrayElementAtIndex(k).objectReferenceValue = clips[k];
            }
        }

        /// <summary>
        /// As <see cref="Steps(AudioClip[], float)"/>, but scaled for an animal wearing a
        /// clip family borrowed from something a different size. `volume` and `pitch`
        /// multiply the usual random spread rather than replacing it, so the variation that
        /// stops a run of steps sounding mechanical survives.
        /// </summary>
        private static FootstepSettings Steps(AudioClip[] clips, float stepDelay, float volume, float pitch)
        {
            FootstepSettings settings = Steps(clips, stepDelay);
            if (clips.Length == 0)
                return settings;
            settings.randomVolumeMin = Mathf.Clamp01(settings.randomVolumeMin * volume);
            settings.randomVolumeMax = Mathf.Clamp01(settings.randomVolumeMax * volume);
            settings.randomPitchMin = Mathf.Clamp(settings.randomPitchMin * pitch, -3f, 3f);
            settings.randomPitchMax = Mathf.Clamp(settings.randomPitchMax * pitch, -3f, 3f);
            return settings;
        }

        private static FootstepSettings Steps(AudioClip[] clips, float stepDelay)
        {
            if (clips.Length == 0)
            {
                // Never comes due: the component divides this by the animation speed and
                // waits for the counter to reach it.
                return new FootstepSettings
                {
                    soundData = new FootstepSoundData { randomAudioClips = new AudioClip[0] },
                    stepDelay = float.MaxValue,
                };
            }
            return new FootstepSettings
            {
                soundData = new FootstepSoundData { randomAudioClips = clips },
                stepDelay = stepDelay,
                stepThreshold = 0.1f,
                randomVolumeMin = 0.7f,
                randomVolumeMax = 1f,
                randomPitchMin = 0.9f,
                randomPitchMax = 1.1f,
            };
        }

        /// <summary>
        /// A 3D audio source child for one of the kit's sound components. The kit makes
        /// one in Start when the field is empty, but its managed update can run before
        /// Start on an entity spawned mid-frame and then trips over the empty field, so
        /// the source is made here, in the prefab.
        /// </summary>
        private static AudioSource Source(GameObject root, string name)
        {
            Transform existing = root.transform.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null)
                go.transform.SetParent(root.transform, false);
            AudioSource source = go.GetComponent<AudioSource>();
            if (source == null)
                source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            return source;
        }

        private static T GetOrAdd<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            return component != null ? component : root.AddComponent<T>();
        }

        /// <summary>The model under an entity names its gender; the entity's own name is the fallback.</summary>
        private static bool IsFemale(GameObject root)
        {
            string name = root.name;
            Transform model = root.transform.Find("Model");
            if (model != null)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
                if (source != null)
                    name = source.name;
            }
            return name.Contains("Female") || root.name.Contains("Innkeeper");
        }

        private static string Report()
        {
            var report = new System.Text.StringBuilder();
            foreach (Hook hook in Hooks)
            {
                int count = Clips(hook.Prefix).Length;
                report.Append(count > 0 ? $"  {hook.Prefix}: {count} clip(s) - {hook.Purpose}\n"
                                        : $"  {hook.Prefix}: MISSING - {hook.Purpose}; add {hook.Prefix}{(hook.Single ? ".mp3" : "1.wav ...")}\n");
            }
            return report.ToString();
        }
    }
}
