using UnityEditor;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Finds the script asset that defines a type, wherever it lives in the project.
    ///
    /// The builders swap a component's or an asset's script in place (`m_Script`) so it keeps
    /// its file id and every value on it, and that needs the <see cref="MonoScript"/> itself.
    /// They used to load it from a path written into each builder, which broke silently - the
    /// swap logged and did nothing - the moment a script was renamed or moved. Looked up by the
    /// type instead, a rename is followed by the compiler and a move needs nothing at all.
    /// </summary>
    internal static class DemoScriptAssets
    {
        /// <summary>The script asset whose class is <paramref name="type"/>, or null when there is none.</summary>
        public static MonoScript Of(System.Type type)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:MonoScript {type.Name}"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.GetClass() == type)
                    return candidate;
            }
            return null;
        }
    }
}
