using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PimbaStage.EditorTools
{
    /// <summary>
    /// Tools > Pimba Stage > Create 3D Set Version (separate scene)
    ///   Makes Assets/PimbaStage/Scenes/PimbaStage3D.unity: a copy of the PimbaStage scene with the
    ///   Blender stage set (Assets/PimbaStage/3D/Cenario_test01.fbx) standing behind the playable stage.
    ///   The PimbaStage scene is never changed, closed or saved by this (unsaved edits in it are kept).
    ///   In the copy, the 2D crates and cardboard blocks are swapped for the Scene_test02 models (speakers, TV,
    ///   a 3D staircase and a block) at the same size and place, each with a collider traced from its shape.
    ///   Sandbags, carts, trapdoors and fire are left as they are.
    /// Tools > Pimba Stage > Apply 3D Speaker Bounce (keeps scene edits)
    ///   Makes every CAIXA DE SOM speaker in the 3D version squash and stretch on each beat of the song.
    /// Tools > Pimba Stage > Check 3D Obstacles
    ///   Tests that the performer can stand on and bump into every 3D obstacle.
    /// Tools > Pimba Stage > Build 3D Version (separate .exe)
    ///   Builds a player that contains only the 3D scene, into Builds/PimbaStage3D.
    /// Tools > Pimba Stage > Save 3D Set Preview Images
    ///   Renders the 3D stage along the whole run into Builds/PimbaStage3D_Previews.
    /// </summary>
    public static class PimbaStage3DBuilder
    {
        private const string ModelPath = "Assets/PimbaStage/3D/Cenario_test01.fbx";
        private const string ObstacleModelPath = "Assets/PimbaStage/3D/Scene_test02.fbx"; // the 3D obstacles
        private const string TextureFolder = "Assets/PimbaStage/3D/PIMBA";   // put the model's PIMBA texture folder here
        private const string MainScenePath = "Assets/PimbaStage/Scenes/PimbaStage.unity";
        private const string Scene3DPath = "Assets/PimbaStage/Scenes/PimbaStage3D.unity";
        private const string RendererPath = "Assets/PimbaStage/3D/PimbaStage3D_Renderer.asset";
        private const string WoodTexturePath = "Assets/PimbaStage/3D/PS3D_Wood.png";
        private const string FloorMaterialPath = "Assets/PimbaStage/3D/PS3D_StageFloor.mat";
        private const string WallMaterialPath = "Assets/PimbaStage/3D/PS3D_UntexturedWall.mat";
        private const string PieceMaterialPath = "Assets/PimbaStage/3D/PS3D_UntexturedPiece.mat";
        private const string AccentMaterialPath = "Assets/PimbaStage/3D/PS3D_UntexturedAccent";
        private const string BlackoutMaterialPath = "Assets/PimbaStage/3D/PS3D_Blackout.mat";
        private const string SourceWoodSprite = "Assets/PimbaStage/Sprites/PS_Wood.png";
        private const string BuildFolder = "Builds/PimbaStage3D";
        private const string PreviewFolder = "Builds/PimbaStage3D_Previews";

        // ---- Placement (world units). The players run along z = 0, from x = 2 to the goal at x = 138. ----
        // Change these and run "Create 3D Set Version" again to move the set.
        private const float StageDepth = 6.5f;     // the 2D stage floor covers z 0 .. 6.5
        private const float SetStartX = 4f;        // the set's first piece (speaker) starts here ...
        private const float SetEndX = 140f;        // ... and its last piece (top staircase) ends here
        private const float SetDepth = 70f;        // depth of the middle of the set's pieces (bigger = further back and smaller;
                                                   // 70 keeps the top staircases inside the view)
        private const float KeepClearDepth = 2f;   // set pieces closer than this to the players are hidden
        private const float ObstacleFrontZ = 0.05f; // obstacles start just behind the players, so the players stay in front
        private const float ColliderCell = 0.08f;  // detail of the obstacle colliders (traced from each model's shape)

        private static readonly Color WallColor = new Color(0.36f, 0.24f, 0.36f);
        private static readonly Color PieceColor = new Color(0.8f, 0.76f, 0.72f);
        private static readonly Color[] AccentColors = { new Color(0.85f, 0.62f, 0.25f), new Color(0.78f, 0.32f, 0.55f) };
        private static readonly Color FloorTint = new Color(0.86f, 0.64f, 0.44f);
        private static readonly Color BlackoutColor = new Color(0.05f, 0.02f, 0.04f);

        private class SetInfo
        {
            public UnityEngine.Renderer floor;
            public readonly List<UnityEngine.Renderer> walls = new List<UnityEngine.Renderer>();
            public readonly List<UnityEngine.Renderer> pieces = new List<UnityEngine.Renderer>();
            public readonly List<string> hidden = new List<string>();
            public float scale;
            public Quaternion rotation;
            public Bounds wallBounds, pieceBounds;
        }

        // =====================================================================================
        // Menu items
        // =====================================================================================

        [MenuItem("Tools/Pimba Stage/Create 3D Set Version (separate scene)")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) { Dialog("Stop Play mode first, then run this again."); return; }
            if (AssetImporter.GetAtPath(ModelPath) == null) { Dialog("Put the model at " + ModelPath + " first."); return; }
            if (!File.Exists(MainScenePath)) { Dialog("Build the stage first (Tools > Pimba Stage > Build Stage Scene)."); return; }
            if (File.Exists(Scene3DPath) && !EditorUtility.DisplayDialog("Pimba Stage",
                    "Replace the 3D version (" + Scene3DPath + ") with a fresh copy of the PimbaStage scene plus the 3D set?\n\n" +
                    "The PimbaStage scene itself is not changed.", "Replace", "Cancel"))
                return;

            ConfigureModelImport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) { Debug.LogError("Pimba Stage: could not load " + ModelPath); return; }

            // The 3D scene is about to be replaced: close it if it is open.
            Scene old3D = SceneManager.GetSceneByPath(Scene3DPath);
            if (old3D.IsValid() && old3D.isLoaded)
            {
                if (SceneManager.sceneCount == 1) EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
                else EditorSceneManager.CloseScene(old3D, true);
            }
            Scene previousActive = SceneManager.GetActiveScene();

            // Copy the stage scene: the open one (including unsaved changes, which stay unsaved there), or the saved file.
            Scene main = SceneManager.GetSceneByPath(MainScenePath);
            bool mainWasDirty = main.IsValid() && main.isLoaded && main.isDirty;
            if (main.IsValid() && main.isLoaded)
            {
                if (!EditorSceneManager.SaveScene(main, Scene3DPath, true)) { Debug.LogError("Pimba Stage: could not write " + Scene3DPath); return; }
            }
            else
            {
                File.Copy(MainScenePath, Scene3DPath, true); // keeps the 3D scene's .meta (and its place in Build Settings)
                AssetDatabase.ImportAsset(Scene3DPath, ImportAssetOptions.ForceSynchronousImport);
            }

            // Work on the copy next to whatever is open, then close it again.
            Scene scene = EditorSceneManager.OpenScene(Scene3DPath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene); // new objects and lighting settings go into the copy
                SetInfo info = BuildSet(scene, model);
                var obstacleModel = AssetImporter.GetAtPath(ObstacleModelPath) != null ? ConfigureObstacleImport() : null;
                string obstacleReport = obstacleModel != null ? BuildObstacles(scene, obstacleModel, info) : "no obstacle model";
                AddStageFloor(scene, info);
                AddBlackout(scene, info);
                SetUpLighting(scene, info);
                AddSpeakerBounce(scene); // the CAIXA DE SOM speakers squash and stretch on the beat
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AddToBuildSettingsDisabled();
                AssetDatabase.SaveAssets();
                RenderPreviews(scene);
                Debug.Log("Pimba Stage: 3D version created at " + Scene3DPath + " (set scale " + info.scale.ToString("0.00") +
                          "). The PimbaStage scene was not changed." +
                          (info.hidden.Count > 0 ? " Hidden (in front of the players): " + string.Join(", ", info.hidden) : "") +
                          " Obstacles: " + obstacleReport);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (SceneManager.sceneCount > 1 && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                // Copying must not make Unity forget that the PimbaStage scene has unsaved changes.
                Scene mainAfter = SceneManager.GetSceneByPath(MainScenePath);
                if (mainWasDirty && mainAfter.IsValid() && mainAfter.isLoaded && !mainAfter.isDirty) EditorSceneManager.MarkSceneDirty(mainAfter);
                Debug.Log("Pimba Stage: PimbaStage scene had unsaved changes: " + mainWasDirty + ", now: " +
                          (mainAfter.IsValid() && mainAfter.isLoaded ? mainAfter.isDirty.ToString() : "not open"));
            }
        }

        /// <summary>
        /// Makes every CAIXA DE SOM speaker in the 3D version (the obstacles and the ones in the stage set) squash and stretch
        /// on each beat of the song, by adding a "Speaker Beat Bounce" object to the 3D scene, without rebuilding it.
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Apply 3D Speaker Bounce (keeps scene edits)")]
        public static void ApplySpeakerBounce()
        {
            if (EditorApplication.isPlaying) { Dialog("Stop Play mode first, then run this again."); return; }
            if (!File.Exists(Scene3DPath)) { Dialog("Create the 3D version first (Tools > Pimba Stage > Create 3D Set Version)."); return; }

            Scene scene = SceneManager.GetSceneByPath(Scene3DPath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            bool hadOtherChanges = wasOpen && scene.isDirty;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(Scene3DPath, OpenSceneMode.Additive);
            int speakers = AddSpeakerBounce(scene);
            string saved;
            if (!hadOtherChanges && EditorSceneManager.SaveScene(scene)) saved = "saved";
            else
            {
                EditorSceneManager.MarkSceneDirty(scene);
                saved = "press Ctrl+S to keep it; it had other unsaved changes, so it wasn't saved for you";
            }
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            Debug.Log("Pimba Stage: " + speakers + " CAIXA DE SOM speakers now squash and stretch on the beat in " + scene.name + " (" + saved + ").");
        }

        private static int AddSpeakerBounce(Scene scene)
        {
            Conductor conductor = null;
            BeatSquashStretch bounce = null;
            int speakers = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (conductor == null) conductor = root.GetComponentInChildren<Conductor>(true);
                if (bounce == null) bounce = root.GetComponentInChildren<BeatSquashStretch>(true);
                if (!root.activeSelf) continue; // the hidden copy of the obstacle model
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("CAIXA DE SOM")) speakers++;
            }
            if (bounce == null)
            {
                var go = new GameObject("Speaker Beat Bounce (CAIXA DE SOM)");
                SceneManager.MoveGameObjectToScene(go, scene);
                bounce = go.AddComponent<BeatSquashStretch>();
                Undo.RegisterCreatedObjectUndo(go, "Speaker beat bounce");
            }
            else Undo.RecordObject(bounce, "Speaker beat bounce");
            bounce.conductor = conductor;
            bounce.namePrefix = "CAIXA DE SOM";
            EditorUtility.SetDirty(bounce);
            return speakers;
        }

        [MenuItem("Tools/Pimba Stage/Build 3D Version (separate .exe)")]
        public static void BuildPlayer3D()
        {
            if (EditorApplication.isPlaying) { Dialog("Stop Play mode first, then run this again."); return; }
            if (!File.Exists(Scene3DPath)) { Dialog("Create the 3D version first (Tools > Pimba Stage > Create 3D Set Version)."); return; }

            // Only the 3D scene is built (from its saved file); save it first if it is open with changes.
            Scene open3D = SceneManager.GetSceneByPath(Scene3DPath);
            if (open3D.IsValid() && open3D.isLoaded && open3D.isDirty) EditorSceneManager.SaveScene(open3D);

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string extension = target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64 ? ".exe"
                : target == BuildTarget.StandaloneOSX ? ".app" : "";
            string location = BuildFolder + "/" + PlayerSettings.productName + " 3D" + extension;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene3DPath },  // only the 3D scene: the normal build is not touched
                locationPathName = location,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log("Pimba Stage: 3D version built at " + Path.GetFullPath(location)
                          + " (" + (summary.totalSize / (1024f * 1024f)).ToString("0") + " MB, " + summary.totalTime.TotalSeconds.ToString("0") + " s).");
            else
                Debug.LogError("Pimba Stage: the 3D build " + summary.result + " with " + summary.totalErrors + " error(s). See the Console.");
        }

        [MenuItem("Tools/Pimba Stage/Play 3D Version (.exe)")]
        public static void Run3D()
        {
            string folder = Path.GetFullPath(BuildFolder);
            string[] players = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.exe") : new string[0];
            string player = null;
            foreach (string file in players)
                if (!Path.GetFileName(file).StartsWith("UnityCrashHandler")) player = file;
            if (player == null) { Dialog("Build the 3D version first (Tools > Pimba Stage > Build 3D Version)."); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(player) { WorkingDirectory = folder, UseShellExecute = true });
        }

        /// <summary>
        /// Drops a performer-sized capsule onto every 3D obstacle and slides one into it from the left,
        /// and reports where it stops (Builds/PimbaStage3D_Previews/obstacle_check.txt).
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Check 3D Obstacles")]
        public static void CheckObstacles()
        {
            if (EditorApplication.isPlaying) { Dialog("Stop Play mode first, then run this again."); return; }
            if (!File.Exists(Scene3DPath)) { Dialog("Create the 3D version first (Tools > Pimba Stage > Create 3D Set Version)."); return; }
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(Scene3DPath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded) { scene = EditorSceneManager.OpenScene(Scene3DPath, OpenSceneMode.Additive); opened = true; }
            var hiddenRoots = new List<GameObject>();
            var report = new StringBuilder();
            int good = 0, problems = 0;
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene other = SceneManager.GetSceneAt(i);
                    if (other == scene || !other.isLoaded) continue;
                    foreach (var root in other.GetRootGameObjects())
                        if (root.activeSelf) { root.SetActive(false); hiddenRoots.Add(root); }
                }
                Physics2D.SyncTransforms();
                var filter = new ContactFilter2D { useTriggers = false };
                filter.SetLayerMask(Physics2D.DefaultRaycastLayers);
                var hits = new List<RaycastHit2D>();
                var size = new Vector2(0.9f, 1.45f); // the performer's capsule
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (!root.name.StartsWith("3D Obstacles")) continue;
                    foreach (Transform obstacle in root.transform)
                    {
                        Bounds vis = new Bounds();
                        bool any = false;
                        foreach (var mr in obstacle.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (!any) { vis = mr.bounds; any = true; } else vis.Encapsulate(mr.bounds);
                        }
                        if (!any) continue;
                        report.Append(obstacle.name).Append(": top ").Append(vis.max.y.ToString("0.00"));

                        // from above: where do the performer's feet land?
                        var from = new Vector2(vis.center.x, vis.max.y + 3f);
                        int n = Physics2D.CapsuleCast(from, size, CapsuleDirection2D.Vertical, 0f, Vector2.down, filter, hits, 10f);
                        RaycastHit2D? best = null;
                        for (int k = 0; k < n; k++) if (best == null || hits[k].distance < best.Value.distance) best = hits[k];
                        bool landedOnIt = best != null && best.Value.collider.transform.IsChildOf(obstacle);
                        float feet = best != null ? from.y - best.Value.distance - size.y * 0.5f : float.NaN;
                        bool standOk = landedOnIt && Mathf.Abs(feet - vis.max.y) < 0.15f;
                        report.Append(" | lands at ").Append(feet.ToString("0.00")).Append(landedOnIt ? " on it" : " NOT on it")
                              .Append(standOk ? " OK" : " PROBLEM");

                        // from the left, at the performer's height above the floor: is it solid?
                        bool sideOk = true;
                        if (vis.min.y < 0.05f)
                        {
                            var side = new Vector2(vis.min.x - 3f, 0.75f);
                            n = Physics2D.CapsuleCast(side, size, CapsuleDirection2D.Vertical, 0f, Vector2.right, filter, hits, 6f);
                            best = null;
                            for (int k = 0; k < n; k++) if (best == null || hits[k].distance < best.Value.distance) best = hits[k];
                            bool hitIt = best != null && best.Value.collider.transform.IsChildOf(obstacle);
                            float front = best != null ? side.x + best.Value.distance + size.x * 0.5f : float.NaN;
                            sideOk = hitIt && Mathf.Abs(front - vis.min.x) < 0.2f;
                            report.Append(" | blocks from the left at x ").Append(front.ToString("0.00")).Append(" (model starts ")
                                  .Append(vis.min.x.ToString("0.00")).Append(")").Append(sideOk ? " OK" : " PROBLEM");
                        }
                        report.AppendLine();
                        if (standOk && sideOk) good++; else problems++;
                    }
                }
            }
            finally
            {
                foreach (var root in hiddenRoots) if (root != null) root.SetActive(true);
                Physics2D.SyncTransforms();
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (opened && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
            Directory.CreateDirectory(PreviewFolder);
            File.WriteAllText(PreviewFolder + "/obstacle_check.txt", report.ToString());
            Debug.Log("Pimba Stage: obstacle check - " + good + " OK, " + problems + " problem(s). Details: " + Path.GetFullPath(PreviewFolder + "/obstacle_check.txt"));
        }

        [MenuItem("Tools/Pimba Stage/Save 3D Set Preview Images")]
        public static void SavePreviews()
        {
            if (EditorApplication.isPlaying) { Dialog("Stop Play mode first, then run this again."); return; }
            if (!File.Exists(Scene3DPath)) { Dialog("Create the 3D version first (Tools > Pimba Stage > Create 3D Set Version)."); return; }
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(Scene3DPath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(Scene3DPath, OpenSceneMode.Additive);
                opened = true;
            }
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderPreviews(scene);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (opened && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // =====================================================================================
        // Building the set
        // =====================================================================================

        private static void ConfigureModelImport()
        {
            // Textures from the model's PIMBA folder (if it was copied in): normal / mask maps are not colour.
            if (AssetDatabase.IsValidFolder(TextureFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null) continue;
                    string file = Path.GetFileNameWithoutExtension(path);
                    bool normal = file.EndsWith("_Normal");
                    bool mask = file.EndsWith("_Roughness") || file.EndsWith("_Metallic") || file.EndsWith("_Alpha");
                    bool changed = false;
                    if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                    if (mask && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
                    if (changed) ti.SaveAndReimport();
                }
            }

            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            bool reimport = AssetDatabase.IsValidFolder(TextureFolder); // picks up newly added textures
            if (importer.importCameras) { importer.importCameras = false; reimport = true; } // the game has its own camera
            if (importer.importLights) { importer.importLights = false; reimport = true; }   // lights are set up below
            if (importer.animationType != ModelImporterAnimationType.Legacy) { importer.animationType = ModelImporterAnimationType.Legacy; reimport = true; }
            if (reimport) importer.SaveAndReimport();
        }

        private static SetInfo BuildSet(Scene scene, GameObject model)
        {
            var info = new SetInfo();

            // The 3D set replaces the painted backdrop, the black wings and the bunting.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Painted Backdrop" || t.name == "Wing" || t.name == "Bunting")
                        t.gameObject.SetActive(false);

            var holder = new GameObject("3D Set (Cenario_test01)");
            var set = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
            set.AddComponent<LoopAllAnimations>(); // the spinning wall disc and the tumbling staircase

            // Blender's camera looks at the set from the side, with the set running along its -Z:
            // turn it so the set runs along +X and its back walls are behind the stage.
            holder.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            Bounds b = WorldBounds(set);
            if (b.size.z > b.size.x) holder.transform.rotation *= Quaternion.Euler(0f, 90f, 0f);
            info.floor = FindFloor(set);
            UnityEngine.Renderer tallest = FindTallest(set);
            if (info.floor != null && tallest != null && tallest.bounds.center.z < info.floor.bounds.center.z)
                holder.transform.rotation *= Quaternion.Euler(0f, 180f, 0f);
            info.rotation = holder.transform.rotation;

            // Sort the pieces: back walls, things in front of the set's floor (curtains, truss), and the set pieces.
            Bounds all = WorldBounds(set);
            float floorCentreZ = info.floor != null ? info.floor.bounds.center.z : all.center.z;
            float floorFrontZ = info.floor != null ? info.floor.bounds.min.z : all.min.z;
            var front = new List<UnityEngine.Renderer>();
            foreach (var r in set.GetComponentsInChildren<UnityEngine.Renderer>(true))
            {
                if (r == info.floor) continue;
                Bounds rb = r.bounds;
                if (rb.center.z > floorCentreZ && rb.size.y > 0.25f * all.size.y) info.walls.Add(r);
                else if (rb.center.z < floorFrontZ && (rb.size.y > 0.25f * all.size.y || rb.size.x > 0.25f * all.size.x)) front.Add(r);
                else info.pieces.Add(r);
            }
            if (info.pieces.Count == 0) info.pieces.AddRange(set.GetComponentsInChildren<UnityEngine.Renderer>(true));

            // Scale: the pieces run from SetStartX to SetEndX. Then floor top at y = 0, pieces centred at SetDepth.
            Bounds pb = Encapsulate(info.pieces);
            info.scale = (SetEndX - SetStartX) / Mathf.Max(0.01f, pb.size.x);
            holder.transform.localScale = Vector3.one * info.scale;
            pb = Encapsulate(info.pieces);
            float floorTop = info.floor != null ? info.floor.bounds.max.y : WorldBounds(set).min.y;
            holder.transform.position += new Vector3(SetStartX - pb.min.x, -floorTop, SetDepth - pb.center.z);

            // Hide what would stand in front of (or right at) the players, and the set's own floor (replaced below).
            foreach (var r in front) Hide(r, info);
            foreach (var r in info.pieces.ToArray())
            {
                if (r.bounds.min.z < KeepClearDepth)
                {
                    Hide(r, info);
                    info.pieces.Remove(r);
                }
            }
            if (info.floor != null)
            {
                info.floor.gameObject.SetActive(false);
                info.hidden.Add(info.floor.name + " (floor, replaced by a wood stage floor)");
            }

            // Pieces with no material in the file get Unity's default material, which in this (2D) project is a 2D
            // material that the 3D renderer draws plain white: give them proper 3D materials instead.
            Material wall = LitMaterial(WallMaterialPath, WallColor, 0.15f);
            Material piece = LitMaterial(PieceMaterialPath, PieceColor, 0.25f);
            var discs = new List<UnityEngine.Renderer>();
            foreach (var r in info.walls)
                if (r.name.StartsWith("Circle")) discs.Add(r);
            discs.Sort((a, c) => c.bounds.size.x.CompareTo(a.bounds.size.x));
            foreach (var r in set.GetComponentsInChildren<UnityEngine.Renderer>(true))
            {
                Material fallback = piece;
                bool isWall = info.walls.Contains(r);
                if (isWall)
                {
                    int d = discs.IndexOf(r);
                    fallback = d >= 0 ? LitMaterial(AccentMaterialPath + d + ".mat", AccentColors[d % AccentColors.Length], 0.3f) : wall;
                }
                var mats = r.sharedMaterials;
                bool changed = mats.Length == 0;
                if (changed) mats = new[] { fallback };
                for (int i = 0; i < mats.Length; i++)
                {
                    bool missing = mats[i] == null || mats[i].shader == null || mats[i].shader.name.Contains("/2D/");
                    bool plainWall = isWall && !missing && !(mats[i].HasProperty("_BaseMap") && mats[i].GetTexture("_BaseMap") != null);
                    if (missing || plainWall) { mats[i] = fallback; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }

            info.pieceBounds = Encapsulate(info.pieces);
            info.wallBounds = info.walls.Count > 0 ? Encapsulate(info.walls) : info.pieceBounds;
            return info;
        }

        /// <summary>A lit wood floor from the back of the 2D stage to the back walls, across the whole camera run.</summary>
        private static void AddStageFloor(Scene scene, SetInfo info)
        {
            float back = Mathf.Max(info.wallBounds.max.z, info.pieceBounds.max.z) + 2f;
            float depth = back - StageDepth;
            const float minX = -60f, maxX = 220f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "3D Stage Floor";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(new Vector3((minX + maxX) * 0.5f, -0.01f, StageDepth + depth * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(maxX - minX, depth, 1f);
            const float tileSize = 2f;
            var mat = LitMaterial(FloorMaterialPath, FloorTint, 0.3f);
            Texture2D wood = WoodTexture();
            if (wood != null)
            {
                mat.SetTexture("_BaseMap", wood);
                mat.SetTextureScale("_BaseMap", new Vector2((maxX - minX) / tileSize, depth / tileSize));
                EditorUtility.SetDirty(mat);
            }
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A plain wall far behind everything, in the walls' colour, so the set has no gaps at its ends.</summary>
        private static void AddBlackout(Scene scene, SetInfo info)
        {
            float z = Mathf.Max(info.wallBounds.max.z, info.pieceBounds.max.z) + 6f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "3D Backstage Wall";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(new Vector3(80f, 30f, z), Quaternion.identity);
            go.transform.localScale = new Vector3(600f, 200f, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = LitMaterial(BlackoutMaterialPath, WallColor * 0.8f, 0.1f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static void SetUpLighting(Scene scene, SetInfo info)
        {
            // This scene renders with a 3D (Universal, Forward+) renderer so the model gets real lighting;
            // the normal game keeps the 2D renderer.
            int rendererIndex = EnsureForwardRenderer();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (rendererIndex >= 0) cam.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex);
                    cam.farClipPlane = Mathf.Max(cam.farClipPlane, 400f);
                }
            }

            // Soft, warm key light.
            var sun = new GameObject("Stage Sun (3D)");
            var light = sun.AddComponent<UnityEngine.Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.85f);
            light.intensity = 0.75f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(40f, -25f, 0f);

            // Coloured spotlights washing the back walls (theatre look), spread along the run.
            Color[] colours = { new Color(1f, 0.5f, 0.78f), new Color(0.5f, 0.75f, 1f), new Color(1f, 0.78f, 0.4f) };
            int n = 0;
            Bounds w = info.wallBounds;
            float top = Mathf.Min(w.max.y, 34f);
            for (float x = w.min.x + 8f; x <= w.max.x - 4f; x += 15f)
            {
                float wallZ = FrontWallZ(info, x);
                if (float.IsNaN(wallZ)) wallZ = w.min.z;
                var go = new GameObject("Stage Spot (3D)");
                var spot = go.AddComponent<UnityEngine.Light>();
                spot.type = LightType.Spot;
                spot.color = colours[n++ % colours.Length];
                var position = new Vector3(x, top * 0.8f, wallZ - 9f);
                var target = new Vector3(x + 3f, top * 0.3f, wallZ);
                float distance = Vector3.Distance(position, target);
                spot.intensity = 0.7f * distance * distance; // URP lights fade with distance squared
                spot.range = distance * 1.8f;
                spot.spotAngle = 55f;
                spot.innerSpotAngle = 28f;
                go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.26f, 0.23f, 0.3f);
        }


        // =====================================================================================
        // 3D obstacles: the 2D crates and cardboard blocks are swapped for the Scene_test02 models
        // (same height, same place). Sandbags, carts, trapdoors and fire are left as they are.
        // =====================================================================================

        private class Slot
        {
            public Transform block;
            public Bounds box;          // the 2D block's collider (world)
            public bool hanging;
            public List<Slot> stairGroup; // crates that form a staircase (replaced by one staircase)
        }

        private static GameObject ConfigureObstacleImport()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ObstacleModelPath);
            bool reimport = false;
            if (importer.importCameras) { importer.importCameras = false; reimport = true; }
            if (importer.importLights) { importer.importLights = false; reimport = true; }
            if (importer.importAnimation) { importer.importAnimation = false; reimport = true; } // obstacles stand still
            if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; reimport = true; }
            if (!importer.isReadable) { importer.isReadable = true; reimport = true; } // the colliders are traced from the meshes
            if (reimport) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(ObstacleModelPath);
        }

        private static string BuildObstacles(Scene scene, GameObject model, SetInfo info)
        {
            // The 2D blocks in the copied level.
            var slots = new List<Slot>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "Stage") continue;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Crate" && t.name != "Hanging Crate" && t.name != "Cardboard Flat") continue;
                    var box = t.GetComponent<BoxCollider2D>();
                    if (box == null) continue;
                    slots.Add(new Slot { block = t, box = box.bounds, hanging = t.name == "Hanging Crate" });
                }
            }
            if (slots.Count == 0) return "no crates or cardboard blocks found";
            slots.Sort((a, b) => a.box.center.x.CompareTo(b.box.center.x));

            // Crates standing side by side and getting taller form a staircase: one 3D staircase replaces them.
            for (int i = 0; i < slots.Count; i++)
            {
                var group = new List<Slot> { slots[i] };
                for (int j = i + 1; j < slots.Count; j++)
                {
                    Slot last = group[group.Count - 1], next = slots[j];
                    bool touching = Mathf.Abs(next.box.min.x - last.box.max.x) < 0.05f;
                    bool grounded = next.box.min.y < 0.05f && last.box.min.y < 0.05f && !next.hanging && !last.hanging;
                    if (touching && grounded && next.box.size.y > last.box.size.y + 0.05f) group.Add(next); else break;
                }
                if (group.Count >= 3)
                {
                    foreach (var g in group) g.stairGroup = group;
                    i += group.Count - 1;
                }
            }

            // One copy of the obstacles file, turned like the set, used as the source of the pieces.
            var library = (GameObject)PrefabUtility.InstantiatePrefab(model);
            library.name = "3D Obstacle Models (source, hidden)";
            library.transform.rotation = info.rotation;
            var renderers = library.GetComponentsInChildren<MeshRenderer>(true);
            Transform speakerA = FindPiece(library, "CAIXA DE SOM"), speakerB = FindPiece(library, "CAIXA DE SOM.002");
            Transform tv = FindPiece(library, "Cylinder.002"), stairs = FindPiece(library, "Plane.009"), block = FindPiece(library, "Cube.005");
            if (speakerA == null) speakerA = speakerB != null ? speakerB : renderers[0].transform;
            if (speakerB == null) speakerB = speakerA;
            if (tv == null) tv = speakerA;
            if (stairs == null) stairs = speakerA;
            if (block == null) block = speakerA;
            Material piece = LitMaterial(PieceMaterialPath, PieceColor, 0.25f);

            var parent = new GameObject("3D Obstacles (Scene_test02)").transform;
            var used = new List<string>();
            int speakers = 0;
            var doneGroups = new HashSet<List<Slot>>();
            foreach (var slot in slots)
            {
                Bounds b = slot.box;
                if (slot.stairGroup != null)
                {
                    if (doneGroups.Add(slot.stairGroup))
                    {
                        Bounds g = slot.stairGroup[0].box;
                        foreach (var s in slot.stairGroup) g.Encapsulate(s.box);
                        var root = NewObstacle(parent, "Stairs", g);
                        var go = ClonePiece(stairs, root, false, piece);
                        Fit(go, g.size.y, g.size.x * 1.35f);
                        // the staircase must climb towards the tallest crate (the right): mirror it if it climbs the other way
                        if (TopAt(go, 0.2f) > TopAt(go, 0.8f)) { go.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * go.transform.rotation; }
                        Place(go, g.max.x, true, g.min.y);
                        AddTracedCollider(root, go);
                        used.Add("staircase for " + slot.stairGroup.Count + " crates at x " + g.center.x.ToString("0"));
                    }
                }
                else if (slot.hanging)
                {
                    var root = NewObstacle(parent, "Block (hanging)", b);
                    var go = ClonePiece(block, root, true, piece);
                    Bounds pb = RendererBounds(go);
                    go.transform.localScale *= b.size.x / Mathf.Max(0.001f, pb.size.x);
                    pb = RendererBounds(go);
                    go.transform.position += new Vector3(b.center.x - pb.center.x, b.max.y - pb.max.y, ObstacleFrontZ - pb.min.z);
                    AddTracedCollider(root, go);
                    // keep its ropes
                    foreach (Transform child in slot.block.Cast<Transform>().ToArray())
                        if (child.name == "Rope") child.SetParent(root, true);
                    used.Add("hanging block at x " + b.center.x.ToString("0"));
                }
                else
                {
                    float w = b.size.x, h = b.size.y;
                    if (h / w >= 1.6f)
                    {
                        // tall and thin (the end wall): a stack of speakers
                        int n = Mathf.Max(2, Mathf.RoundToInt(h / w));
                        var root = NewObstacle(parent, "Speaker Stack", b);
                        for (int k = 0; k < n; k++)
                        {
                            var go = ClonePiece(speakers++ % 2 == 0 ? speakerA : speakerB, root, false, piece);
                            Fit(go, h / n, w * 1.35f);
                            Place(go, b.center.x, false, b.min.y + k * h / n);
                            AddTracedCollider(root, go);
                        }
                        used.Add(n + " stacked speakers at x " + b.center.x.ToString("0"));
                    }
                    else
                    {
                        bool wide = w / h >= 1.15f;
                        var root = NewObstacle(parent, wide ? "TV" : "Speaker", b);
                        var go = ClonePiece(wide ? tv : (speakers++ % 2 == 0 ? speakerA : speakerB), root, false, piece);
                        Fit(go, h, w * 1.35f);
                        Place(go, b.center.x, false, b.min.y);
                        AddTracedCollider(root, go);
                        used.Add((wide ? "TV" : "speaker") + " at x " + b.center.x.ToString("0"));
                    }
                }
            }

            foreach (var slot in slots) Object.DestroyImmediate(slot.block.gameObject);
            library.SetActive(false);
            return slots.Count + " crates/blocks swapped (" + string.Join("; ", used) + ")";
        }

        private static Transform FindPiece(GameObject library, string name)
        {
            foreach (var t in library.GetComponentsInChildren<Transform>(true))
                if (t.name == name && t.GetComponent<MeshRenderer>() != null) return t;
            return null;
        }

        private static Transform NewObstacle(Transform parent, string kind, Bounds slot)
        {
            var root = new GameObject("Obstacle: " + kind + " (x " + slot.center.x.ToString("0.0") + ")").transform;
            root.SetParent(parent, false);
            return root;
        }

        /// <summary>A copy of one model from the obstacles file, the same size and turn as in the file (or laid flat).</summary>
        private static GameObject ClonePiece(Transform template, Transform root, bool layFlat, Material fallback)
        {
            var go = Object.Instantiate(template.gameObject);
            go.name = template.name;
            go.transform.SetPositionAndRotation(template.position, layFlat ? Flat(template) : template.rotation);
            go.transform.localScale = template.lossyScale;
            go.transform.SetParent(root, true);
            foreach (var anim in go.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(anim);
            foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == null || mats[i].shader == null || mats[i].shader.name.Contains("/2D/")) { mats[i] = fallback; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>Turn that lays a box-shaped model flat: thinnest side up, longest side along the stage.</summary>
        private static Quaternion Flat(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return t.rotation;
            Vector3 s = t.localScale;
            Vector3 e = Vector3.Scale(mf.sharedMesh.bounds.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
            int thin = e.x <= e.y && e.x <= e.z ? 0 : (e.y <= e.z ? 1 : 2);
            int longest = e.x >= e.y && e.x >= e.z ? 0 : (e.y >= e.z ? 1 : 2);
            if (longest == thin) longest = (thin + 1) % 3;
            int mid = 3 - thin - longest;
            Vector3 up = Axis(thin), forward = Axis(mid);
            return Quaternion.Inverse(Quaternion.LookRotation(forward, up));
        }

        private static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>Uniform scale so the model is as tall as the block (but not much wider than it).</summary>
        private static void Fit(GameObject go, float height, float maxWidth)
        {
            Bounds b = RendererBounds(go);
            float k = height / Mathf.Max(0.001f, b.size.y);
            if (b.size.x * k > maxWidth) k = maxWidth / Mathf.Max(0.001f, b.size.x);
            go.transform.localScale *= k;
        }

        /// <summary>Stands the model on y = bottom, centred on x (or with its right side at x), just behind the players.</summary>
        private static void Place(GameObject go, float x, bool rightAligned, float bottom)
        {
            Bounds b = RendererBounds(go);
            float dx = rightAligned ? x - b.max.x : x - b.center.x;
            go.transform.position += new Vector3(dx, bottom - b.min.y, ObstacleFrontZ - b.min.z);
        }

        private static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<UnityEngine.Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>Height of the model's top at a fraction (0 = left, 1 = right) of its width.</summary>
        private static float TopAt(GameObject go, float fraction)
        {
            Bounds b = RendererBounds(go);
            float x = Mathf.Lerp(b.min.x, b.max.x, fraction), best = float.NegativeInfinity;
            foreach (var tri in WorldTriangles(go))
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector2 p = tri[k], q = tri[(k + 1) % 3];
                    if ((p.x - x) * (q.x - x) > 0f) continue;
                    float t = Mathf.Abs(q.x - p.x) < 1e-6f ? 0f : (x - p.x) / (q.x - p.x);
                    best = Mathf.Max(best, Mathf.Lerp(p.y, q.y, t));
                }
            }
            return best;
        }

        private static List<Vector2[]> WorldTriangles(GameObject go)
        {
            var tris = new List<Vector2[]>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Vector3[] v = mesh.vertices;
                var w = new Vector2[v.Length];
                for (int i = 0; i < v.Length; i++) { Vector3 p = mf.transform.TransformPoint(v[i]); w[i] = new Vector2(p.x, p.y); }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] idx = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < idx.Length; i += 3) tris.Add(new[] { w[idx[i]], w[idx[i + 1]], w[idx[i + 2]] });
                }
            }
            return tris;
        }

        /// <summary>
        /// A solid 2D collider with the model's shape as seen from the front (steps of a staircase included):
        /// the shape is traced on a fine grid and merged into one outline.
        /// </summary>
        private static void AddTracedCollider(Transform root, GameObject piece)
        {
            List<Vector2[]> tris = WorldTriangles(piece);
            Bounds b = RendererBounds(piece);
            var colGo = new GameObject("Collider (" + piece.name + ")");
            colGo.transform.SetParent(root, false);
            var body = colGo.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var composite = colGo.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;

            int nx = Mathf.Clamp(Mathf.CeilToInt(b.size.x / ColliderCell), 1, 600);
            int ny = Mathf.Clamp(Mathf.CeilToInt(b.size.y / ColliderCell), 1, 600);
            float cw = b.size.x / nx, ch = b.size.y / ny;
            var filled = new bool[nx, ny];
            int count = 0;
            foreach (var t in tris)
            {
                Vector2 a = t[0], c1 = t[1], c2 = t[2];
                float area = (c1.x - a.x) * (c2.y - a.y) - (c1.y - a.y) * (c2.x - a.x);
                if (Mathf.Abs(area) < 1e-9f) continue;
                int i0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(c1.x, c2.x)) - b.min.x) / cw), 0, nx - 1);
                int i1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(c1.x, c2.x)) - b.min.x) / cw), 0, nx - 1);
                int j0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, Mathf.Min(c1.y, c2.y)) - b.min.y) / ch), 0, ny - 1);
                int j1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.y, Mathf.Max(c1.y, c2.y)) - b.min.y) / ch), 0, ny - 1);
                for (int i = i0; i <= i1; i++)
                {
                    for (int j = j0; j <= j1; j++)
                    {
                        if (filled[i, j]) continue;
                        var p = new Vector2(b.min.x + (i + 0.5f) * cw, b.min.y + (j + 0.5f) * ch);
                        float d1 = Cross(a, c1, p), d2 = Cross(c1, c2, p), d3 = Cross(c2, a, p);
                        bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                        if (!(neg && pos)) { filled[i, j] = true; count++; }
                    }
                }
            }

            if (count == 0)
            {
                var box = colGo.AddComponent<BoxCollider2D>();
                box.offset = b.center;
                box.size = b.size;
                box.compositeOperation = Collider2D.CompositeOperation.Merge;
            }
            else
            {
                for (int j = 0; j < ny; j++)
                {
                    int i = 0;
                    while (i < nx)
                    {
                        if (!filled[i, j]) { i++; continue; }
                        int start = i;
                        while (i < nx && filled[i, j]) i++;
                        var box = colGo.AddComponent<BoxCollider2D>();
                        box.offset = new Vector2(b.min.x + (start + i) * 0.5f * cw, b.min.y + (j + 0.5f) * ch);
                        box.size = new Vector2((i - start) * cw, ch);
                        box.compositeOperation = Collider2D.CompositeOperation.Merge;
                    }
                }
            }
            composite.GenerateGeometry();
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        // =====================================================================================
        // Previews (also a report of where every piece ended up)
        // =====================================================================================

        private static void RenderPreviews(Scene scene)
        {
            Camera cam = null;
            StageScroller scroller = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                scroller = root.GetComponentInChildren<StageScroller>(true);
                if (scroller != null) { cam = scroller.GetComponent<Camera>(); break; }
            }
            if (cam == null) { Debug.LogError("Pimba Stage: no stage camera found in " + scene.path); return; }

            // Only the 3D scene may show up in the pictures: other open scenes are hidden for a moment.
            var hiddenRoots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene other = SceneManager.GetSceneAt(i);
                if (other == scene || !other.isLoaded) continue;
                foreach (var root in other.GetRootGameObjects())
                    if (root.activeSelf) { root.SetActive(false); hiddenRoots.Add(root); }
            }

            Directory.CreateDirectory(PreviewFolder);
            foreach (string old in Directory.GetFiles(PreviewFolder, "preview_*.png")) File.Delete(old);
            const int width = 1280, height = 948; // the playfield's shape (left 76% of a 16:9 screen)
            Vector3 position = cam.transform.position;
            UnityEngine.Rect rect = cam.rect;
            var rt = new RenderTexture(width, height, 24);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                cam.rect = new UnityEngine.Rect(0f, 0f, 1f, 1f);
                cam.targetTexture = rt;
                for (int i = 0; i <= 6; i++)
                {
                    float x = Mathf.Lerp(scroller.startX, scroller.endX, i / 6f);
                    cam.transform.position = new Vector3(x, position.y, position.z);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(PreviewFolder + "/preview_" + i + "_x" + x.ToString("0") + ".png", tex.EncodeToPNG());
                }
            }
            finally
            {
                cam.targetTexture = null;
                cam.rect = rect;
                cam.transform.position = position;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                foreach (var root in hiddenRoots) if (root != null) root.SetActive(true);
            }

            var report = new StringBuilder();
            report.AppendLine("camera " + cam.transform.position.ToString("0.0") + " fov " + cam.fieldOfView + " far " + cam.farClipPlane +
                              " run x " + scroller.startX + ".." + scroller.endX);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith("3D Obstacles")) continue;
                foreach (Transform obstacle in root.transform)
                {
                    Bounds vis = RendererBounds(obstacle.gameObject);
                    report.Append(obstacle.name).Append("  model x ").Append(vis.min.x.ToString("0.00")).Append("..").Append(vis.max.x.ToString("0.00"))
                        .Append(" y ").Append(vis.min.y.ToString("0.00")).Append("..").Append(vis.max.y.ToString("0.00"))
                        .Append(" z ").Append(vis.min.z.ToString("0.00")).Append("..").Append(vis.max.z.ToString("0.00"));
                    foreach (var cc in obstacle.GetComponentsInChildren<CompositeCollider2D>(true))
                        report.Append("  | collider x ").Append(cc.bounds.min.x.ToString("0.00")).Append("..").Append(cc.bounds.max.x.ToString("0.00"))
                            .Append(" y ").Append(cc.bounds.min.y.ToString("0.00")).Append("..").Append(cc.bounds.max.y.ToString("0.00"))
                            .Append(" paths ").Append(cc.pathCount).Append(" boxes ").Append(cc.GetComponents<BoxCollider2D>().Length);
                    report.AppendLine();
                }
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith("3D") || root.name.StartsWith("3D Obstacle")) continue;
                foreach (var r in root.GetComponentsInChildren<UnityEngine.Renderer>(true))
                {
                    report.Append(r.name).Append(r.gameObject.activeInHierarchy ? "" : " (hidden)")
                        .Append("  centre ").Append(r.bounds.center.ToString("0.0"))
                        .Append("  size ").Append(r.bounds.size.ToString("0.0"));
                    foreach (var m in r.sharedMaterials)
                    {
                        report.Append("  | ");
                        if (m == null) { report.Append("none"); continue; }
                        report.Append(m.name).Append(" / ").Append(m.shader.name);
                        if (m.HasProperty("_BaseColor")) report.Append(" / colour ").Append(m.GetColor("_BaseColor"));
                        if (m.HasProperty("_BaseMap")) report.Append(m.GetTexture("_BaseMap") != null ? " / textured" : " / no texture");
                        if (m.HasProperty("_Surface")) report.Append(" / surface ").Append(m.GetFloat("_Surface"));
                    }
                    report.AppendLine();
                }
            }
            File.WriteAllText(PreviewFolder + "/set_pieces.txt", report.ToString());
            Debug.Log("Pimba Stage: 3D set previews saved in " + Path.GetFullPath(PreviewFolder));
        }

        // =====================================================================================
        // Helpers
        // =====================================================================================

        private static void Dialog(string message) => EditorUtility.DisplayDialog("Pimba Stage", message, "OK");

        private static void Hide(UnityEngine.Renderer r, SetInfo info)
        {
            if (!r.gameObject.activeSelf) return;
            r.gameObject.SetActive(false);
            info.hidden.Add(r.name);
        }

        private static Material LitMaterial(string path, Color colour, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (shader != null && mat.shader != shader) mat.shader = shader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material UnlitMaterial(string path, Color colour)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (shader != null && mat.shader != shader) mat.shader = shader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A repeating copy of the stage's wood texture for the 3D floor (the sprite itself is left as it is).</summary>
        private static Texture2D WoodTexture()
        {
            if (!File.Exists(WoodTexturePath))
            {
                if (!File.Exists(SourceWoodSprite)) return null;
                AssetDatabase.CopyAsset(SourceWoodSprite, WoodTexturePath);
            }
            var ti = AssetImporter.GetAtPath(WoodTexturePath) as TextureImporter;
            if (ti != null && (ti.textureType != TextureImporterType.Default || ti.wrapMode != TextureWrapMode.Repeat ||
                               ti.filterMode != FilterMode.Point || ti.mipmapEnabled))
            {
                ti.textureType = TextureImporterType.Default;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(WoodTexturePath);
        }

        /// <summary>Depth of the front-most back wall at this x (NaN if there is none).</summary>
        private static float FrontWallZ(SetInfo info, float x)
        {
            float best = float.NaN;
            foreach (var r in info.walls)
            {
                Bounds b = r.bounds;
                if (x >= b.min.x && x <= b.max.x && (float.IsNaN(best) || b.min.z < best)) best = b.min.z;
            }
            return best;
        }

        /// <summary>Adds a Universal (3D) renderer to the project's URP asset (the 2D renderer stays the default).</summary>
        private static int EnsureForwardRenderer()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Debug.LogWarning("Pimba Stage: no URP asset found; the 3D set keeps the 2D renderer (flat colours).");
                return -1;
            }
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, RendererPath);
            }
            // Forward+ has no per-object light limit, so every spotlight shows on the big walls.
            if (data.renderingMode != UnityEngine.Rendering.Universal.RenderingMode.ForwardPlus)
            {
                data.renderingMode = UnityEngine.Rendering.Universal.RenderingMode.ForwardPlus;
                EditorUtility.SetDirty(data);
            }

            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray) return -1;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return i;

            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            return index;
        }

        private static void AddToBuildSettingsDisabled()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == Scene3DPath)) return;
            scenes.Add(new EditorBuildSettingsScene(Scene3DPath, false)); // unticked: the normal build is unchanged
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<UnityEngine.Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        private static Bounds Encapsulate(List<UnityEngine.Renderer> renderers)
        {
            if (renderers.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        /// <summary>The flattest, widest piece is the floor.</summary>
        private static UnityEngine.Renderer FindFloor(GameObject go)
        {
            UnityEngine.Renderer best = null;
            float bestArea = 0f;
            foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
            {
                Vector3 s = r.bounds.size;
                if (s.y > 0.05f * Mathf.Max(s.x, s.z)) continue;
                float area = s.x * s.z;
                if (area > bestArea) { bestArea = area; best = r; }
            }
            return best;
        }

        private static UnityEngine.Renderer FindTallest(GameObject go)
        {
            UnityEngine.Renderer best = null;
            foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
                if (best == null || r.bounds.size.y > best.bounds.size.y) best = r;
            return best;
        }
    }
}
