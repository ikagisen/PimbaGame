using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PimbaStage.EditorTools
{
    /// <summary>
    /// Menu: Tools > Pimba Stage > Build Stage Scene
    /// Builds the whole 2.5D theater scene from the design sketch: perspective stage with depth,
    /// curtains, a row of stage lights, the artist banner, hearts for both players, and the
    /// arrow lane with the musician on the right. All art is generated, so there is nothing to import.
    /// </summary>
    public static class PimbaStageBuilder
    {
        private const string Root = "Assets/PimbaStage";
        private const string SpriteFolder = Root + "/Sprites";
        private const string SceneFolder = Root + "/Scenes";
        private const string SongFolder = Root + "/Songs";
        private const string ScenePath = SceneFolder + "/PimbaStage.unity";
        private const string MaterialPath = Root + "/PS_SpriteUnlit.mat";
        private const string DemoSongPath = SongFolder + "/Demo - Arraial na Aldeia.asset";
        private const string ButtonFolder = Root + "/UI";
        private const string CharacterFolder = Root + "/Characters";
        private const string Scene3DPath = SceneFolder + "/PimbaStage3D.unity";
        private const string AudioFolder = Root + "/Audio";
        private static readonly Vector2 SingerPivot = new Vector2(0.5f, 0.5f);
        // The performer's animation frames are 34x34 px; his body is centred on column 18, feet on the bottom row.
        private static readonly Vector2 PerformerPivot = new Vector2(18f / 34f, 0f);

        private const int UILayer = 5;
        private const int IgnoreRaycastLayer = 2;
        private const float PlayfieldWidth = 0.76f;  // left part of the screen; the arrow lane is on the right
        private const float CurtainWidth = 0.065f;   // fraction of the playfield covered by each side curtain
        private const float StageDepth = 6.5f;
        private const float CameraStartX = 6.5f;
        private const float CameraEndX = 139f;

        private static readonly Color WoodFront = new Color(0.55f, 0.36f, 0.22f);
        private static readonly Color WoodTop = new Color(0.80f, 0.58f, 0.38f);
        private static readonly Color CrateColor = new Color(0.86f, 0.62f, 0.38f);
        private static readonly Color PieceColor = new Color(0.95f, 0.55f, 0.2f);
        private static readonly Color RopeColor = new Color(0.78f, 0.7f, 0.52f);
        private static readonly Color Dark = new Color(0.04f, 0.02f, 0.04f);

        private static Material spriteMat;
        private static Font font;
        private static bool customFont;
        private const string FontFolder = "Assets/PimbaStage/Fonts";
        private static Transform world;
        private static readonly List<DifficultyFilter> difficultyObjects = new List<DifficultyFilter>();
        private static Sprite sSquare, sCircle, sSoft, sTriangle, sArrow, sArrowOutline, sHeart, sCone, sCurtain, sValance,
            sWood, sCrate, sCardboard, sBackdrop, sFlame, sStar, sRounded, sFrame, sBellows, sSack;

        [MenuItem("Tools/Pimba Stage/Build Stage Scene")]
        public static void Build()
        {
            if (System.IO.File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Rebuild Pimba Stage?",
                    "This replaces " + ScenePath + " with a freshly built scene (your song assets are kept).", "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildWithoutPrompts();
        }

        /// <summary>
        /// Puts the red "lose" screen (UI/Results_Lose.png) with the GAME OVER layer on top (UI/Results_GameOver.png)
        /// into the open scene and the 3D version's scene, without rebuilding, so any changes made in the scene are kept.
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Update Lose Background (keeps scene edits)")]
        public static void ApplyLoseBackground()
        {
            if (StopIfPlaying()) return;
            if (LoadButtonSprite("Results_Lose.png", FilterMode.Bilinear) == null)
            {
                EditorUtility.DisplayDialog("Pimba Stage", "Put Results_Lose.png in " + ButtonFolder + " first.", "OK");
                return;
            }
            ApplyToScenes(SetLoseScreen, "lose screen", "results screen");
        }

        private static int SetLoseScreen(Scene scene)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var game in root.GetComponentsInChildren<StageGame>(true))
                {
                    Undo.RecordObject(game, "Set lose screen");
                    if (game.resultsBackground == null && game.resultsPanel != null)
                        game.resultsBackground = game.resultsPanel.GetComponent<Image>();
                    game.loseBackground = LoadButtonSprite("Results_Lose.png", FilterMode.Bilinear);
                    game.gameOverPanel = LoadButtonSprite("Results_GameOver.png");  // pixel art, kept sharp
                    game.gameOverPanelSize = 0.97f;  // nearly the whole red screen
                    EditorUtility.SetDirty(game);
                    count++;
                }
            return count;
        }

        /// <summary>
        /// Switches every UI text in the open scene to the font in Assets/PimbaStage/Fonts,
        /// without rebuilding the scene, so any changes made in the scene are kept.
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Apply Font (keeps scene edits)")]
        public static void ApplyFont()
        {
            if (StopIfPlaying()) return;
            var newFont = LoadFont();
            if (!customFont)
            {
                EditorUtility.DisplayDialog("Pimba Stage", "Put a .ttf or .otf font in " + FontFolder + " first.", "OK");
                return;
            }

            int texts = 0;
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var text in root.GetComponentsInChildren<Text>(true))
                {
                    Undo.RecordObject(text, "Apply font");
                    text.font = newFont;
                    text.fontStyle = FontStyle.Normal;
                    EditorUtility.SetDirty(text);
                    texts++;
                }
                foreach (var menu in root.GetComponentsInChildren<StageMenu>(true))
                {
                    Undo.RecordObject(menu, "Apply font");
                    menu.font = newFont; // used for the song list, which is made while playing
                    EditorUtility.SetDirty(menu);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("Pimba Stage: " + newFont.name + " applied to " + texts + " texts. Save the scene (Ctrl+S) to keep it.");
        }

        /// <summary>
        /// Gives Player 1 his idle / walk / jump / fall / hurt / double jump animations (Characters/PerformerIdle_0.png ...)
        /// in the open scene and in the 3D version's scene, without rebuilding, so scene edits are kept.
        /// A scene with no other unsaved changes is saved straight away.
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Apply Performer Animations (keeps scene edits)")]
        public static void ApplyPerformerAnimations()
        {
            if (StopIfPlaying()) return;
            if (LoadCharacterFrames("PerformerIdle", 20f, PerformerPivot).Length == 0)
            {
                EditorUtility.DisplayDialog("Pimba Stage", "Put the performer frames (PerformerIdle_0.png ...) in " + CharacterFolder + " first.", "OK");
                return;
            }

            ApplyToScenes(SetPerformerAnimations, "performer animations", "performer (Player 1)");
        }

        /// <summary>
        /// Player 2's art: the dance, a pose for each arrow and a miss pose (Characters/SingerIdle_0.png ...), the miss sound
        /// (Audio/Miss_1.wav ...), his platform (UI/SingerPlatform_0..2.png) and his skull health bar (UI/SingerHealth_0..11.png),
        /// in the open scene and the 3D version's scene, without rebuilding. A scene with no other unsaved changes is saved straight away.
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Apply P2 Singer Art (keeps scene edits)")]
        public static void ApplySingerAnimations()
        {
            if (StopIfPlaying()) return;
            if (LoadCharacterFrames("SingerIdle", 20f, SingerPivot).Length == 0)
            {
                EditorUtility.DisplayDialog("Pimba Stage", "Put the singer frames (SingerIdle_0.png ...) in " + CharacterFolder + " first.", "OK");
                return;
            }
            ApplyToScenes(SetSingerAnimations, "singer animations", "singer (Player 2)");
        }

        /// <summary>
        /// Replaces Player 1's row of hearts with the counter box (UI/HeartCounter_0..11.png) holding the yellow heart
        /// (UI/HeartYellow.png), in the open scene and the 3D version's scene, without rebuilding. Player 2's hearts stay as they are.
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Apply P1 Heart Counter (keeps scene edits)")]
        public static void ApplyPerformerHeartCounter()
        {
            if (StopIfPlaying()) return;
            if (LoadCharacterFrames("HeartCounter", 16f, SingerPivot, ButtonFolder).Length == 0 || LoadButtonSprite("HeartYellow.png") == null)
            {
                EditorUtility.DisplayDialog("Pimba Stage", "Put HeartCounter_0.png ... and HeartYellow.png in " + ButtonFolder + " first.", "OK");
                return;
            }
            ApplyToScenes(SetPerformerHeartCounter, "Player 1 heart counter", "Player 1 hearts");
        }

        private static int SetPerformerHeartCounter(Scene scene)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var game in root.GetComponentsInChildren<StageGame>(true))
                    if (SetPerformerHeartCounter(game)) count++;
            return count;
        }

        /// <summary>
        /// Puts the heart counter where Player 1's heart row is and hides the row (it is kept, switched off).
        /// The box is drawn at 5 UI units per pixel and the yellow heart at 3.75 (3/4 of that), centred in the box's left window.
        /// Returns false if the art or the heart row is missing.
        /// </summary>
        private static bool SetPerformerHeartCounter(StageGame game)
        {
            var frames = LoadCharacterFrames("HeartCounter", 16f, SingerPivot, ButtonFolder);
            var heartSprite = LoadButtonSprite("HeartYellow.png");
            if (frames.Length == 0 || heartSprite == null) return false;

            RectTransform row = null;
            if (game.performerHeartFills != null && game.performerHeartFills.Length > 0 && game.performerHeartFills[0] != null)
                row = game.performerHeartFills[0].transform.parent.parent as RectTransform;

            var counter = game.performerHeartCounter;
            if (counter == null)
            {
                if (row == null) return false;
                var box = new GameObject("Performer Heart Counter (P1)", typeof(RectTransform)).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(box.gameObject, "Heart counter");
                box.gameObject.layer = UILayer;
                box.SetParent(row.parent, false);
                box.SetSiblingIndex(row.GetSiblingIndex() + 1);
                box.anchorMin = box.anchorMax = row.anchorMin;
                box.pivot = row.pivot;
                box.anchoredPosition = row.anchoredPosition;
                var panelImage = box.gameObject.AddComponent<Image>();

                var heartRect = new GameObject("Yellow Heart", typeof(RectTransform)).GetComponent<RectTransform>();
                heartRect.gameObject.layer = UILayer;
                heartRect.SetParent(box, false);
                var heartImage = heartRect.gameObject.AddComponent<Image>();

                counter = box.gameObject.AddComponent<HeartCounter>();
                counter.panel = panelImage;
                counter.heart = heartImage;
            }
            else Undo.RecordObject(counter, "Heart counter");

            const float unitsPerPixel = 5f;
            var boxRect = (RectTransform)counter.transform;
            Undo.RecordObject(boxRect, "Heart counter");
            boxRect.sizeDelta = new Vector2(frames[0].rect.width, frames[0].rect.height) * unitsPerPixel;
            counter.frames = frames;
            counter.maxNumber = 3;
            counter.framesPerNumber = 4;
            counter.emptyFrame = 10;
            counter.frameTime = 0.13f;  // frame time from the GIF
            EditorUtility.SetDirty(counter);

            Undo.RecordObject(counter.panel, "Heart counter");
            counter.panel.sprite = frames[0];
            counter.panel.raycastTarget = false;
            EditorUtility.SetDirty(counter.panel);

            var hr = counter.heart.rectTransform;
            Undo.RecordObject(hr, "Heart counter");
            Undo.RecordObject(counter.heart, "Heart counter");
            hr.anchorMin = hr.anchorMax = new Vector2(0f, 0.5f);
            hr.pivot = new Vector2(0.5f, 0.5f);
            hr.anchoredPosition = new Vector2(9f * unitsPerPixel, 0f);  // middle of the window (pixel columns 3-14)
            hr.sizeDelta = new Vector2(heartSprite.rect.width, heartSprite.rect.height) * unitsPerPixel * 0.75f;
            counter.heart.sprite = heartSprite;
            counter.heart.color = Color.white;
            counter.heart.raycastTarget = false;
            EditorUtility.SetDirty(hr);
            EditorUtility.SetDirty(counter.heart);

            if (row != null && row.gameObject.activeSelf)
            {
                Undo.RecordObject(row.gameObject, "Heart counter");
                row.gameObject.SetActive(false);
            }
            Undo.RecordObject(game, "Heart counter");
            game.performerHeartCounter = counter;
            EditorUtility.SetDirty(game);
            return true;
        }

        private static readonly string[] DifficultyArtNames = { "Easy", "Normal", "Hard" };
        private const float DifficultyArtScale = 3f; // 51x24 px art shown at 153x72

        /// <summary>
        /// Draws the Options screen's EASY / NORMAL / HARD buttons (both players) with the pixel art in UI/Difficulty_*_On.png
        /// (coloured badge, shown on the chosen one) and UI/Difficulty_*_Off.png (just the text, shown on the others),
        /// in the open scene and the 3D version's scene, without rebuilding, so scene edits are kept.
        /// </summary>
        [MenuItem("Tools/Pimba Stage/Apply Difficulty Buttons (keeps scene edits)")]
        public static void ApplyDifficultyButtons()
        {
            if (StopIfPlaying()) return;
            if (System.Array.IndexOf(LoadDifficultyArt("On"), null) >= 0 || System.Array.IndexOf(LoadDifficultyArt("Off"), null) >= 0)
            {
                EditorUtility.DisplayDialog("Pimba Stage", "Put Difficulty_Easy_On.png, Difficulty_Easy_Off.png, Difficulty_Normal_On.png ... in " + ButtonFolder + " first.", "OK");
                return;
            }
            ApplyToScenes(SetDifficultyButtons, "difficulty buttons", "options menu");
        }

        private static Sprite[] LoadDifficultyArt(string state)
        {
            var sprites = new Sprite[DifficultyArtNames.Length];
            for (int i = 0; i < sprites.Length; i++)
                sprites[i] = LoadButtonSprite("Difficulty_" + DifficultyArtNames[i] + "_" + state + ".png");
            return sprites;
        }

        private static int SetDifficultyButtons(Scene scene)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var menu in root.GetComponentsInChildren<StageMenu>(true))
                    if (SetDifficultyButtons(menu)) count++;
            return count;
        }

        private static bool SetDifficultyButtons(StageMenu menu)
        {
            var chosen = LoadDifficultyArt("On");
            var plain = LoadDifficultyArt("Off");
            if (System.Array.IndexOf(chosen, null) >= 0 || System.Array.IndexOf(plain, null) >= 0) return false;

            Undo.RecordObject(menu, "Set difficulty buttons");
            menu.difficultyChosenArt = chosen;
            menu.difficultyPlainArt = plain;
            EditorUtility.SetDirty(menu);

            foreach (var row in new[] { menu.performerButtons, menu.musicianButtons })
            {
                if (row == null) continue;
                for (int i = 0; i < row.Length && i < chosen.Length; i++)
                {
                    var button = row[i];
                    if (button == null) continue;

                    // A whole-number size so the pixels stay square, centred where the old button was.
                    var rect = (RectTransform)button.transform;
                    Undo.RecordObject(rect, "Set difficulty buttons");
                    Vector2 centreAnchor = (rect.anchorMin + rect.anchorMax) * 0.5f;
                    Vector2 centreOffset = (rect.offsetMin + rect.offsetMax) * 0.5f;
                    rect.anchorMin = centreAnchor;
                    rect.anchorMax = centreAnchor;
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = chosen[i].rect.size * DifficultyArtScale;
                    rect.anchoredPosition = centreOffset;
                    EditorUtility.SetDirty(rect);

                    var image = button.targetGraphic as Image;
                    if (image != null)
                    {
                        Undo.RecordObject(image, "Set difficulty buttons");
                        image.sprite = i == 1 ? chosen[i] : plain[i]; // the game shows the saved choice when it starts
                        image.type = Image.Type.Simple;
                        image.preserveAspect = true;
                        image.color = Color.white;
                        image.raycastTarget = true; // the whole box is clickable, not only the letters
                        EditorUtility.SetDirty(image);
                    }

                    Undo.RecordObject(button, "Set difficulty buttons");
                    var colors = button.colors;
                    colors.normalColor = Color.white; // keep the art's colours exactly as drawn
                    button.colors = colors;
                    EditorUtility.SetDirty(button);

                    var label = button.GetComponentInChildren<Text>(true);
                    if (label != null && label.gameObject != button.gameObject)
                    {
                        Undo.RecordObject(label.gameObject, "Set difficulty buttons");
                        label.gameObject.SetActive(false); // the words are in the art
                    }
                }
            }
            return true;
        }

        /// <summary>Runs apply on every open scene and on the 3D version's scene, saving the ones it can.</summary>
        private static void ApplyToScenes(System.Func<Scene, int> apply, string what, string who)
        {
            var report = new List<string>();
            var openScenes = new List<Scene>();
            var hadOtherChanges = new List<bool>();
            bool open3D = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (scene.path == Scene3DPath) open3D = true;
                openScenes.Add(scene);
                hadOtherChanges.Add(scene.isDirty);
            }

            // The 3D version's scene first: opening and closing it can flag the open scene as changed,
            // so the open scene is saved after it.
            if (!open3D && System.IO.File.Exists(Scene3DPath))
            {
                var scene3D = EditorSceneManager.OpenScene(Scene3DPath, OpenSceneMode.Additive);
                if (apply(scene3D) > 0 && EditorSceneManager.SaveScene(scene3D))
                    report.Add(scene3D.name + " (saved)");
                EditorSceneManager.CloseScene(scene3D, true);
            }

            for (int i = 0; i < openScenes.Count; i++)
            {
                var scene = openScenes[i];
                if (apply(scene) == 0) continue;
                if (!hadOtherChanges[i] && !string.IsNullOrEmpty(scene.path) && EditorSceneManager.SaveScene(scene))
                    report.Insert(0, scene.name + " (saved)");
                else
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    report.Insert(0, scene.name + " (press Ctrl+S to keep it; it had other unsaved changes, so it wasn't saved for you)");
                }
            }

            Debug.Log(report.Count > 0
                ? "Pimba Stage: " + what + " set in " + string.Join(", ", report) + "."
                : "Pimba Stage: no " + who + " found. Open the PimbaStage scene first.");
        }

        private static int SetSingerAnimations(Scene scene)
        {
            RhythmLane lane = null;
            StageGame game = null;
            var singers = new List<SpriteFrameAnimator>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (lane == null) lane = root.GetComponentInChildren<RhythmLane>(true);
                if (game == null) game = root.GetComponentInChildren<StageGame>(true);
                foreach (var anim in root.GetComponentsInChildren<SpriteFrameAnimator>(true))
                    if (anim.GetComponent<Image>() != null && anim.name == "Character" &&
                        anim.transform.parent != null && anim.transform.parent.name == "Singer")
                        singers.Add(anim);  // the singer himself, not the platform next to him
            }
            int count = 0;
            foreach (var anim in singers)
                if (SetSingerAnimations(anim, lane))
                {
                    if (game != null) SetSingerStage(anim, game);
                    count++;
                }
            return count;
        }

        /// <summary>
        /// Player 2's stage: the singer stands on his platform (UI/SingerPlatform_0..2.png; its lights pulse on the beat),
        /// and his health is the upright skull bar to his right (UI/SingerHealth_0..11.png, one picture per segment left)
        /// instead of the three hearts. Everything is measured from the bottom of the panel, so it stays put on any screen;
        /// the platform's pole runs down to the bottom edge. The old podium and hearts are switched off, not deleted.
        /// </summary>
        private static void SetSingerStage(SpriteFrameAnimator singer, StageGame game)
        {
            var platformFrames = LoadCharacterFrames("SingerPlatform", 16f, SingerPivot, ButtonFolder);
            var healthLevels = LoadCharacterFrames("SingerHealth", 16f, SingerPivot, ButtonFolder);
            var stand = singer.transform.parent as RectTransform;
            var panel = stand != null ? stand.parent as RectTransform : null;
            var character = (RectTransform)singer.transform;
            if (panel == null || platformFrames.Length == 0 || healthLevels.Length == 0 || singer.frames == null || singer.frames.Length == 0) return;
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Singer stage");

            const float unitsPerPixel = 6f;      // singer and platform (the singer was 7 before; 6 makes room for the platform)
            const float barUnitsPerPixel = 2.25f; // health bar
            const float feet = 184.5f;           // the singer's feet (top of the platform), above the bottom of the panel
            const int dishRows = 13;             // the platform's dish and lights; the pole starts under it
            var barSize = new Vector2(healthLevels[0].rect.width, healthLevels[0].rect.height) * barUnitsPerPixel;

            // The stand now sits on the bottom of the panel, so all heights below are from the panel's bottom edge.
            stand.anchorMin = stand.anchorMax = new Vector2(0.5f, 0f);
            stand.pivot = new Vector2(0.5f, 0f);
            stand.anchoredPosition = Vector2.zero;
            float centre = stand.rect.width * 0.5f;

            // The singer stands on the dish.
            var singerSize = new Vector2(singer.frames[0].rect.width, singer.frames[0].rect.height) * unitsPerPixel;
            character.sizeDelta = singerSize;
            character.anchoredPosition = new Vector2(centre, feet + singerSize.y * 0.5f);
            var spot = stand.Find("Spotlight") as RectTransform;
            if (spot != null) spot.anchoredPosition = new Vector2(centre, feet + singerSize.y * 0.5f);
            foreach (var old in new[] { "Podium", "Podium Trim" })
            {
                var t = stand.Find(old);
                if (t != null) t.gameObject.SetActive(false);
            }

            // The platform, just behind the singer; its top edge is at his feet.
            var platform = stand.Find("Platform") as RectTransform;
            if (platform == null)
            {
                platform = new GameObject("Platform", typeof(RectTransform)).GetComponent<RectTransform>();
                platform.gameObject.layer = UILayer;
                platform.SetParent(stand, false);
                platform.gameObject.AddComponent<Image>();
                platform.gameObject.AddComponent<SpriteFrameAnimator>();
            }
            var strayPoses = platform.GetComponent<SingerPoses>();  // an earlier version mistook the platform for the singer
            if (strayPoses != null) Undo.DestroyObjectImmediate(strayPoses);
            platform.SetSiblingIndex(character.GetSiblingIndex());
            platform.anchorMin = platform.anchorMax = Vector2.zero;
            platform.pivot = new Vector2(0.5f, 1f);
            platform.sizeDelta = new Vector2(platformFrames[0].rect.width, platformFrames[0].rect.height) * unitsPerPixel;
            platform.anchoredPosition = new Vector2(centre, feet);
            var platformImage = platform.GetComponent<Image>();
            platformImage.sprite = platformFrames[0];
            platformImage.raycastTarget = false;
            var lights = platform.GetComponent<SpriteFrameAnimator>();
            lights.frames = platformFrames;
            lights.framesPerSecond = 1000f / 170f;          // frame time from the GIF, when no song is playing
            lights.framesPerBeat = platformFrames.Length;   // during a song: lit on the beat, then dimming
            lights.conductor = singer.conductor;

            // Text (rating, combo) draws over the singer, not under him.
            var rating = panel.Find("Rating");
            if (rating != null && stand.GetSiblingIndex() > rating.GetSiblingIndex()) stand.SetSiblingIndex(rating.GetSiblingIndex());

            // The skull health bar, standing upright to the right of the singer, centred on him and his dish.
            var bar = panel.Find("Singer Health Bar (P2)") as RectTransform;
            if (bar == null)
            {
                bar = new GameObject("Singer Health Bar (P2)", typeof(RectTransform)).GetComponent<RectTransform>();
                bar.gameObject.layer = UILayer;
                bar.SetParent(panel, false);
                bar.gameObject.AddComponent<Image>();
                bar.gameObject.AddComponent<HealthBarSprites>();
            }
            float dishBottom = feet - dishRows * unitsPerPixel;
            float barMiddle = (dishBottom + feet + singerSize.y) * 0.5f;
            bar.SetSiblingIndex(stand.GetSiblingIndex() + 1);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = barSize;
            bar.anchoredPosition = new Vector2(singerSize.x * 0.5f + 4f + barSize.x * 0.5f, barMiddle);
            var barImage = bar.GetComponent<Image>();
            barImage.sprite = healthLevels[healthLevels.Length - 1];
            barImage.raycastTarget = false;
            var health = bar.GetComponent<HealthBarSprites>();
            health.image = barImage;
            health.levels = healthLevels;

            if (game.musicianHeartFills != null && game.musicianHeartFills.Length > 0 && game.musicianHeartFills[0] != null)
                game.musicianHeartFills[0].transform.parent.parent.gameObject.SetActive(false);
            Undo.RecordObject(game, "Singer stage");
            game.musicianHealthBar = health;
            EditorUtility.SetDirty(game);
        }

        /// <summary>
        /// Hooks Player 2 up to his dance / arrow / miss frames and the miss sounds.
        /// The new frames are 34x32 px (the old ones were 34x23), so the picture grows upwards from the podium at the same 7x size.
        /// Returns false if the idle frames are missing.
        /// </summary>
        private static bool SetSingerAnimations(SpriteFrameAnimator anim, RhythmLane lane)
        {
            var idle = LoadCharacterFrames("SingerIdle", 20f, SingerPivot);
            if (idle.Length == 0) return false;

            var poses = anim.GetComponent<SingerPoses>();
            if (poses == null) poses = Undo.AddComponent<SingerPoses>(anim.gameObject);
            else Undo.RecordObject(poses, "Singer animations");
            poses.lane = lane;
            poses.idleFrames = idle;
            poses.leftFrames = LoadCharacterFrames("SingerLeft", 20f, SingerPivot);
            poses.downFrames = LoadCharacterFrames("SingerDown", 20f, SingerPivot);
            poses.upFrames = LoadCharacterFrames("SingerUp", 20f, SingerPivot);
            poses.rightFrames = LoadCharacterFrames("SingerRight", 20f, SingerPivot);
            poses.missFrames = LoadCharacterFrames("SingerMiss", 20f, SingerPivot);
            poses.idleFramesPerBeat = 4;
            poses.idleFps = 1000f / 140f;  // frame time from the GIF
            poses.poseFps = 1000f / 140f;
            poses.missSounds = LoadMissSounds();
            EditorUtility.SetDirty(poses);

            Undo.RecordObject(anim, "Singer animations");
            anim.frames = idle;
            anim.framesPerSecond = poses.idleFps;
            anim.framesPerBeat = poses.idleFramesPerBeat;
            EditorUtility.SetDirty(anim);

            var img = anim.GetComponent<Image>();
            Undo.RecordObject(img, "Singer animations");
            img.sprite = idle[0];
            img.preserveAspect = true;
            EditorUtility.SetDirty(img);

            var rt = (RectTransform)anim.transform;
            float height = rt.rect.width * idle[0].rect.height / idle[0].rect.width;
            if (Mathf.Abs(rt.rect.height - height) > 0.5f)
            {
                Undo.RecordObject(rt, "Singer animations");
                float bottom = rt.anchoredPosition.y - rt.pivot.y * rt.rect.height;
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, bottom + rt.pivot.y * height);
                EditorUtility.SetDirty(rt);
            }
            return true;
        }

        /// <summary>Loads Audio/Miss_1, Miss_2 ... (.wav, .ogg or .mp3).</summary>
        private static AudioClip[] LoadMissSounds()
        {
            var clips = new List<AudioClip>();
            for (int i = 1; i <= 16; i++)
            {
                AudioClip clip = null;
                foreach (var ext in new[] { ".wav", ".ogg", ".mp3" })
                {
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + "/Miss_" + i + ext);
                    if (clip != null) break;
                }
                if (clip == null) break;
                clips.Add(clip);
            }
            return clips.ToArray();
        }

        private static int SetPerformerAnimations(Scene scene)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var player in root.GetComponentsInChildren<StagePlayer>(true))
                    if (SetPerformerAnimations(player)) count++;
            return count;
        }

        /// <summary>Hooks Player 1 up to his animation frames. Returns false if the idle frames are missing.</summary>
        private static bool SetPerformerAnimations(StagePlayer player)
        {
            var idle = LoadCharacterFrames("PerformerIdle", 20f, PerformerPivot);
            if (idle.Length == 0) return false;

            Undo.RecordObject(player, "Performer animations");
            player.idleFrames = idle;
            player.walkFrames = LoadCharacterFrames("PerformerWalk", 20f, PerformerPivot);
            player.jumpFrames = LoadCharacterFrames("PerformerJump", 20f, PerformerPivot);  // going up (2 frames)
            player.fallFrames = LoadCharacterFrames("PerformerFall", 20f, PerformerPivot);  // going down (2 frames)
            player.hurtFrames = LoadCharacterFrames("PerformerHurt", 20f, PerformerPivot);
            player.doubleJumpFrames = LoadCharacterFrames("PerformerDoubleJump", 20f, PerformerPivot);
            player.idleFps = 1000f / 170f;  // frame times from the GIFs
            player.walkFps = 1000f / 170f;
            player.jumpFps = 10f;
            player.hurtFps = 1000f / 140f;
            player.doubleJumpFps = 1000f / 130f;
            EditorUtility.SetDirty(player);

            var anim = player.animator;
            if (anim != null)
            {
                Undo.RecordObject(anim, "Performer animations");
                anim.frames = idle;
                anim.framesPerSecond = player.idleFps;
                EditorUtility.SetDirty(anim);
                var art = anim.GetComponent<SpriteRenderer>();
                if (art != null)
                {
                    Undo.RecordObject(art, "Performer animations");
                    art.sprite = idle[0];
                    EditorUtility.SetDirty(art);
                }
            }
            return true;
        }

        private static bool StopIfPlaying()
        {
            if (!EditorApplication.isPlaying) return false;
            EditorUtility.DisplayDialog("Pimba Stage", "Stop Play mode first, then run this again.", "OK");
            return true;
        }

        public static void BuildWithoutPrompts()
        {
            EnsureFolder("Assets", "PimbaStage");
            EnsureFolder(Root, "Sprites");
            EnsureFolder(Root, "Scenes");
            EnsureFolder(Root, "Songs");
            EnsureFolder(Root, "Charts");

            CreateSprites();
            spriteMat = CreateMaterial();
            font = LoadFont();
            var songs = LoadSongs();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            world = new GameObject("Stage").transform;
            difficultyObjects.Clear();

            BuildBackground();
            BuildLevel();
            StagePlayer player = CreatePlayer(new Vector2(2f, 0.8f));

            // Camera (perspective = 2.5D depth), only on the left part of the screen
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.orthographic = false;
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.03f, 0.05f);
            cam.rect = new UnityEngine.Rect(0f, 0f, PlayfieldWidth, 1f);
            camGo.transform.position = new Vector3(CameraStartX, 3.8f, -20f);
            var scroller = camGo.AddComponent<StageScroller>();
            scroller.cam = cam;
            scroller.player = player;
            scroller.startX = CameraStartX;
            scroller.endX = CameraEndX;
            scroller.leftInset = CurtainWidth + 0.005f;
            scroller.rightInset = CurtainWidth + 0.005f;

            // Game systems
            var gameGo = new GameObject("Pimba Stage Game");
            gameGo.AddComponent<AudioSource>();
            var conductor = gameGo.AddComponent<Conductor>();
            var game = gameGo.AddComponent<StageGame>();
            game.songs = songs;
            game.conductor = conductor;
            game.scroller = scroller;
            game.player = player;
            game.difficultyObjects = difficultyObjects.ToArray();

            // Mouse / keyboard / gamepad for the menus (Input System)
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
            eventSystem.AddComponent<MenuInputSetup>();

            BuildUI(game);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = gameGo;
            Debug.Log("Pimba Stage built at " + ScenePath + ". Press Play! Player 1: A/D + W/Space. Player 2: arrow keys.");
        }

        // ======================================================================
        // World
        // ======================================================================

        private static void BuildBackground()
        {
            Sized("Painted Backdrop", world, sBackdrop, Color.white, new Vector3(80f, 8f, StageDepth + 0.2f), new Vector2(200f, 16f), -100, SpriteDrawMode.Tiled);

            // Black masking flats (wings) give the stage depth
            for (float x = 4f; x < 170f; x += 22f)
                SR("Wing", world, sCurtain, new Color(0.32f, 0.2f, 0.26f), new Vector3(x, 4.75f, 3.5f), new Vector2(1.1f, 2.4f), -80);

            // Festival bunting with little lights (a Portuguese "arraial")
            var bunting = new GameObject("Bunting").transform;
            bunting.SetParent(world, false);
            Color[] flagColors =
            {
                new Color(0.9f, 0.2f, 0.2f), new Color(1f, 0.85f, 0.2f), new Color(0.2f, 0.7f, 0.3f),
                new Color(0.25f, 0.5f, 0.95f), new Color(0.95f, 0.95f, 0.95f)
            };
            int flag = 0;
            for (float x0 = -16f; x0 < 176f; x0 += 12f)
            {
                const float length = 12f, topY = 6.3f, sag = 0.8f, z = 5f;
                Vector3 previous = Vector3.zero;
                int k = 0;
                for (float s = 0f; s <= length + 0.001f; s += 1f, k++)
                {
                    float u = s / length * 2f - 1f;
                    var p = new Vector3(x0 + s, topY - sag * (1f - u * u), z);
                    if (k > 0)
                    {
                        Vector3 d = p - previous;
                        var rope = SR("Rope", bunting, sSquare, new Color(0.2f, 0.15f, 0.15f), (p + previous) * 0.5f, new Vector2(d.magnitude, 0.035f), -76);
                        rope.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    }
                    if (k > 0 && s < length - 0.01f)
                    {
                        var f = SR("Flag", bunting, sTriangle, flagColors[flag++ % flagColors.Length], p + new Vector3(0f, -0.3f, 0f), new Vector2(0.5f, 0.55f), -75);
                        f.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
                    }
                    if (k % 2 == 1) SR("Bulb", bunting, sSoft, new Color(1f, 0.85f, 0.5f, 0.9f), p + new Vector3(0f, -0.05f, -0.01f), new Vector2(0.35f, 0.35f), -74);
                    previous = p;
                }
            }

            // Darkness under the stage, seen through the trapdoors
            SR("Under Stage", world, sSquare, Dark, new Vector3(80f, -1.6f, 0.1f), new Vector2(200f, 3.2f), -35);
            var pit = SR("Trapdoor Darkness", world, sSquare, Dark, new Vector3(80f, -0.3f, StageDepth * 0.5f), new Vector2(200f, StageDepth), -55);
            pit.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static void BuildLevel()
        {
            // Stage floor with trapdoor gaps
            float[,] floors = { { -14f, 22f }, { 25f, 40f }, { 43.5f, 58f }, { 64f, 74f }, { 77f, 90f }, { 92.5f, 104f }, { 108f, 118f }, { 126f, 172f } };
            for (int i = 0; i < floors.GetLength(0); i++) Floor(floors[i, 0], floors[i, 1]);

            // 1. Warm-up
            Crate(8f, 0f, 1.4f, 1.2f);
            Crate(13.5f, 0f, 2f, 1.8f);
            Crate(18.5f, 0f, 1.5f, 1f);
            Rose(8f, 2f);
            Rose(13.5f, 2.9f);
            Rose(18.5f, 2f);

            // 2. Cardboard town (the tall blocks from the sketch) + stage fire
            CardboardFlat(28f, 2.4f, 1.6f, new Color(0.95f, 0.85f, 0.7f));
            CardboardFlat(31.8f, 3f, 3.2f, new Color(0.85f, 0.9f, 1f));
            NotOnEasy(Flames(35.4f, 1.4f));
            CardboardFlat(38.4f, 2f, 1.8f, new Color(1f, 0.85f, 0.85f));
            Rose(31.8f, 4.3f);

            // 3. Sandbag + crate stairs
            Sandbag(48f, 8.4f, 0f);
            Crate(52.5f, 0f, 1.5f, 1f);
            Crate(54f, 0f, 1.5f, 2f);
            Crate(55.5f, 0f, 1.5f, 3f);
            Rose(55.5f, 4.1f);

            // 4. Cart over a big trapdoor
            MovingPiece(59.4f, 1f, 2.4f, 0.5f, new Vector2(3.2f, 0f), 1.6f, true);
            Rose(61f, 2.6f);
            Flames(67f, 1.6f);
            CardboardFlat(70.5f, 2.5f, 2.4f, new Color(0.9f, 1f, 0.85f));
            Rose(70.5f, 3.6f);

            // 5. Elevator up to a tall flat, then a sandbag
            MovingPiece(79f, 0.25f, 2f, 0.5f, new Vector2(0f, 3f), 1.2f, false);
            CardboardFlat(83.5f, 3f, 3.8f, new Color(1f, 0.92f, 0.75f));
            Rose(83.5f, 5f);
            NotOnEasy(Sandbag(87.5f, 8.4f, 0.35f));

            // 6. Fire and crates
            Flames(95f, 1.2f);
            Crate(97.2f, 0f, 1.2f, 1.3f);
            NotOnEasy(Flames(99.4f, 1.2f));
            Crate(102f, 0f, 1.6f, 0.9f);
            Rose(97.2f, 2.5f);

            // 7. Hanging crate over a trapdoor, sandbag
            HangingCrate(106f, 1.2f, 1.4f, 0.6f);
            NotOnEasy(Sandbag(111.5f, 8.4f, 0.6f));
            Crate(115f, 0f, 2f, 1.6f);
            Rose(115f, 2.8f);

            // 8. Long cart ride, then the finale spotlight
            MovingPiece(119.6f, 0.9f, 2.6f, 0.5f, new Vector2(4.6f, 0f), 2f, true);
            Rose(122f, 2.6f);
            Goal(138f);
            CardboardFlat(147f, 2.4f, 9f, new Color(0.7f, 0.6f, 0.65f));

            // Player 1 EASY: planks over the smaller trapdoors
            OnlyOnEasy(Bridge(22f, 25f));
            OnlyOnEasy(Bridge(40f, 43.5f));
            OnlyOnEasy(Bridge(74f, 77f));
            OnlyOnEasy(Bridge(90f, 92.5f));

            // Player 1 HARD: extra fire and sandbags
            OnlyOnHard(Flames(16f, 1.1f));
            OnlyOnHard(Flames(45.5f, 1.2f));
            OnlyOnHard(Sandbag(61.5f, 8.4f, 0.5f));
            OnlyOnHard(Flames(109.8f, 1.2f));
            OnlyOnHard(Flames(130f, 1.4f));
        }

        private static void NotOnEasy(GameObject go) => Filter(go, false, true, true);
        private static void OnlyOnEasy(GameObject go) => Filter(go, true, false, false);
        private static void OnlyOnHard(GameObject go) => Filter(go, false, false, true);

        private static void Filter(GameObject go, bool easy, bool normal, bool hard)
        {
            var filter = go.AddComponent<DifficultyFilter>();
            filter.onEasy = easy;
            filter.onNormal = normal;
            filter.onHard = hard;
            difficultyObjects.Add(filter);
        }

        private static GameObject Bridge(float xMin, float xMax)
        {
            float w = xMax - xMin + 0.4f;
            var plank = Sized("Plank Bridge (Easy)", world, sWood, new Color(0.75f, 0.52f, 0.3f), new Vector3((xMin + xMax) * 0.5f, -0.12f, -0.02f),
                new Vector2(w, 0.24f), -28, SpriteDrawMode.Tiled);
            var box = plank.gameObject.AddComponent<BoxCollider2D>();
            box.offset = Vector2.zero;
            box.size = new Vector2(w, 0.24f);
            var top = Sized("Top", plank.transform, sWood, new Color(0.85f, 0.62f, 0.38f), new Vector3(0f, 0.12f, 0.8f), new Vector2(w, 1.6f), -47, SpriteDrawMode.Tiled);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return plank.gameObject;
        }

        private static void Floor(float xMin, float xMax)
        {
            float w = xMax - xMin;
            var front = Sized("Stage Floor", world, sWood, WoodFront, new Vector3((xMin + xMax) * 0.5f, -1.5f, 0f), new Vector2(w, 3f), -30, SpriteDrawMode.Tiled);
            var box = front.gameObject.AddComponent<BoxCollider2D>();
            box.offset = Vector2.zero;
            box.size = new Vector2(w, 3f);

            var top = Sized("Floor Top", front.transform, sWood, WoodTop, new Vector3(0f, 1.5f, StageDepth * 0.5f), new Vector2(w, StageDepth), -50, SpriteDrawMode.Tiled);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SR("Edge", front.transform, sSquare, new Color(0.3f, 0.18f, 0.1f), new Vector3(0f, 1.44f, -0.01f), new Vector2(w, 0.12f), -29);
        }

        private static GameObject Crate(float cx, float bottom, float w, float h)
        {
            var sr = Sized("Crate", world, sCrate, CrateColor, new Vector3(cx, bottom + h * 0.5f, 0f), new Vector2(w, h), -20, SpriteDrawMode.Sliced);
            var box = sr.gameObject.AddComponent<BoxCollider2D>();
            box.offset = Vector2.zero;
            box.size = new Vector2(w, h);
            var top = Sized("Top", sr.transform, sWood, new Color(0.7f, 0.5f, 0.3f), new Vector3(0f, h * 0.5f, 0.5f), new Vector2(w, 1f), -45, SpriteDrawMode.Tiled);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return sr.gameObject;
        }

        private static void HangingCrate(float cx, float bottom, float w, float h)
        {
            var crate = Crate(cx, bottom, w, h);
            crate.name = "Hanging Crate";
            for (int side = -1; side <= 1; side += 2)
                SR("Rope", crate.transform, sSquare, RopeColor, new Vector3(side * w * 0.38f, h * 0.5f + 5f, 0.01f), new Vector2(0.05f, 10f), -21);
        }

        private static void CardboardFlat(float cx, float w, float h, Color tint)
        {
            var sr = Sized("Cardboard Flat", world, sCardboard, tint, new Vector3(cx, h * 0.5f, 0f), new Vector2(w, h), -22, SpriteDrawMode.Sliced);
            var box = sr.gameObject.AddComponent<BoxCollider2D>();
            box.offset = Vector2.zero;
            box.size = new Vector2(w, h);
            var top = Sized("Top", sr.transform, sCardboard, tint * 0.85f, new Vector3(0f, h * 0.5f, 0.35f), new Vector2(w, 0.7f), -45, SpriteDrawMode.Sliced);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Painted windows
            int cols = Mathf.Max(1, Mathf.FloorToInt((w - 0.3f) / 0.75f));
            int index = 0;
            for (float y = h - 0.65f; y > 0.55f; y -= 0.9f)
            {
                for (int c = 0; c < cols; c++, index++)
                {
                    float x = -w * 0.5f + (c + 0.5f) * (w / cols);
                    bool lit = Hash(Mathf.RoundToInt(cx * 10f) + index, 4) > 0.4f;
                    SR("Window", sr.transform, sSquare, lit ? new Color(1f, 0.85f, 0.45f) : new Color(0.25f, 0.3f, 0.45f),
                        new Vector3(x, y - h * 0.5f, -0.01f), new Vector2(0.36f, 0.48f), -21);
                }
            }
        }

        private static GameObject Flames(float cx, float width)
        {
            var go = new GameObject("Stage Fire");
            go.transform.SetParent(world, false);
            go.transform.position = new Vector3(cx, 0f, 0f);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = new Vector2(0f, 0.4f);
            box.size = new Vector2(width - 0.2f, 0.8f);
            var hazard = go.AddComponent<StageHazard>();

            int n = Mathf.Max(2, Mathf.RoundToInt(width / 0.45f));
            var parts = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                float x = -width * 0.5f + (i + 0.5f) * width / n;
                float height = i % 2 == 0 ? 1f : 0.8f;
                var root = new GameObject("Flame").transform;
                root.SetParent(go.transform, false);
                root.localPosition = new Vector3(x, 0f, -0.01f * i);
                SR("Cutout", root, sFlame, Color.white, new Vector3(0f, height * 0.5f, 0f), new Vector2(width / n * 1.6f, height), -10);
                parts[i] = root;
            }
            hazard.flickerParts = parts;
            SR("Base", go.transform, sSquare, new Color(0.25f, 0.12f, 0.06f), new Vector3(0f, 0.06f, -0.05f), new Vector2(width, 0.12f), -9);
            return go;
        }

        private static GameObject Sandbag(float cx, float ropeLength, float phase)
        {
            var pivot = new GameObject("Swinging Sandbag");
            pivot.transform.SetParent(world, false);
            pivot.transform.position = new Vector3(cx, 9.5f, 0f);
            var body = pivot.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            var swing = pivot.AddComponent<SwingingSandbag>();
            swing.phase = phase;
            swing.amplitude = 50f;
            swing.period = 2.4f;

            SR("Rope", pivot.transform, sSquare, RopeColor, new Vector3(0f, -ropeLength * 0.5f, 0f), new Vector2(0.06f, ropeLength), -15);
            var bag = SR("Sandbag", pivot.transform, sSack, Color.white, new Vector3(0f, -ropeLength - 0.1f, 0f), new Vector2(1f, 1f), -9);
            var circle = bag.gameObject.AddComponent<CircleCollider2D>();
            circle.isTrigger = true;
            circle.radius = 0.38f;
            circle.offset = new Vector2(0f, -0.08f);
            bag.gameObject.AddComponent<StageHazard>();
            return pivot;
        }

        private static void MovingPiece(float cx, float cy, float w, float h, Vector2 travel, float speed, bool cart)
        {
            var sr = Sized(cart ? "Stage Cart" : "Stage Elevator", world, sCrate, PieceColor, new Vector3(cx, cy, 0f), new Vector2(w, h), -20, SpriteDrawMode.Sliced);
            var box = sr.gameObject.AddComponent<BoxCollider2D>();
            box.offset = Vector2.zero;
            box.size = new Vector2(w, h);
            var body = sr.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            var mover = sr.gameObject.AddComponent<MovingSetPiece>();
            mover.travel = travel;
            mover.speed = speed;

            var top = Sized("Top", sr.transform, sWood, new Color(0.85f, 0.5f, 0.25f), new Vector3(0f, h * 0.5f, 0.5f), new Vector2(w, 1f), -45, SpriteDrawMode.Tiled);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (cart)
            {
                for (int side = -1; side <= 1; side += 2)
                    SR("Wheel", sr.transform, sCircle, new Color(0.15f, 0.12f, 0.12f), new Vector3(side * w * 0.32f, -h * 0.5f, -0.01f), new Vector2(0.38f, 0.38f), -19);
            }
            else
            {
                for (int side = -1; side <= 1; side += 2)
                    SR("Cable", sr.transform, sSquare, RopeColor, new Vector3(side * w * 0.4f, h * 0.5f + 5f, 0.01f), new Vector2(0.05f, 10f), -21);
            }
        }

        private static void Rose(float x, float y)
        {
            var go = new GameObject("Rose");
            go.transform.SetParent(world, false);
            go.transform.position = new Vector3(x, y, 0f);
            var circle = go.AddComponent<CircleCollider2D>();
            circle.isTrigger = true;
            circle.radius = 0.45f;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var green = new Color(0.2f, 0.6f, 0.25f);
            SR("Stem", visual, sSquare, green, new Vector3(0f, -0.22f, 0f), new Vector2(0.07f, 0.5f), -5);
            var leaf = SR("Leaf", visual, sCircle, green, new Vector3(0.09f, -0.25f, 0f), new Vector2(0.2f, 0.1f), -5);
            leaf.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            SR("Bloom", visual, sCircle, new Color(0.85f, 0.08f, 0.2f), new Vector3(0f, 0.1f, 0f), new Vector2(0.34f, 0.34f), -4);
            SR("Bloom Light", visual, sCircle, new Color(1f, 0.35f, 0.45f), new Vector3(0.03f, 0.13f, -0.01f), new Vector2(0.15f, 0.15f), -3);
            go.AddComponent<Rose>().visual = visual;
        }

        private static void Goal(float x)
        {
            var go = new GameObject("Finale Spotlight");
            go.transform.SetParent(world, false);
            go.transform.position = new Vector3(x, 0f, 0f);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = new Vector2(0f, 1.5f);
            box.size = new Vector2(1.4f, 3f);

            var pool = SR("Light Pool", go.transform, sSoft, new Color(1f, 0.9f, 0.5f, 0.6f), new Vector3(0f, 0.02f, 1.2f), new Vector2(3.2f, 2.4f), -48);
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            SR("Beam", go.transform, sCone, new Color(1f, 0.95f, 0.7f, 0.35f), new Vector3(0f, 4f, 0.3f), new Vector2(3f, 4f), -60);
            SR("Glow", go.transform, sSoft, new Color(1f, 0.85f, 0.3f, 0.6f), new Vector3(0f, 2f, 0.05f), new Vector2(2.2f, 2.2f), -7);
            var star = SR("Star", go.transform, sStar, Color.white, new Vector3(0f, 2f, 0f), new Vector2(1.3f, 1.3f), -6);
            go.AddComponent<StageGoal>().star = star.transform;
        }

        private static StagePlayer CreatePlayer(Vector2 position)
        {
            var go = new GameObject("Performer (Player 1)") { tag = "Player", layer = IgnoreRaycastLayer };
            go.transform.position = position;
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.freezeRotation = true;
            body.gravityScale = 3f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var capsule = go.AddComponent<CapsuleCollider2D>();
            capsule.offset = Vector2.zero;

            var model = new GameObject("Model").transform;
            model.SetParent(go.transform, false);

            // The user's red performer: idle / walk / jump / fall / hurt (Characters/PerformerIdle_0.png ..., 34x34 px),
            // or the older single animation (Characters/Performer_0..3.png, 30x34 px).
            // 20 px per unit = 1.7 units tall; pivot at the feet so they sit on the bottom of the collider.
            Sprite[] frames = LoadCharacterFrames("PerformerIdle", 20f, PerformerPivot);
            if (frames.Length == 0) frames = LoadCharacterFrames("Performer", 20f, new Vector2(0.5f, 0f));
            if (frames.Length > 0)
            {
                capsule.direction = CapsuleDirection2D.Vertical;
                capsule.size = new Vector2(0.9f, 1.45f);
                model.localPosition = new Vector3(0f, -0.725f, 0f);
                var art = SR("Character", model, frames[0], Color.white, Vector3.zero, Vector2.one, 10);
                var anim = art.gameObject.AddComponent<SpriteFrameAnimator>();
                anim.frames = frames;
                anim.framesPerSecond = 7.7f;

                var performerSpot = SR("Performer Spotlight", world, sSoft, new Color(1f, 0.92f, 0.65f, 0.35f), new Vector3(position.x, 0.02f, 1.2f), new Vector2(2.4f, 1.6f), -49);
                performerSpot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var performer = go.AddComponent<StagePlayer>();
                performer.model = model;
                performer.floorSpot = performerSpot.transform;
                performer.animator = anim;
                SetPerformerAnimations(performer);
                return performer;
            }

            // Fallback: simple generated performer
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.size = new Vector2(0.8f, 1.3f);
            var ink = new Color(0.1f, 0.08f, 0.12f);
            SR("Leg", model, sRounded, ink, new Vector3(-0.17f, -0.48f, 0f), new Vector2(0.2f, 0.35f), 9);
            SR("Leg", model, sRounded, ink, new Vector3(0.17f, -0.48f, 0f), new Vector2(0.2f, 0.35f), 9);
            SR("Body", model, sRounded, new Color(0.88f, 0.2f, 0.28f), new Vector3(0f, -0.08f, 0f), new Vector2(0.85f, 0.75f), 10);
            SR("Belt", model, sSquare, new Color(0.15f, 0.12f, 0.1f), new Vector3(0f, -0.3f, -0.01f), new Vector2(0.84f, 0.1f), 11);
            SR("Scarf", model, sTriangle, new Color(1f, 0.85f, 0.2f), new Vector3(0f, 0.18f, -0.01f), new Vector2(0.4f, 0.22f), 11)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            SR("Head", model, sCircle, new Color(1f, 0.84f, 0.7f), new Vector3(0f, 0.48f, 0f), new Vector2(0.6f, 0.6f), 11);
            SR("Eye", model, sCircle, ink, new Vector3(0.15f, 0.52f, -0.01f), new Vector2(0.09f, 0.11f), 12);
            SR("Hat Brim", model, sRounded, ink, new Vector3(0f, 0.74f, -0.01f), new Vector2(0.8f, 0.12f), 12);
            SR("Hat", model, sRounded, ink, new Vector3(0f, 0.88f, -0.01f), new Vector2(0.46f, 0.3f), 12);

            var spot = SR("Performer Spotlight", world, sSoft, new Color(1f, 0.92f, 0.65f, 0.35f), new Vector3(position.x, 0.02f, 1.2f), new Vector2(2.2f, 1.6f), -49);
            spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var player = go.AddComponent<StagePlayer>();
            player.model = model;
            player.floorSpot = spot.transform;
            return player;
        }

        // ======================================================================
        // UI (theater frame + right-hand arrow lane, as in the sketch)
        // ======================================================================

        private static void BuildUI(StageGame game)
        {
            var canvasGo = new GameObject("Stage UI", typeof(RectTransform));
            canvasGo.layer = UILayer;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGo.AddComponent<GraphicRaycaster>();
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;
            var pulses = new List<BeatPulse>();

            // ---------------- Playfield (left)
            var play = UIRect("Playfield", root, Vector2.zero, new Vector2(PlayfieldWidth, 1f));

            Color[] lightColors =
            {
                new Color(1f, 0.95f, 0.8f), new Color(1f, 0.45f, 0.75f), new Color(0.4f, 0.9f, 1f),
                new Color(1f, 0.7f, 0.3f), new Color(0.7f, 0.5f, 1f)
            };
            const int lightCount = 7;
            var beams = UIRect("Light Beams", play, Vector2.zero, Vector2.one);
            for (int i = 0; i < lightCount; i++)
            {
                float fx = 0.15f + i * (0.7f / (lightCount - 1));
                var beam = UIRectAt("Beam", beams, new Vector2(fx, 0.835f), new Vector2(240f, 800f), new Vector2(0.5f, 1f));
                beam.localEulerAngles = new Vector3(0f, 0f, (0.5f - fx) * 40f);
                var c = lightColors[i % lightColors.Length];
                var img = Img(beam, sCone, new Color(c.r, c.g, c.b, 0.09f));
                var pulse = beam.gameObject.AddComponent<BeatPulse>();
                pulse.punch = Vector3.zero;
                pulse.everyNBeats = 2;
                pulse.beatOffset = i % 2;
                pulse.graphic = img;
                pulse.flashAlpha = 0.12f;
                pulse.palette = Rotate(lightColors, i);
                pulses.Add(pulse);
            }

            var banner = Label("Banner", play, new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.83f), "", 78,
                new Color(1f, 0.93f, 0.75f, 0.92f), FontStyle.BoldAndItalic);
            var bannerPulse = banner.gameObject.AddComponent<BeatPulse>();
            bannerPulse.punch = new Vector3(0.03f, 0.03f, 0f);
            bannerPulse.everyNBeats = 2;
            pulses.Add(bannerPulse);
            game.bannerText = banner;

            var bar = UIRect("Light Bar", play, new Vector2(0.03f, 0.862f), new Vector2(0.97f, 0.872f));
            Img(bar, sSquare, new Color(0.13f, 0.13f, 0.15f));
            for (int i = 0; i < lightCount; i++)
            {
                float fx = 0.15f + i * (0.7f / (lightCount - 1));
                var housing = UIRectAt("Stage Light", play, new Vector2(fx, 0.867f), new Vector2(44f, 52f), new Vector2(0.5f, 1f));
                Img(housing, sRounded, new Color(0.1f, 0.1f, 0.12f), Image.Type.Sliced);
                var lens = UIRectAt("Lens", housing, new Vector2(0.5f, 0f), new Vector2(34f, 20f), new Vector2(0.5f, 0.5f));
                var c = lightColors[i % lightColors.Length];
                var lensImg = Img(lens, sCircle, c);
                var pulse = lens.gameObject.AddComponent<BeatPulse>();
                pulse.punch = new Vector3(0.35f, 0.35f, 0f);
                pulse.everyNBeats = 2;
                pulse.beatOffset = i % 2;
                pulse.graphic = lensImg;
                pulse.palette = Rotate(lightColors, i);
                pulses.Add(pulse);
            }

            var curtainLeft = UIRect("Curtain Left", play, Vector2.zero, new Vector2(CurtainWidth, 1f));
            Img(curtainLeft, sCurtain, Color.white);
            var curtainRight = UIRect("Curtain Right", play, new Vector2(1f - CurtainWidth, 0f), Vector2.one);
            Img(curtainRight, sCurtain, Color.white);
            curtainRight.localScale = new Vector3(-1f, 1f, 1f);
            game.curtainLeft = curtainLeft;
            game.curtainRight = curtainRight;

            var valance = UIRect("Valance", play, new Vector2(0f, 0.88f), Vector2.one);
            Img(valance, sValance, Color.white);

            game.performerHeartFills = Hearts("Performer Hearts (P1)", play, new Vector2(CurtainWidth + 0.012f, 0.02f), new Vector2(0f, 0f), 3, 64f, 74f);
            SetPerformerHeartCounter(game); // the user's counter box replaces the row when its art is there

            game.countdownText = Label("Countdown", play, new Vector2(0f, 0.3f), new Vector2(1f, 0.7f), "", 170, new Color(1f, 0.9f, 0.3f));

            // Main menu + options (shown on the closed curtain)
            var menuGo = new GameObject("Menu") { layer = UILayer };
            menuGo.transform.SetParent(play, false);
            var menu = menuGo.AddComponent<StageMenu>();
            menu.font = font;
            menu.entrySprite = sRounded;
            game.menu = menu;
            BuildMainMenu(menu, play);
            BuildOptions(menu, play);

            var results = UIRect("Results Panel", play, new Vector2(0.12f, 0.1f), new Vector2(0.88f, 0.86f));
            game.resultsBackground = Img(results, sRounded, new Color(0f, 0f, 0f, 0.6f), Image.Type.Sliced);
            game.loseBackground = LoadButtonSprite("Results_Lose.png", FilterMode.Bilinear);
            game.gameOverPanel = LoadButtonSprite("Results_GameOver.png");
            game.resultsPanel = results.gameObject;
            game.resultsTitle = Label("Result", results, new Vector2(0f, 0.74f), new Vector2(1f, 0.98f), "BRAVO!", 120, new Color(1f, 0.84f, 0.2f));
            game.resultsBody = Label("Stats", results, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.74f), "", 34, Color.white, FontStyle.Normal);
            results.gameObject.SetActive(false);

            var pause = UIRect("Pause Panel", play, new Vector2(0.25f, 0.35f), new Vector2(0.75f, 0.65f));
            Img(pause, sRounded, new Color(0f, 0f, 0f, 0.7f), Image.Type.Sliced);
            Label("Paused", pause, Vector2.zero, Vector2.one, "PAUSED\n\nESC resume     R restart     Q main menu", 40, Color.white);
            pause.gameObject.SetActive(false);
            game.pausePanel = pause.gameObject;

            // ---------------- Rhythm panel (right)
            var panel = UIRect("Rhythm Panel (P2)", root, new Vector2(PlayfieldWidth, 0f), Vector2.one);
            Img(panel, sSquare, new Color(0.15f, 0.07f, 0.07f));
            var trim = UIRect("Gold Trim", panel, Vector2.zero, new Vector2(0f, 1f));
            trim.offsetMax = new Vector2(10f, 0f);
            Img(trim, sSquare, new Color(0.86f, 0.66f, 0.26f));

            game.scoreText = Label("Score", panel, new Vector2(0.04f, 0.945f), new Vector2(0.96f, 0.992f), "SCORE 0", 28, Color.white);
            var modeText = Label("Mode", panel, new Vector2(0.04f, 0.912f), new Vector2(0.96f, 0.945f), "", 22, new Color(1f, 0.6f, 0.2f));

            var laneRect = UIRect("Arrow Lane", panel, new Vector2(0.17f, 0.4f), new Vector2(0.83f, 0.905f));
            Img(laneRect, sRounded, new Color(0f, 0f, 0f, 0.5f), Image.Type.Sliced);
            var frame = UIRect("Frame", laneRect, Vector2.zero, Vector2.one);
            Img(frame, sFrame, new Color(0.95f, 0.78f, 0.4f, 0.9f), Image.Type.Sliced);
            var notes = UIRect("Notes", laneRect, Vector2.zero, Vector2.one);
            notes.offsetMin = new Vector2(6f, 6f);
            notes.offsetMax = new Vector2(-6f, -6f);
            notes.gameObject.AddComponent<RectMask2D>();

            const float hitLine = 80f;
            var line = UIRect("Hit Line", notes, Vector2.zero, new Vector2(1f, 0f));
            line.sizeDelta = new Vector2(-20f, 4f);
            line.anchoredPosition = new Vector2(0f, hitLine);
            Img(line, sSquare, new Color(1f, 1f, 1f, 0.25f));
            var receptor = UIRectAt("Hit Box", notes, new Vector2(0.5f, 0f), new Vector2(116f, 116f), new Vector2(0.5f, 0.5f));
            receptor.anchoredPosition = new Vector2(0f, hitLine);
            Img(receptor, sFrame, Color.white, Image.Type.Sliced);
            var laneReceptors = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var r = UIRectAt("Receptor " + (ArrowDir)i, notes, new Vector2(0.5f, 0f), new Vector2(64f, 64f), new Vector2(0.5f, 0.5f));
                Img(r, sArrowOutline, new Color(1f, 1f, 1f, 0.75f));
                r.gameObject.SetActive(false);
                laneReceptors[i] = r;
            }
            var flash = UIRectAt("Hit Flash", notes, new Vector2(0.5f, 0f), new Vector2(160f, 160f), new Vector2(0.5f, 0.5f));
            var flashImg = Img(flash, sSoft, new Color(1f, 1f, 1f, 0f));

            var rating = Label("Rating", panel, new Vector2(0f, 0.345f), new Vector2(1f, 0.4f), "", 52, Color.white);
            var combo = Label("Combo", panel, new Vector2(0f, 0.31f), new Vector2(1f, 0.35f), "", 28, Color.white);

            var lane = laneRect.gameObject.AddComponent<RhythmLane>();
            lane.noteArea = notes;
            lane.arrowSprite = sArrow;
            lane.singleReceptor = receptor;
            lane.laneReceptors = laneReceptors;
            lane.hitFlash = flashImg;
            lane.ratingText = rating;
            lane.comboText = combo;
            lane.modeText = modeText;
            lane.hitLineOffset = hitLine;
            game.lane = lane;

            BuildMusician(panel, pulses, game.conductor);
            game.musicianHeartFills = Hearts("Singer Hearts (P2)", panel, new Vector2(0.5f, 0.012f), new Vector2(0.5f, 0f), 3, 56f, 66f);
            var singerPoses = panel.GetComponentInChildren<SingerPoses>(true);
            if (singerPoses != null) SetSingerStage(singerPoses.GetComponent<SpriteFrameAnimator>(), game); // platform + skull health bar

            game.beatPulses = pulses.ToArray();
        }

        private static void BuildMainMenu(StageMenu menu, RectTransform play)
        {
            var panel = UIRect("Main Menu", play, new Vector2(0.14f, 0.07f), new Vector2(0.86f, 0.87f));
            Img(panel, sRounded, new Color(0f, 0f, 0f, 0.55f), Image.Type.Sliced);
            menu.mainPanel = panel.gameObject;

            Label("Title", panel, new Vector2(0f, 0.8f), new Vector2(1f, 0.98f), "PIMBA STAGE", 100, new Color(1f, 0.84f, 0.25f));
            menu.playButton = ArtButton("Play", panel, new Vector2(0.5f, 0.63f), LoadButtonSprite("Button_Play.png"), "PLAY", new Color(1f, 0.64f, 0.05f));
            menu.optionsButton = ArtButton("Options", panel, new Vector2(0.5f, 0.45f), LoadButtonSprite("Button_Options.png"), "OPTIONS", new Color(0.24f, 0.63f, 0.43f));
            menu.quitButton = ArtButton("Quit", panel, new Vector2(0.5f, 0.27f), LoadButtonSprite("Button_Quit.png"), "QUIT", new Color(0.78f, 0.08f, 0.08f));

            menu.summaryText = Label("Summary", panel, new Vector2(0.02f, 0.11f), new Vector2(0.98f, 0.17f), "", 24, new Color(1f, 0.9f, 0.7f));
            Label("Controls", panel, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.1f),
                "P1: A / D move, W / SPACE jump       P2: ARROW KEYS on the beat       ESC pause",
                21, new Color(0.85f, 0.82f, 0.78f), FontStyle.Normal);
        }

        private static void BuildOptions(StageMenu menu, RectTransform play)
        {
            var panel = UIRect("Options", play, new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.89f));
            Img(panel, sRounded, new Color(0f, 0f, 0f, 0.72f), Image.Type.Sliced);
            menu.optionsPanel = panel.gameObject;
            var gold = new Color(1f, 0.84f, 0.25f);
            string[] levels = { "EASY", "NORMAL", "HARD" };

            Label("Title", panel, new Vector2(0f, 0.89f), new Vector2(1f, 0.99f), "OPTIONS", 64, gold);

            Label("P1 Label", panel, new Vector2(0.04f, 0.8f), new Vector2(0.96f, 0.86f), "PERFORMER (P1)  -  OBSTACLES", 28, Color.white);
            menu.performerButtons = new Button[3];
            for (int i = 0; i < 3; i++)
                menu.performerButtons[i] = TextButton("P1 " + levels[i], panel, new Vector2(0.17f + i * 0.23f, 0.73f), new Vector2(0.37f + i * 0.23f, 0.8f), levels[i]);
            menu.performerHint = Label("P1 Hint", panel, new Vector2(0.04f, 0.67f), new Vector2(0.96f, 0.725f), "", 22, new Color(0.85f, 0.82f, 0.78f), FontStyle.Italic);

            Label("P2 Label", panel, new Vector2(0.04f, 0.585f), new Vector2(0.96f, 0.645f), "SINGER (P2)  -  ARROWS", 28, Color.white);
            menu.musicianButtons = new Button[3];
            for (int i = 0; i < 3; i++)
                menu.musicianButtons[i] = TextButton("P2 " + levels[i], panel, new Vector2(0.17f + i * 0.23f, 0.515f), new Vector2(0.37f + i * 0.23f, 0.585f), levels[i]);
            menu.musicianHint = Label("P2 Hint", panel, new Vector2(0.04f, 0.455f), new Vector2(0.96f, 0.51f), "", 22, new Color(0.85f, 0.82f, 0.78f), FontStyle.Italic);

            // Song list
            Label("Song Label", panel, new Vector2(0.04f, 0.385f), new Vector2(0.96f, 0.44f), "SONG", 28, Color.white);
            var list = UIRect("Song List", panel, new Vector2(0.12f, 0.19f), new Vector2(0.88f, 0.385f));
            var listBg = Img(list, sRounded, new Color(0f, 0f, 0f, 0.35f), Image.Type.Sliced);
            listBg.raycastTarget = true;
            var scroll = list.gameObject.AddComponent<ScrollRect>();
            var viewport = UIRect("Viewport", list, Vector2.zero, Vector2.one);
            viewport.offsetMin = new Vector2(8f, 8f);
            viewport.offsetMax = new Vector2(-8f, -8f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportImg = Img(viewport, sSquare, new Color(0f, 0f, 0f, 0f));
            viewportImg.raycastTarget = true;
            var content = UIRect("Content", viewport, new Vector2(0f, 1f), Vector2.one);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            menu.songScroll = scroll;
            menu.songListContent = content;

            // Volume
            Label("Volume Label", panel, new Vector2(0.04f, 0.1f), new Vector2(0.24f, 0.17f), "VOLUME", 28, Color.white);
            var sliderRect = UIRect("Volume Slider", panel, new Vector2(0.26f, 0.115f), new Vector2(0.72f, 0.155f));
            var sliderBg = Img(sliderRect, sRounded, new Color(0.12f, 0.06f, 0.08f), Image.Type.Sliced);
            sliderBg.raycastTarget = true;
            var fillArea = UIRect("Fill Area", sliderRect, Vector2.zero, Vector2.one);
            fillArea.offsetMin = new Vector2(6f, 6f);
            fillArea.offsetMax = new Vector2(-6f, -6f);
            var fill = UIRect("Fill", fillArea, Vector2.zero, Vector2.one);
            Img(fill, sRounded, gold, Image.Type.Sliced);
            var handleArea = UIRect("Handle Slide Area", sliderRect, Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(16f, 0f);
            handleArea.offsetMax = new Vector2(-16f, 0f);
            var handle = UIRect("Handle", handleArea, Vector2.zero, Vector2.one);
            handle.sizeDelta = new Vector2(46f, 10f);
            var handleImg = Img(handle, sCircle, Color.white);
            handleImg.raycastTarget = true;
            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.8f;
            var colors = slider.colors;
            colors.highlightedColor = new Color(1f, 0.9f, 0.5f);
            colors.selectedColor = gold;
            slider.colors = colors;
            menu.volumeSlider = slider;
            menu.volumeText = Label("Volume Value", panel, new Vector2(0.74f, 0.1f), new Vector2(0.9f, 0.17f), "80%", 28, gold);

            menu.backButton = TextButton("Back", panel, new Vector2(0.4f, 0.015f), new Vector2(0.6f, 0.085f), "BACK");
            SetDifficultyButtons(menu); // EASY / NORMAL / HARD drawn with the pixel art, if it's in the UI folder
            panel.gameObject.SetActive(false);
        }

        /// <summary>Button using the hand-drawn pixel art (shown 4x), or a plain coloured button if the art is missing.</summary>
        private static Button ArtButton(string name, Transform parent, Vector2 anchor, Sprite sprite, string fallbackText, Color fallbackColor)
        {
            Vector2 size = sprite != null ? new Vector2(sprite.rect.width, sprite.rect.height) * 4f : new Vector2(240f, 110f);
            var rect = UIRectAt(name, parent, anchor, size, new Vector2(0.5f, 0.5f));
            Image image;
            if (sprite != null)
            {
                image = Img(rect, sprite, Color.white);
                image.preserveAspect = true;
            }
            else
            {
                image = Img(rect, sRounded, fallbackColor, Image.Type.Sliced);
                Label("Label", rect, Vector2.zero, Vector2.one, fallbackText, 48, Color.white);
            }
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(0.9f, 0.9f, 0.9f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            button.colors = colors;
            rect.gameObject.AddComponent<MenuButtonFX>();
            return button;
        }

        private static Button TextButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string text)
        {
            var rect = UIRect(name, parent, anchorMin, anchorMax);
            var image = Img(rect, sRounded, new Color(0.16f, 0.09f, 0.11f, 0.95f), Image.Type.Sliced);
            image.raycastTarget = true;
            var label = Label("Label", rect, Vector2.zero, Vector2.one, text, 30, Color.white);
            label.GetComponent<Outline>().enabled = false;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(0.92f, 0.92f, 0.92f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            button.colors = colors;
            rect.gameObject.AddComponent<MenuButtonFX>().selectedScale = 1.1f;
            return button;
        }

        /// <summary>
        /// Loads Characters/{prefix}_0.png, _1.png ... (frames taken from the user's GIFs) as crisp pixel-art sprites.
        /// Returns an empty array if there are none.
        /// </summary>
        private static Sprite[] LoadCharacterFrames(string prefix, float pixelsPerUnit, Vector2 pivot, string folder = CharacterFolder)
        {
            var frames = new List<Sprite>();
            for (int i = 0; i < 64; i++)
            {
                string path = folder + "/" + prefix + "_" + i + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) break;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                bool needsSetup = importer.textureType != TextureImporterType.Sprite || importer.filterMode != FilterMode.Point ||
                                  importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled ||
                                  !Mathf.Approximately(importer.spritePixelsPerUnit, pixelsPerUnit) ||
                                  settings.spriteAlignment != (int)SpriteAlignment.Custom || settings.spritePivot != pivot;
                if (needsSetup)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.filterMode = FilterMode.Point;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.spritePixelsPerUnit = pixelsPerUnit;
                    importer.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = pivot;
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    importer.SaveAndReimport();
                }
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) break;
                frames.Add(sprite);
            }
            return frames.ToArray();
        }

        /// <summary>Loads one of the hand-drawn menu buttons and makes sure it imports as crisp pixel art.</summary>
        private static Sprite LoadButtonSprite(string file, FilterMode filter = FilterMode.Point)
        {
            string path = ButtonFolder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("Pimba Stage: UI art not found at " + path + ", using the plain look.");
                return null;
            }
            if (importer.textureType != TextureImporterType.Sprite || importer.filterMode != filter ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled ||
                importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = filter;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.spritePixelsPerUnit = 16f;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BuildMusician(RectTransform panel, List<BeatPulse> pulses, Conductor conductor)
        {
            // The user's yellow singer, shown at 7x: the dance (Characters/SingerIdle_0..3.png, 34x32 px, one loop per beat),
            // a pose for each arrow and a miss pose (SingerLeft/Down/Up/Right/Miss_0..1.png),
            // or the older 4-frame dance only (Characters/Singer_0..3.png, 34x23 px).
            Sprite[] frames = LoadCharacterFrames("SingerIdle", 20f, SingerPivot);
            if (frames.Length == 0) frames = LoadCharacterFrames("Singer", 20f, SingerPivot);
            if (frames.Length > 0)
            {
                var stand = UIRectAt("Singer", panel, new Vector2(0.5f, 0.065f), new Vector2(340f, 250f), new Vector2(0.5f, 0f));
                Img(Px("Spotlight", stand, 170f, 115f, 320f, 220f), sSoft, new Color(1f, 0.9f, 0.55f, 0.22f));
                var singer = Px("Character", stand, 170f, 112f, 238f, 161f);
                var singerImg = Img(singer, frames[0], Color.white);
                singerImg.preserveAspect = true;
                var anim = singer.gameObject.AddComponent<SpriteFrameAnimator>();
                anim.frames = frames;
                anim.framesPerSecond = 7.7f;
                anim.framesPerBeat = 4;
                anim.conductor = conductor;
                SetSingerAnimations(anim, panel.GetComponentInChildren<RhythmLane>(true));
                Img(Px("Podium", stand, 170f, 16f, 300f, 32f), sRounded, new Color(0.38f, 0.1f, 0.12f), Image.Type.Sliced);
                Img(Px("Podium Trim", stand, 170f, 33f, 300f, 6f), sSquare, new Color(0.86f, 0.66f, 0.26f));
                return;
            }

            // Fallback: simple generated accordion player
            var box = UIRectAt("Musician", panel, new Vector2(0.5f, 0.07f), new Vector2(340f, 200f), new Vector2(0.5f, 0f));
            var suit = new Color(0.12f, 0.12f, 0.18f);
            var skin = new Color(1f, 0.82f, 0.68f);
            var ink = new Color(0.08f, 0.06f, 0.08f);

            Img(Px("Body", box, 170f, 105f, 130f, 120f), sRounded, suit, Image.Type.Sliced);
            Img(Px("Bow Tie", box, 170f, 158f, 34f, 14f), sRounded, new Color(0.85f, 0.15f, 0.2f), Image.Type.Sliced);

            var head = Px("Head", box, 170f, 180f, 62f, 62f);
            Img(head, sCircle, skin);
            Img(Px("Eye L", head, 22f, 36f, 7f, 8f), sCircle, ink);
            Img(Px("Eye R", head, 40f, 36f, 7f, 8f), sCircle, ink);
            Img(Px("Moustache", head, 31f, 22f, 34f, 9f), sRounded, ink, Image.Type.Sliced);
            Img(Px("Hat Brim", head, 31f, 58f, 86f, 12f), sRounded, ink, Image.Type.Sliced);
            Img(Px("Hat", head, 31f, 72f, 52f, 24f), sRounded, ink, Image.Type.Sliced);
            var headPulse = head.gameObject.AddComponent<BeatPulse>();
            headPulse.punch = new Vector3(0.06f, 0.06f, 0f);
            pulses.Add(headPulse);

            var accordion = Px("Accordion", box, 170f, 108f, 150f, 66f);
            var bellows = Px("Bellows", accordion, 75f, 33f, 94f, 58f);
            Img(bellows, sBellows, new Color(0.95f, 0.9f, 0.82f));
            var bellowsPulse = bellows.gameObject.AddComponent<BeatPulse>();
            bellowsPulse.punch = new Vector3(0.22f, 0f, 0f);
            pulses.Add(bellowsPulse);
            Img(Px("Left Box", accordion, 14f, 33f, 28f, 66f), sRounded, new Color(0.8f, 0.14f, 0.18f), Image.Type.Sliced);
            Img(Px("Right Box", accordion, 136f, 33f, 28f, 66f), sRounded, new Color(0.8f, 0.14f, 0.18f), Image.Type.Sliced);
            for (int k = 0; k < 4; k++)
                Img(Px("Key", accordion, 136f, 14f + k * 13f, 14f, 8f), sRounded, Color.white, Image.Type.Sliced);

            Img(Px("Podium", box, 170f, 30f, 300f, 60f), sRounded, new Color(0.38f, 0.1f, 0.12f), Image.Type.Sliced);
            Img(Px("Podium Trim", box, 170f, 60f, 300f, 6f), sSquare, new Color(0.86f, 0.66f, 0.26f));
        }

        private static Image[] Hearts(string name, Transform parent, Vector2 anchor, Vector2 pivot, int count, float size, float spacing)
        {
            var row = UIRectAt(name, parent, anchor, new Vector2(spacing * (count - 1) + size, size), pivot);
            var fills = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var back = UIRectAt("Heart " + (i + 1), row, new Vector2(0f, 0.5f), new Vector2(size, size), new Vector2(0f, 0.5f));
                back.anchoredPosition = new Vector2(i * spacing, 0f);
                Img(back, sHeart, new Color(0.1f, 0.02f, 0.04f, 0.9f));
                var fillRect = UIRect("Fill", back, Vector2.zero, Vector2.one);
                fillRect.offsetMin = new Vector2(5f, 5f);
                fillRect.offsetMax = new Vector2(-5f, -5f);
                var fill = Img(fillRect, sHeart, new Color(0.95f, 0.18f, 0.3f));
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.fillAmount = 1f;
                fills[i] = fill;
            }
            return fills;
        }

        // ======================================================================
        // Helpers
        // ======================================================================

        private static SpriteRenderer SR(string name, Transform parent, Sprite sprite, Color color, Vector3 localPosition, Vector2 scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            if (spriteMat != null) sr.sharedMaterial = spriteMat;
            return sr;
        }

        private static SpriteRenderer Sized(string name, Transform parent, Sprite sprite, Color color, Vector3 localPosition, Vector2 size, int order, SpriteDrawMode mode)
        {
            var sr = SR(name, parent, sprite, color, localPosition, Vector2.one, order);
            sr.drawMode = mode;
            sr.size = size;
            return sr;
        }

        private static RectTransform UIRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UILayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static RectTransform UIRectAt(string name, Transform parent, Vector2 anchor, Vector2 size, Vector2 pivot)
        {
            var rt = UIRect(name, parent, anchor, anchor);
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>Fixed-size element centred at (x, y) pixels from the parent's bottom-left corner.</summary>
        private static RectTransform Px(string name, Transform parent, float x, float y, float w, float h)
        {
            var rt = UIRectAt(name, parent, Vector2.zero, new Vector2(w, h), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = type;
            img.raycastTarget = false;
            return img;
        }

        private static Text Label(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string text, int size, Color color,
            FontStyle style = FontStyle.Bold)
        {
            var rt = UIRect(name, parent, anchorMin, anchorMax);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            // The pixel font only has a regular style; fake bold/italic would blur it.
            t.fontStyle = customFont ? FontStyle.Normal : style;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            return t;
        }

        private static Color[] Rotate(Color[] colors, int by)
        {
            var result = new Color[colors.Length];
            for (int i = 0; i < colors.Length; i++) result[i] = colors[(i + by) % colors.Length];
            return result;
        }

        private static List<PimbaSong> LoadSongs()
        {
            var demo = AssetDatabase.LoadAssetAtPath<PimbaSong>(DemoSongPath);
            if (demo == null)
            {
                demo = ScriptableObject.CreateInstance<PimbaSong>();
                demo.title = "Arraial na Aldeia";
                demo.artist = "Banda do Arraial";
                demo.useDemoTrack = true;
                demo.bpm = 128f;
                demo.autoSeed = 7;
                AssetDatabase.CreateAsset(demo, DemoSongPath);
            }
            var songs = new List<PimbaSong> { demo };
            foreach (string guid in AssetDatabase.FindAssets("t:PimbaSong"))
            {
                var song = AssetDatabase.LoadAssetAtPath<PimbaSong>(AssetDatabase.GUIDToAssetPath(guid));
                if (song != null && !songs.Contains(song)) songs.Add(song);
            }
            return songs;
        }

        /// <summary>Uses the first font in Assets/PimbaStage/Fonts (VCR OSD Mono), else Unity's built-in font.</summary>
        private static Font LoadFont()
        {
            customFont = false;
            if (AssetDatabase.IsValidFolder(FontFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Font", new[] { FontFolder }))
                {
                    var custom = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                    if (custom == null) continue;
                    customFont = true;
                    return custom;
                }
            }
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) Debug.LogWarning("Pimba Stage: built-in font LegacyRuntime.ttf not found; assign a font to the UI texts.");
            return f;
        }

        private static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            return mat;
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        // ======================================================================
        // Generated art
        // ======================================================================

        private static readonly Vector2[] ArrowShape =
        {
            new Vector2(64f, 121f), new Vector2(121f, 64f), new Vector2(88f, 64f), new Vector2(88f, 8f),
            new Vector2(40f, 8f), new Vector2(40f, 64f), new Vector2(7f, 64f)
        };

        private static void CreateSprites()
        {
            Vector4 none = Vector4.zero;
            sSquare = MakeSprite("PS_Square", 16, 16, 16f, FilterMode.Point, none, (x, y) => Color.white);
            sCircle = MakeSprite("PS_Circle", 128, 128, 128f, FilterMode.Bilinear, none,
                (x, y) => new Color(1f, 1f, 1f, Mathf.Clamp01(63.5f - Dist(x + 0.5f, y + 0.5f, 64f, 64f))));
            sSoft = MakeSprite("PS_Soft", 128, 128, 128f, FilterMode.Bilinear, none, (x, y) =>
            {
                float a = Mathf.Clamp01(1f - Dist(x + 0.5f, y + 0.5f, 64f, 64f) / 63f);
                return new Color(1f, 1f, 1f, a * a);
            });
            sTriangle = MakeSprite("PS_Triangle", 128, 128, 128f, FilterMode.Bilinear, none, (x, y) =>
            {
                float half = 64f * (1f - (y + 0.5f) / 128f);
                return new Color(1f, 1f, 1f, Mathf.Clamp01((half - Mathf.Abs(x + 0.5f - 64f)) * 0.9f));
            });
            sArrow = MakeSprite("PS_Arrow", 128, 128, 128f, FilterMode.Bilinear, none, (x, y) => ArrowPixel(x, y, false));
            sArrowOutline = MakeSprite("PS_ArrowOutline", 128, 128, 128f, FilterMode.Bilinear, none, (x, y) => ArrowPixel(x, y, true));
            sHeart = MakeSprite("PS_Heart", 64, 64, 64f, FilterMode.Bilinear, none, HeartPixel);
            sCone = MakeSprite("PS_LightCone", 128, 256, 128f, FilterMode.Bilinear, none, (x, y) =>
            {
                float v = (y + 0.5f) / 256f;
                float half = 0.06f + 0.44f * (1f - v);
                float dx = Mathf.Abs((x + 0.5f) / 128f - 0.5f);
                float edge = Mathf.Clamp01((half - dx) / (half * 0.45f));
                float a = edge * Mathf.Lerp(0.15f, 1f, Mathf.Pow(v, 1.2f)) * Mathf.Clamp01((1f - v) * 25f);
                return new Color(1f, 1f, 1f, a);
            });
            sCurtain = MakeSprite("PS_Curtain", 256, 512, 128f, FilterMode.Bilinear, none, (x, y) =>
            {
                float u = (x + 0.5f) / 256f, v = (y + 0.5f) / 512f;
                float fold = 0.5f + 0.5f * Mathf.Cos(u * Mathf.PI * 2f * 4.5f);
                if (y < 18) return new Color(0.88f * (0.6f + 0.4f * fold), 0.66f * (0.6f + 0.4f * fold), 0.22f * (0.6f + 0.4f * fold), 1f);
                float shade = (0.35f + 0.65f * Mathf.Pow(fold, 0.8f)) * Mathf.Lerp(0.85f, 1f, v) * (v > 0.94f ? 0.75f : 1f);
                return new Color(0.66f * shade, 0.06f * shade, 0.1f * shade, 1f);
            });
            sValance = MakeSprite("PS_Valance", 512, 128, 128f, FilterMode.Bilinear, none, (x, y) =>
            {
                float u = (x + 0.5f) / 512f;
                float f = (u * 7f) % 1f;
                float edge = 8f + 34f * (2f * f - 1f) * (2f * f - 1f);
                float a = Mathf.Clamp01(y + 0.5f - edge);
                if (a <= 0f) return Color.clear;
                float fold = 0.5f + 0.5f * Mathf.Cos(u * Mathf.PI * 2f * 28f);
                Color c = y < edge + 10f || (y > 110 && y < 118)
                    ? new Color(0.9f, 0.68f, 0.24f) * (0.7f + 0.3f * fold)
                    : new Color(0.62f, 0.05f, 0.09f) * (0.45f + 0.55f * fold);
                c.a = a;
                return c;
            });
            sWood = MakeSprite("PS_Wood", 64, 64, 64f, FilterMode.Point, none, (x, y) =>
            {
                int plank = y / 16, py = y % 16;
                float seamX = Hash(plank, 9) * 64f;
                bool seam = py == 0 || Mathf.Abs(x - seamX) < 0.75f;
                float s = seam ? 0.45f
                    : 0.8f + 0.2f * Hash(plank, 3) + 0.06f * Mathf.Sin(x * 0.45f + Hash(plank, 5) * 20f + py * 0.9f) + (Hash(x / 3, y) - 0.5f) * 0.05f;
                if (py == 1) s += 0.08f;
                return new Color(s, s, s, 1f);
            });
            sCrate = MakeSprite("PS_Crate", 64, 64, 64f, FilterMode.Point, new Vector4(12, 12, 12, 12), (x, y) =>
            {
                if (x < 2 || y < 2 || x > 61 || y > 61) return Grey(0.3f);
                if (x < 11 || y < 11 || x > 52 || y > 52)
                    return Grey(x == 10 || y == 10 || x == 53 || y == 53 ? 0.45f : 0.72f + (Hash(x / 4, y / 4) - 0.5f) * 0.06f);
                bool seam = (x - 11) % 10 == 0;
                return Grey(seam ? 0.55f : 0.92f + 0.08f * Hash((x - 11) / 10, 2) + (Hash(x, y / 3) - 0.5f) * 0.05f);
            });
            sCardboard = MakeSprite("PS_Cardboard", 64, 64, 64f, FilterMode.Point, new Vector4(8, 8, 8, 8), (x, y) =>
            {
                if (x < 3 || y < 3 || x > 60 || y > 60) return Grey(0.42f);
                return Grey(0.95f - (y % 6 == 0 ? 0.05f : 0f) + (Hash(x / 2, y / 2) - 0.5f) * 0.04f);
            });
            sBackdrop = MakeSprite("PS_Backdrop", 512, 256, 16f, FilterMode.Bilinear, none, BackdropPixel);
            sFlame = MakeSprite("PS_Flame", 128, 128, 128f, FilterMode.Bilinear, none, FlamePixel);
            sStar = MakeSprite("PS_Star", 128, 128, 128f, FilterMode.Bilinear, none, StarPixel);
            sRounded = MakeSprite("PS_Rounded", 64, 64, 64f, FilterMode.Bilinear, new Vector4(20, 20, 20, 20),
                (x, y) => new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - RoundedSdf(x + 0.5f, y + 0.5f, 32f, 32f, 31.5f, 31.5f, 18f))));
            sFrame = MakeSprite("PS_Frame", 64, 64, 64f, FilterMode.Bilinear, new Vector4(20, 20, 20, 20), (x, y) =>
            {
                float d = RoundedSdf(x + 0.5f, y + 0.5f, 32f, 32f, 31.5f, 31.5f, 18f);
                return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + 6.5f));
            });
            sBellows = MakeSprite("PS_Bellows", 64, 32, 64f, FilterMode.Point, none, (x, y) =>
            {
                float s = x % 8 < 4 ? 1f : 0.55f;
                if (y < 3 || y > 28) s *= 0.6f;
                return Grey(s);
            });
            sSack = MakeSprite("PS_Sandbag", 64, 64, 64f, FilterMode.Bilinear, none, (x, y) =>
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = Mathf.Min(Dist(px, py, 32f, 25f) - 22f, RoundedSdf(px, py, 32f, 50f, 7f, 10f, 3f));
                float a = Mathf.Clamp01(0.5f - d);
                if (a <= 0f) return Color.clear;
                Color c = d > -3f ? new Color(0.3f, 0.2f, 0.1f)
                    : py > 40f && py < 45f ? new Color(0.35f, 0.22f, 0.12f)
                    : new Color(0.78f, 0.64f, 0.44f) * (0.85f + 0.15f * py / 64f);
                c.a = a;
                return c;
            });
        }

        private static Color ArrowPixel(int x, int y, bool outlineOnly)
        {
            float d = PolySdf(new Vector2(x + 0.5f, y + 0.5f), ArrowShape);
            float alpha = Mathf.Clamp01(0.5f - d);
            const float border = 8f;
            if (outlineOnly) return new Color(1f, 1f, 1f, alpha * (d > -border ? 1f : 0.18f));
            float t = Mathf.Clamp01(-d - border + 0.5f);
            float shade = Mathf.Lerp(0.78f, 1f, y / 127f);
            Color c = Color.Lerp(new Color(0.08f, 0.06f, 0.1f), new Color(shade, shade, shade), t);
            c.a = alpha;
            return c;
        }

        private static Color HeartPixel(int x, int y)
        {
            int inside = 0;
            for (int sy = 0; sy < 3; sy++)
            {
                for (int sx = 0; sx < 3; sx++)
                {
                    float u = ((x + (sx + 0.5f) / 3f) / 64f - 0.5f) * 2.7f;
                    float v = ((y + (sy + 0.5f) / 3f) / 64f - 0.45f) * 2.7f;
                    float a = u * u + v * v - 1f;
                    if (a * a * a - u * u * v * v * v <= 0f) inside++;
                }
            }
            return new Color(1f, 1f, 1f, inside / 9f);
        }

        private static Color FlamePixel(int x, int y)
        {
            float xn = (x + 0.5f - 64f) / 60f;
            float yn = (y + 0.5f) / 124f;
            if (yn > 1f) return Color.clear;
            float w = yn < 0.32f
                ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((0.32f - yn) / 0.32f, 2f)))
                : Mathf.Pow(1f - (yn - 0.32f) / 0.68f, 1.1f);
            w *= 0.85f * (1f + 0.07f * Mathf.Sin(yn * 19f));
            float d = (Mathf.Abs(xn) - w) * 60f;
            float a = Mathf.Clamp01(0.5f - d);
            if (a <= 0f) return Color.clear;
            Color c = d > -3.5f ? new Color(0.35f, 0.1f, 0.03f)
                : Mathf.Abs(xn) < w * 0.5f && yn < 0.62f ? new Color(1f, 0.86f, 0.25f)
                : new Color(1f, 0.38f, 0.08f);
            c.a = a;
            return c;
        }

        private static Vector2[] starShape;

        private static Color StarPixel(int x, int y)
        {
            if (starShape == null)
            {
                starShape = new Vector2[10];
                for (int k = 0; k < 10; k++)
                {
                    float angle = (90f + k * 36f) * Mathf.Deg2Rad;
                    float r = k % 2 == 0 ? 60f : 25f;
                    starShape[k] = new Vector2(64f + r * Mathf.Cos(angle), 64f + r * Mathf.Sin(angle));
                }
            }
            float d = PolySdf(new Vector2(x + 0.5f, y + 0.5f), starShape);
            Color c = d > -5f ? new Color(0.55f, 0.32f, 0.05f) : new Color(1f, 0.82f + 0.1f * (y / 128f), 0.2f);
            c.a = Mathf.Clamp01(0.5f - d);
            return c;
        }

        private static Color BackdropPixel(int x, int y)
        {
            float u = (x + 0.5f) / 512f, v = (y + 0.5f) / 256f;
            float tau = Mathf.PI * 2f;
            Color top = new Color(0.07f, 0.08f, 0.25f), mid = new Color(0.32f, 0.13f, 0.4f), low = new Color(0.98f, 0.52f, 0.3f);
            Color c = v > 0.5f ? Color.Lerp(mid, top, (v - 0.5f) / 0.5f) : Color.Lerp(low, mid, Mathf.Clamp01((v - 0.15f) / 0.35f));
            if (v > 0.45f && Hash(x, y) > 0.995f) c = Color.Lerp(c, Color.white, 0.8f);
            float moon = Dist(x + 0.5f, y + 0.5f, 400f, 205f);
            if (moon < 16.5f) c = Color.Lerp(c, new Color(1f, 0.95f, 0.8f), Mathf.Clamp01(16f - moon));

            float far = 78f + 16f * Mathf.Sin(tau * u * 2f) + 9f * Mathf.Sin(tau * u * 5f + 1.3f);
            if (y < far) c = new Color(0.25f, 0.15f, 0.34f);

            // Little village on the far hill
            int house = x / 32;
            float hx = house * 32 + 6 + Hash(house, 1) * 8f, hw = 14f;
            float hu = (hx + hw * 0.5f) / 512f;
            float baseY = 78f + 16f * Mathf.Sin(tau * hu * 2f) + 9f * Mathf.Sin(tau * hu * 5f + 1.3f) - 9f;
            float hh = 10f + Hash(house, 2) * 6f;
            if (x >= hx && x < hx + hw && y >= baseY && y < baseY + hh)
            {
                c = new Color(0.5f, 0.46f, 0.42f);
                if ((int)(x - hx) % 5 == 2 && (int)(y - baseY) % 5 == 2 && Hash(x / 3, y / 3) > 0.35f) c = new Color(1f, 0.85f, 0.4f);
            }
            float roofTop = baseY + hh + 6f - Mathf.Abs(x - (hx + hw * 0.5f)) * 0.75f;
            if (x >= hx - 1 && x < hx + hw + 1 && y >= baseY + hh && y < roofTop) c = new Color(0.55f, 0.2f, 0.15f);

            float near = 40f + 14f * Mathf.Sin(tau * u * 3f + 0.5f) + 6f * Mathf.Sin(tau * u * 7f);
            if (y < near) c = new Color(0.12f, 0.08f, 0.19f);
            c.a = 1f;
            return c;
        }

        private static Color Grey(float s) => new Color(s, s, s, 1f);

        private static float Dist(float x, float y, float cx, float cy)
        {
            float dx = x - cx, dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static float RoundedSdf(float px, float py, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(px - cx) - (hx - r);
            float qy = Mathf.Abs(py - cy) - (hy - r);
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static float PolySdf(Vector2 p, Vector2[] poly)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                Vector2 e = b - a, w = p - a;
                float t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, (w - e * t).magnitude);
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside ? -d : d;
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 1023u) / 1023f;
        }

        private static Sprite MakeSprite(string name, int width, int height, float pixelsPerUnit, FilterMode filter, Vector4 border,
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
                importer.spriteBorder = border;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
