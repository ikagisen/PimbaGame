using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Platformer2D.EditorTools
{
    /// <summary>
    /// Menu: Tools > Platformer 2D > Build Level
    /// Generates the sprites it needs, then builds a complete, playable 2D level in
    /// Assets/Platformer2D/Scenes/Platformer2DLevel.unity.
    /// </summary>
    public static class PlatformerLevelBuilder2D
    {
        private const string RootFolder = "Assets/Platformer2D";
        private const string SpriteFolder = RootFolder + "/Sprites";
        private const string SceneFolder = RootFolder + "/Scenes";
        private const string ScenePath = SceneFolder + "/Platformer2DLevel.unity";
        private const string MaterialPath = RootFolder + "/PF2D_SpriteUnlit.mat";
        private const int IgnoreRaycastLayer = 2;

        private static readonly Color Sky = new Color(0.55f, 0.80f, 0.98f);
        private static readonly Color PlayerBlue = new Color(0.22f, 0.50f, 1.00f);
        private static readonly Color Ink = new Color(0.10f, 0.12f, 0.18f);
        private static readonly Color CoinGold = new Color(1.00f, 0.78f, 0.10f);
        private static readonly Color CoinShine = new Color(1.00f, 0.92f, 0.55f);
        private static readonly Color SpikeRed = new Color(0.86f, 0.16f, 0.16f);
        private static readonly Color PlatformOrange = new Color(1.00f, 0.56f, 0.16f);
        private static readonly Color BounceMagenta = new Color(0.90f, 0.32f, 0.86f);
        private static readonly Color Metal = new Color(0.82f, 0.84f, 0.88f);
        private static readonly Color FlagGrey = new Color(0.62f, 0.62f, 0.66f);
        private static readonly Color FlagGreen = new Color(0.22f, 0.86f, 0.32f);
        private static readonly Color GoalGold = new Color(1.00f, 0.84f, 0.20f);
        private static readonly Color HillGreen = new Color(0.60f, 0.84f, 0.62f);
        private static readonly Color CloudWhite = new Color(1f, 1f, 1f, 0.9f);

        private static Material spriteMat;
        private static Sprite square, circle, triangle, rounded, dirt, grass;

        [MenuItem("Tools/Platformer 2D/Build Level")]
        public static void BuildLevel()
        {
            if (System.IO.File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Rebuild 2D platformer level?",
                    "This replaces " + ScenePath + " with a freshly built level.", "Rebuild", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildLevelWithoutPrompts();
        }

        /// <summary>Same as the menu item, but never shows dialogs (safe to call from automation).</summary>
        public static void BuildLevelWithoutPrompts()
        {
            EnsureFolder("Assets", "Platformer2D");
            EnsureFolder(RootFolder, "Sprites");
            EnsureFolder(RootFolder, "Scenes");
            CreateSprites();
            spriteMat = CreateSpriteMaterial();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var level = new GameObject("Level").transform;
            var pickups = new GameObject("Coins").transform;

            // ---------- Player ----------
            GameObject player = CreatePlayer(new Vector2(0f, 0.7f));

            // ---------- Section 1: start ----------
            Ground("Wall Left", -3f, -2f, 8f, -6f, level);
            Ground("Ground Start", -2f, 12f, 0f, -6f, level);
            CreateCoin(new Vector2(4f, 0.8f), pickups);
            CreateCoin(new Vector2(6f, 0.8f), pickups);
            CreateCoin(new Vector2(8f, 0.8f), pickups);

            // ---------- Section 2: first gap + spikes ----------
            Ground("Ground Spikes", 15f, 26f, 0f, -6f, level);
            CreateSpikes(new Vector2(20.5f, 0f), 4, level);
            CreateCoin(new Vector2(19f, 2.2f), pickups);
            CreateCoin(new Vector2(20.5f, 3.0f), pickups);
            CreateCoin(new Vector2(22f, 2.2f), pickups);

            // ---------- Section 3: rising floating platforms ----------
            Ground("Step 1", 27.5f, 30.5f, 1.75f, 1.25f, level);
            Ground("Step 2", 31.5f, 34.5f, 3.25f, 2.75f, level);
            Ground("Step 3", 35.5f, 38.5f, 4.75f, 4.25f, level);
            CreateCoin(new Vector2(29f, 2.75f), pickups);
            CreateCoin(new Vector2(33f, 4.25f), pickups);
            CreateCoin(new Vector2(37f, 5.75f), pickups);

            // ---------- Section 4: checkpoint + moving platform over a pit ----------
            Ground("Ground Checkpoint", 40f, 48f, 3f, -6f, level);
            CreateCheckpoint(new Vector2(42f, 3f), level);
            CreateMovingPlatform(new Vector2(50f, 3f), new Vector2(7.5f, 0f), level);
            CreateCoin(new Vector2(52f, 4.4f), pickups);
            CreateCoin(new Vector2(54.5f, 4.4f), pickups);
            CreateCoin(new Vector2(57f, 4.4f), pickups);

            // ---------- Section 5: spikes, bounce pad, tall tower ----------
            Ground("Ground Bounce", 60f, 69f, 3f, -6f, level);
            CreateCheckpoint(new Vector2(61f, 3f), level);
            CreateSpikes(new Vector2(64f, 3f), 3, level);
            CreateBouncePad(new Vector2(67f, 3f), level);
            CreateCoin(new Vector2(67f, 7f), pickups);
            CreateCoin(new Vector2(67f, 10.5f), pickups);
            Ground("Tower", 69f, 75f, 10f, -6f, level);
            CreateCoin(new Vector2(71f, 10.9f), pickups);
            CreateCoin(new Vector2(73f, 10.9f), pickups);

            // ---------- Section 6: goal ----------
            Ground("Ground Goal", 79f, 89f, 10f, -6f, level);
            Ground("Wall Right", 89f, 90f, 16f, -6f, level);
            CreateCoin(new Vector2(81f, 10.9f), pickups);
            CreateCoin(new Vector2(83f, 10.9f), pickups);
            CreateGoal(new Vector2(87f, 10f), level);

            // ---------- Kill zone under the level ----------
            var killZone = new GameObject("Kill Zone");
            killZone.transform.SetParent(level, false);
            killZone.transform.position = new Vector3(45f, -12f, 0f);
            var killBox = killZone.AddComponent<BoxCollider2D>();
            killBox.isTrigger = true;
            killBox.size = new Vector2(300f, 2f);
            killZone.AddComponent<Hazard>();

            CreateBackground();

            // ---------- Game manager + camera ----------
            new GameObject("Game").AddComponent<PlatformerGame>();
            SetupCamera(scene, player.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = player;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log("2D platformer level built at " + ScenePath + ". Press Play!");
        }

        // ======================================================================
        // Pieces
        // ======================================================================

        private static GameObject CreatePlayer(Vector2 position)
        {
            var player = new GameObject("Player") { tag = "Player", layer = IgnoreRaycastLayer };
            player.transform.position = position;

            var body = player.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.freezeRotation = true;
            body.gravityScale = 3f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var capsule = player.AddComponent<CapsuleCollider2D>();
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.size = new Vector2(0.8f, 1.2f);
            capsule.offset = Vector2.zero;

            player.AddComponent<PlatformerPlayer>();

            var model = new GameObject("Model").transform;
            model.SetParent(player.transform, false);
            SpriteObj("Body", model, rounded, PlayerBlue, Vector2.zero, new Vector2(0.9f, 1.2f), 10);
            SpriteObj("Eye", model, circle, Color.white, new Vector2(0.17f, 0.24f), new Vector2(0.3f, 0.3f), 11);
            SpriteObj("Pupil", model, circle, Ink, new Vector2(0.23f, 0.24f), new Vector2(0.14f, 0.14f), 12);
            return player;
        }

        /// <summary>A solid block from xMin..xMax and bottom..top: tiled dirt with a grass strip on top.</summary>
        private static GameObject Ground(string name, float xMin, float xMax, float top, float bottom, Transform parent)
        {
            float width = xMax - xMin;
            float height = top - bottom;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((xMin + xMax) * 0.5f, (top + bottom) * 0.5f, 0f);
            go.isStatic = true;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dirt;
            if (spriteMat != null) sr.sharedMaterial = spriteMat;
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = new Vector2(width, height);
            sr.sortingOrder = 0;

            var box = go.AddComponent<BoxCollider2D>();
            box.offset = Vector2.zero;
            box.size = new Vector2(width, height);

            // Grass sprite: 0.25 units of solid grass + 0.125 units of blades sticking up.
            var cap = SpriteObj("Grass", go.transform, grass, Color.white, new Vector2(0f, height * 0.5f - 0.0625f), Vector2.one, 1);
            cap.drawMode = SpriteDrawMode.Tiled;
            cap.size = new Vector2(width, 0.375f);
            cap.gameObject.isStatic = true;
            return go;
        }

        private static void CreateCoin(Vector2 position, Transform parent)
        {
            var coin = new GameObject("Coin");
            coin.transform.SetParent(parent, false);
            coin.transform.position = position;
            var trigger = coin.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.4f;

            var visual = SpriteObj("Sprite", coin.transform, circle, CoinGold, Vector2.zero, new Vector2(0.6f, 0.6f), 3);
            SpriteObj("Shine", visual.transform, circle, CoinShine, Vector2.zero, new Vector2(0.55f, 0.55f), 4);
            coin.AddComponent<Coin>().visual = visual.transform;
        }

        private static void CreateSpikes(Vector2 groundPoint, int count, Transform parent)
        {
            const float spacing = 0.6f;
            float width = count * spacing;

            var spikes = new GameObject("Spikes");
            spikes.transform.SetParent(parent, false);
            spikes.transform.position = groundPoint;
            var trigger = spikes.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.offset = new Vector2(0f, 0.25f);
            trigger.size = new Vector2(width - 0.1f, 0.5f);
            spikes.AddComponent<Hazard>();

            for (int i = 0; i < count; i++)
            {
                float x = -width * 0.5f + spacing * 0.5f + i * spacing;
                SpriteObj("Spike", spikes.transform, triangle, SpikeRed, new Vector2(x, 0.3f), new Vector2(spacing, 0.6f), 2);
            }
        }

        private static void CreateCheckpoint(Vector2 groundPoint, Transform parent)
        {
            var checkpoint = new GameObject("Checkpoint");
            checkpoint.transform.SetParent(parent, false);
            checkpoint.transform.position = groundPoint;
            var trigger = checkpoint.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.offset = new Vector2(0f, 1.25f);
            trigger.size = new Vector2(1f, 2.5f);

            SpriteObj("Pole", checkpoint.transform, square, Metal, new Vector2(0f, 1.1f), new Vector2(0.1f, 2.2f), 2);
            var flag = SpriteObj("Flag", checkpoint.transform, triangle, FlagGrey, new Vector2(0.35f, 1.95f), new Vector2(0.5f, 0.6f), 2);
            flag.transform.localRotation = Quaternion.Euler(0f, 0f, -90f); // point right

            var component = checkpoint.AddComponent<Checkpoint>();
            component.flag = flag;
            component.activeColor = FlagGreen;
        }

        private static void CreateMovingPlatform(Vector2 position, Vector2 travel, Transform parent)
        {
            var platform = new GameObject("Moving Platform");
            platform.transform.SetParent(parent, false);
            platform.transform.position = position;
            platform.transform.localScale = new Vector3(3f, 0.5f, 1f);

            var sr = platform.AddComponent<SpriteRenderer>();
            sr.sprite = square;
            sr.color = PlatformOrange;
            if (spriteMat != null) sr.sharedMaterial = spriteMat;

            var box = platform.AddComponent<BoxCollider2D>();
            box.size = Vector2.one;
            box.offset = Vector2.zero;

            var body = platform.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var mover = platform.AddComponent<MovingPlatform>();
            mover.travel = travel;
            mover.speed = 2.5f;
        }

        private static void CreateBouncePad(Vector2 groundPoint, Transform parent)
        {
            var pad = new GameObject("Bounce Pad");
            pad.transform.SetParent(parent, false);
            pad.transform.position = groundPoint;
            var trigger = pad.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.offset = new Vector2(0f, 0.25f);
            trigger.size = new Vector2(1.5f, 0.5f);

            SpriteObj("Base", pad.transform, square, Ink, new Vector2(0f, 0.08f), new Vector2(1.6f, 0.16f), 2);
            var spring = SpriteObj("Spring", pad.transform, rounded, BounceMagenta, new Vector2(0f, 0.24f), new Vector2(1.4f, 0.18f), 3);

            var component = pad.AddComponent<BouncePad>();
            component.bounceSpeed = 23f;
            component.visual = spring.transform;
        }

        private static void CreateGoal(Vector2 groundPoint, Transform parent)
        {
            var goal = new GameObject("Goal");
            goal.transform.SetParent(parent, false);
            goal.transform.position = groundPoint;
            var trigger = goal.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.offset = new Vector2(0f, 1.75f);
            trigger.size = new Vector2(1.4f, 3.5f);

            SpriteObj("Base", goal.transform, square, Metal, new Vector2(0f, 0.1f), new Vector2(0.7f, 0.2f), 2);
            SpriteObj("Pole", goal.transform, square, Metal, new Vector2(0f, 1.6f), new Vector2(0.12f, 3.2f), 2);
            SpriteObj("Top", goal.transform, circle, GoalGold, new Vector2(0f, 3.25f), new Vector2(0.3f, 0.3f), 3);

            var pivot = new GameObject("Flag Pivot").transform;
            pivot.SetParent(goal.transform, false);
            pivot.localPosition = new Vector3(0.06f, 2.8f, 0f);
            SpriteObj("Flag", pivot, square, GoalGold, new Vector2(0.5f, 0f), new Vector2(1f, 0.6f), 2);

            goal.AddComponent<GoalFlag>().flag = pivot;
        }

        private static void CreateBackground()
        {
            var background = new GameObject("Background").transform;

            var hills = new GameObject("Hills").transform;
            hills.SetParent(background, false);
            hills.gameObject.AddComponent<Parallax>().factor = 0.5f;
            for (int i = 0; i < 9; i++)
            {
                float radius = 5f + (i % 3) * 1.5f;
                SpriteObj("Hill", hills, circle, HillGreen, new Vector2(-15f + i * 9.5f, -3f), new Vector2(radius * 2f, radius * 2f), -20);
            }

            var clouds = new GameObject("Clouds").transform;
            clouds.SetParent(background, false);
            clouds.gameObject.AddComponent<Parallax>().factor = 0.85f;
            for (int i = 0; i < 7; i++)
            {
                var cloud = new GameObject("Cloud").transform;
                cloud.SetParent(clouds, false);
                cloud.localPosition = new Vector3(-12f + i * 7f, 6f + (i % 3) * 1.2f, 0f);
                SpriteObj("Puff", cloud, circle, CloudWhite, Vector2.zero, new Vector2(2.2f, 1.6f), -30);
                SpriteObj("Puff", cloud, circle, CloudWhite, new Vector2(-1f, -0.2f), new Vector2(1.6f, 1.2f), -30);
                SpriteObj("Puff", cloud, circle, CloudWhite, new Vector2(1.1f, -0.2f), new Vector2(1.7f, 1.3f), -30);
            }
        }

        private static void SetupCamera(Scene scene, Transform player)
        {
            Camera cam = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                cam = root.GetComponent<Camera>();
                if (cam != null) break;
            }
            if (cam == null)
            {
                var camObject = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camObject.AddComponent<Camera>();
                camObject.AddComponent<AudioListener>();
            }

            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sky;
            cam.transform.position = new Vector3(player.position.x, player.position.y + 1.5f, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.gameObject.AddComponent<PlatformerCamera>().target = player;
        }

        // ======================================================================
        // Helpers
        // ======================================================================

        private static SpriteRenderer SpriteObj(string name, Transform parent, Sprite sprite, Color color,
            Vector2 localPosition, Vector2 localScale, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(localScale.x, localScale.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            if (spriteMat != null) sr.sharedMaterial = spriteMat;
            return sr;
        }

        /// <summary>Unlit sprite material, so everything shows at full colour without needing 2D lights.</summary>
        private static Material CreateSpriteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null; // fall back to the pipeline's default sprite material

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        private static void CreateSprites()
        {
            square = MakeSprite("PF2D_Square", 16, 16, 16f, FilterMode.Point, (x, y) => Color.white);

            circle = MakeSprite("PF2D_Circle", 128, 128, 128f, FilterMode.Bilinear, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64f, 64f));
                return new Color(1f, 1f, 1f, Mathf.Clamp01(63.5f - d));
            });

            triangle = MakeSprite("PF2D_Triangle", 128, 128, 128f, FilterMode.Bilinear, (x, y) =>
            {
                float halfWidth = 64f * (1f - (y + 0.5f) / 128f);
                float dx = Mathf.Abs(x + 0.5f - 64f);
                return new Color(1f, 1f, 1f, Mathf.Clamp01((halfWidth - dx) * 0.9f));
            });

            rounded = MakeSprite("PF2D_Rounded", 64, 64, 64f, FilterMode.Bilinear, (x, y) =>
            {
                const float half = 31.5f, radius = 14f;
                float qx = Mathf.Abs(x + 0.5f - 32f) - (half - radius);
                float qy = Mathf.Abs(y + 0.5f - 32f) - (half - radius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                                + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - outside));
            });

            dirt = MakeSprite("PF2D_Dirt", 32, 32, 32f, FilterMode.Point, (x, y) =>
            {
                float n = Hash(x / 2, y / 2);
                Color baseColor = new Color(0.53f, 0.37f, 0.24f);
                if (n > 0.86f) return new Color(0.42f, 0.28f, 0.18f);
                if (n < 0.08f) return new Color(0.63f, 0.46f, 0.31f);
                float shade = (Hash(x / 4 + 31, y / 4 + 17) - 0.5f) * 0.05f;
                return new Color(baseColor.r + shade, baseColor.g + shade, baseColor.b + shade);
            });

            grass = MakeSprite("PF2D_Grass", 32, 12, 32f, FilterMode.Point, (x, y) =>
            {
                Color mid = new Color(0.33f, 0.69f, 0.27f);
                Color light = new Color(0.47f, 0.82f, 0.36f);
                Color dark = new Color(0.24f, 0.52f, 0.20f);
                if (y == 0) return dark;
                if (y < 6) return mid;
                if (y < 8) return light;
                int bladeHeight = (int)(Hash(x, 7) * 4.99f);
                return (y - 8) < bladeHeight ? light : new Color(0f, 0f, 0f, 0f);
            });
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 1023u) / 1023f;
        }

        private static Sprite MakeSprite(string name, int width, int height, float pixelsPerUnit, FilterMode filter,
            System.Func<int, int, Color> pixel)
        {
            string path = SpriteFolder + "/" + name + ".png";

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel(x, y);
            tex.SetPixels(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = pixelsPerUnit;
                importer.filterMode = filter;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; // required for tiled ground
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
