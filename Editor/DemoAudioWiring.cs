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
        private const string ItemDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Items";

        public const string Footstep = "Footstep";
        public const string HorseStep = "HorseStep";
        public const string SwimStroke = "SwimStroke";
        public const string SwordSwing = "SwordSwing";
        public const string PunchSwing = "PunchSwing";
        public const string ArrowFire = "ArrowFire";
        public const string SpellCast = "SpellCast";
        public const string ManHit = "ManHit";
        public const string WomanHit = "WomanHit";
        public const string ManDeath = "ManDeath";
        public const string WomanDeath = "WomanDeath";
        public const string AmbientNature = "AmbientNature";
        public const string OceanWaves = "OceanWaves";
        public const string CryptAmbience = "CryptAmbience";

        /// <summary>
        /// One stroke per swim cycle: the Swim_Fwd_Loop clip is 1.33 s and the stroke
        /// clips run 1.2-1.5 s, so a shorter delay stacks strokes on top of each other.
        /// </summary>
        private const float SwimCycle = 1.333f;

        private struct Hook
        {
            public string Prefix;
            public string Purpose;
        }

        /// <summary>Every clip family something in the demo can play, in the order the log reports them.</summary>
        private static readonly Hook[] Hooks =
        {
            new Hook { Prefix = Footstep, Purpose = "footsteps for every character, all gaits" },
            new Hook { Prefix = HorseStep, Purpose = "the horse's hooves" },
            new Hook { Prefix = SwimStroke, Purpose = "swimming strokes (silent until provided)" },
            new Hook { Prefix = SwordSwing, Purpose = "sword and axe swings" },
            new Hook { Prefix = PunchSwing, Purpose = "unarmed swings" },
            new Hook { Prefix = ArrowFire, Purpose = "bow shots, on the loose" },
            new Hook { Prefix = SpellCast, Purpose = "staff casts, on the launch" },
            new Hook { Prefix = ManHit, Purpose = "hurt grunts, male characters" },
            new Hook { Prefix = WomanHit, Purpose = "hurt grunts, female characters" },
            new Hook { Prefix = ManDeath, Purpose = "death cries, male characters" },
            new Hook { Prefix = WomanDeath, Purpose = "death cries, female characters" },
            new Hook { Prefix = AmbientNature, Purpose = "island ambience loop" },
            new Hook { Prefix = OceanWaves, Purpose = "shore loop, fades with height" },
            new Hook { Prefix = CryptAmbience, Purpose = "dungeon ambience loop" },
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

            DemoDatabaseWiring.PreloadAudio();
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(DemoAudioWiring)}] Wired audio into {characters} characters, " +
                      $"{(horse ? "the horse, " : "")}{models} models and {weapons} weapons. " +
                      "The island's ambience is built with the sea (Rebuild Sea).\n" + Report());
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
            EditorUtility.SetDirty(hurt);

            AudioClip[] death = Clips(female ? WomanDeath : ManDeath);
            var deathComponent = root.GetComponent<CharacterDeathSoundComponent>();
            if (death.Length > 0)
            {
                deathComponent = GetOrAdd<CharacterDeathSoundComponent>(root);
                deathComponent.audioSource = Source(root, "_DeathAudioSource");
                deathComponent.settingType = AudioComponentSettingType.Sfx;
                deathComponent.soundData = new CharacterDeathSoundComponent.DeathSoundData { randomAudioClips = death };
                EditorUtility.SetDirty(deathComponent);
            }
            else if (deathComponent != null)
            {
                Object.DestroyImmediate(deathComponent);
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
            AudioClip[] punches = Clips(PunchSwing);
            var serialized = new SerializedObject(model);
            SetAttackClips(serialized.FindProperty("defaultAnimations.rightHandAttackAnimations"), punches);
            SetAttackClips(serialized.FindProperty("defaultAnimations.leftHandAttackAnimations"), punches);
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
                                        : $"  {hook.Prefix}: MISSING - {hook.Purpose}; add {hook.Prefix}1.wav ...\n");
            }
            return report.ToString();
        }
    }
}
