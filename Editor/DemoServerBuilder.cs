using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MultiplayerARPG.Demo.EditorTools
{
    /// <summary>
    /// Builds the player that the map spawner launches.
    ///
    /// The demo runs the kit's MMO flow, and in that flow a map is not hosted by whoever
    /// is playing: the map spawn server starts a separate process per map and hands
    /// players to it. That process is a build of this project, so pressing Play in the
    /// editor is not enough on its own — the editor is the client and the login, central,
    /// database and map spawn servers, but it has no map server to give anyone until this
    /// has been run at least once.
    ///
    /// It cannot be the editor instead. Starting a map server in the editor process
    /// suppresses the home scene, so the login screen never appears and there is nothing
    /// to log in with.
    ///
    /// The output goes to <c>builds/</c> beside the project, which is where the scene's
    /// spawn path points — relative, so it works from wherever the project is checked out.
    /// </summary>
    public static class DemoServerBuilder
    {
        /// <summary>Relative to the project folder, matching MapSpawnNetworkManager.</summary>
        private const string OutputDir = "builds";
        private const string OutputName = "OpenMMORPG.exe";

        [MenuItem("Open MMORPG/Demo/Build Map Server", priority = 100)]
        public static void Build()
        {
            string[] scenes = ScenesInBuild();
            if (scenes.Length == 0)
            {
                Debug.LogError($"[{nameof(DemoServerBuilder)}] No scenes enabled in the build settings.");
                return;
            }

            string root = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.Combine(root, OutputDir, OutputName);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            bool headless = DedicatedServerInstalled;
            if (headless)
            {
                Debug.Log($"[{nameof(DemoServerBuilder)}] Building a dedicated server to {output}. " +
                          "No graphics stack, so no shader variants to compile.");
            }
            else
            {
                Debug.LogWarning($"[{nameof(DemoServerBuilder)}] Building a full player to {output}, because the " +
                                 "Dedicated Server module is not installed. This has to compile the whole URP " +
                                 "shader set — hundreds of thousands of variants, and the better part of an hour " +
                                 "the first time, though it is cached afterwards. Installing 'Windows Dedicated " +
                                 "Server Build Support' from the Unity Hub avoids it entirely: a map server draws " +
                                 "nothing, so it needs no shaders at all.");
            }

            if (headless)
                MatchServerDefinesToStandalone();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            };
            if (headless)
            {
                // The spawner runs this with -batchmode -nographics anyway; a dedicated
                // server build makes that official and strips the renderer out of it.
                options.subtarget = (int)StandaloneBuildSubtarget.Server;
            }
            BuildReport report = BuildPipeline.BuildPlayer(options);

            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[{nameof(DemoServerBuilder)}] Built {output} " +
                          $"({summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:F0}s). " +
                          "The map spawner can now start map servers.");
            }
            else
            {
                Debug.LogError($"[{nameof(DemoServerBuilder)}] Build {summary.result} with {summary.totalErrors} error(s).");
            }
        }

        /// <summary>
        /// Whether the build the spawner needs is actually there.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Map Server", validate = true)]
        public static bool CanBuild()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        /// <summary>
        /// Gives the dedicated server the same scripting defines as the normal player.
        ///
        /// Scripting defines are held per build target, and the dedicated server is its
        /// own target rather than a flavour of Standalone — so a project that sets, say,
        /// DISABLE_ADDRESSABLES for Standalone leaves the server compiling without it.
        /// The two then disagree about which fields exist on a serialized class, and the
        /// build dies with "script class layout is incompatible between the editor and the
        /// player", once per affected type. It is worth doing every build rather than
        /// once, because the defines only have to be edited for Standalone to drift apart
        /// again, and nothing warns you.
        /// </summary>
        private static void MatchServerDefinesToStandalone()
        {
            string standalone = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            string server = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Server);
            if (standalone == server)
                return;
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Server, standalone);
            Debug.Log($"[{nameof(DemoServerBuilder)}] Server scripting defines were \"{server}\", " +
                      $"now \"{standalone}\" to match the player. They have to agree or the two " +
                      "compile different fields and the build cannot serialize anything.");
        }

        /// <summary>
        /// Whether Unity can build a dedicated server here.
        ///
        /// Asked of the editor's own files rather than of an API, because the build only
        /// fails at the end if the module is missing, and by then the shaders have already
        /// been compiled for nothing. The server player is a separate download in the Hub
        /// and shows up as its own variation beside the normal ones.
        /// </summary>
        public static bool DedicatedServerInstalled
        {
            get
            {
                string editor = Path.GetDirectoryName(EditorApplication.applicationPath);
                string variations = Path.Combine(editor, "Data/PlaybackEngines/windowsstandalonesupport/Variations");
                if (!Directory.Exists(variations))
                    return false;
                foreach (string variation in Directory.GetDirectories(variations))
                {
                    if (Path.GetFileName(variation).Contains("server"))
                        return true;
                }
                return false;
            }
        }

        /// <summary>The path the map spawner will look for, for anything that wants to check.</summary>
        public static string ExpectedPath
        {
            get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputDir, OutputName); }
        }

        public static bool Exists
        {
            get { return File.Exists(ExpectedPath); }
        }

        private static string[] ScenesInBuild()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }
    }
}
