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

            // A dedicated server build leaves the editor on the Server subtarget, and the
            // editor then compiles the whole project with UNITY_SERVER: the kit strips every
            // client-only block out of play mode - animation and weapon sounds among them -
            // and nothing says why. The demo is played from the editor as a client, so the
            // editor goes back to the player subtarget whatever it was before.
            if (EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player)
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                Debug.Log($"[{nameof(DemoServerBuilder)}] Editor put back on the Player subtarget; the " +
                          "project recompiles without UNITY_SERVER. (TerrainShaderRepair, in Demo/Editor, then reimports " +
                          "URP's far-terrain shader, which the server build leaves stubbed - without that the " +
                          "island goes magenta past 220 m.)");
            }

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

        /// <summary>Where the second client goes: beside the server build, never over it.</summary>
        private const string ClientOutputDir = "builds_client";

        /// <summary>
        /// A second player for testing what needs two - trading, mail, player shops, duels,
        /// friends, parties, the guild bank, PvP. Press Play in 00Init for the servers and the
        /// first client, then start this build for the second; both log in to 127.0.0.1.
        ///
        /// A player build with no arguments starts no servers
        /// (`MMOServerInstance` only reads the editor's start-on-awake flags inside the
        /// editor), so this is a client and nothing else, however 00Init is set up.
        ///
        /// Its own folder, because `builds/` is the map server the spawner launches, and a client
        /// build written over it would leave the map spawner starting a client with a renderer.
        /// A full player, so it compiles the URP shaders - long the first time, cached after.
        /// </summary>
        [MenuItem("Open MMORPG/Demo/Build Test Client (second player)", priority = 101)]
        public static void BuildTestClient()
        {
            string[] scenes = ScenesInBuild();
            if (scenes.Length == 0)
            {
                Debug.LogError($"[{nameof(DemoServerBuilder)}] No scenes enabled in the build settings.");
                return;
            }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.Combine(root, ClientOutputDir, OutputName);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Debug.Log($"[{nameof(DemoServerBuilder)}] Building a test client to {output}.");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
                subtarget = (int)StandaloneBuildSubtarget.Player,
            };
            BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[{nameof(DemoServerBuilder)}] Built the test client {output} " +
                          $"({summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:F0}s).");
            }
            else
            {
                Debug.LogError($"[{nameof(DemoServerBuilder)}] Test client build {summary.result} with {summary.totalErrors} error(s).");
            }
        }

        [MenuItem("Open MMORPG/Demo/Build Test Client (second player)", validate = true)]
        public static bool CanBuildTestClient()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
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
