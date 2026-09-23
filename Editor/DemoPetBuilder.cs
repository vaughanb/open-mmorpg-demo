using UnityEditor;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// The Pup's Collar: the one item in the demo that puts a second creature on the field
    /// fighting for the player.
    ///
    /// **A pet is one asset and no code.** `PetItem.UseItem` is the whole loop: using it
    /// walks the character's summons, dismisses any pet already out, and - unless the one
    /// dismissed came from this same item - summons a fresh one through `CharacterSummon`.
    /// So the same click calls and sends away, and only one pet can be out at a time. What
    /// makes the summoned creature an ally is the summoner the kit hands it, not anything
    /// on the prefab: to the prefab it is an ordinary monster entity.
    ///
    /// The pup itself is built by <see cref="DemoWildlifeBuilder"/>, where the wolf's mesh,
    /// material, clips and measured collider already live.
    /// </summary>
    public static class DemoPetBuilder
    {
        private const string ItemDir = "Assets/OpenMMORPG/Demo/GameData/Resources/Items";
        private const string EntityDir = "Assets/OpenMMORPG/Demo/Prefabs/GamePlay/CharacterEntities";
        private const string PetEntity = EntityDir + "/DemoWolfPup.prefab";

        public const string CollarItem = "PupCollar";

        [MenuItem("Open MMORPG/Demo/Build Pet", priority = 158)]
        public static void Build()
        {
            var entity = AssetDatabase.LoadAssetAtPath<GameObject>(PetEntity);
            BaseMonsterCharacterEntity pet = entity != null ? entity.GetComponent<BaseMonsterCharacterEntity>() : null;
            if (pet == null)
            {
                Debug.LogError($"[{nameof(DemoPetBuilder)}] No pet entity at \"{PetEntity}\". " +
                               "Run Build Wildlife first - the pup is built with the wolf.");
                return;
            }

            var collar = Create<PetItem>($"{ItemDir}/{CollarItem}.asset");
            var serialized = new SerializedObject(collar);
            serialized.FindProperty("id").stringValue = CollarItem;
            serialized.FindProperty("defaultTitle").stringValue = "Pup's Collar";
            serialized.FindProperty("defaultDescription").stringValue =
                "Worn leather, chewed through twice and mended twice. Whistle into it and something comes.";
            serialized.FindProperty("sellPrice").intValue = 300;
            serialized.FindProperty("weight").floatValue = 0.3f;
            serialized.FindProperty("maxStack").intValue = 1;
            serialized.FindProperty("petEntity").objectReferenceValue = pet;

            // **No duration.** The kit will expire a summon on a timer if asked to, which
            // is right for a mage's conjured thing and wrong for an animal somebody owns -
            // a pet that vanishes on its own reads as a bug rather than as a rule. It stays
            // until the collar is used again or it is killed.
            serialized.FindProperty("noSummonDuration").boolValue = true;
            // Long enough that the collar is not an escape hatch, short enough to re-call
            // a pup that died without the player standing about.
            serialized.FindProperty("useItemCooldown").floatValue = 20f;

            DemoItemBuilder.AdoptItemIcon(serialized, "PupCollar");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(collar);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(DemoPetBuilder)}] Built the Pup's Collar, calling {pet.name}. " +
                      "Run Build NPCs And Quests for the pedlar's board and Wire Game Database to register it.");
        }

        private static T Create<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
