using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static PimbaStage.RuntimeUI;

namespace PimbaStage
{
    /// <summary>
    /// The map creator (3D version): players put the game's 3D pieces on the stage and save the map to play on it.
    ///  Object list (bottom): pick a piece, then left click on the stage to place it (keeps placing until right click / Esc).
    ///  Left click a placed piece: pick it up to move it.   Right click a placed piece (or Delete): remove it.
    ///  Q / E or the mouse wheel: size.   F: flip.   Shift: place in the air (otherwise it lands on what's below).
    ///  A / D or Left / Right (and the wheel with nothing picked): move along the stage.
    ///  Top bar: map name, SAVE, TEST (save and play it now), EXIT.
    /// </summary>
    public class MapCreator : MonoBehaviour
    {
        private static readonly float[] Sizes = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f };
        private const float Grid = 0.25f;
        private const float PanSpeed = 14f;
        private static readonly Color GhostTint = new Color(1.25f, 1.2f, 0.75f);
        private static readonly Color BadTint = new Color(1.6f, 0.35f, 0.35f);
        private static readonly Color HoverTint = new Color(1.45f, 1.45f, 1.2f);

        public bool IsOpen { get; private set; }

        private StageGame game;
        private MapStage stage;
        private StageScroller scroller;
        private Camera cam;
        private Font font;
        private Sprite sprite;

        private RectTransform root;
        private InputField nameField;
        private Text status, info, startLabel, finishLabel;
        private float statusUntil;
        private readonly List<(string id, Image back, RawImage picture)> tiles = new List<(string, Image, RawImage)>();

        private Transform ghostHolder;
        private MapPieceTag held, hover;
        private bool heldIsNew;
        private Vector3 heldFrom;
        private float heldFromSize;
        private bool heldFromFlip;
        private string brush;
        private float brushSize = 1f;
        private bool brushFlip, heldValid;
        private float viewX;
        private bool unsaved, exitArmed;
        private string overwriteArmed;
        private string loadedPath;
        private static MaterialPropertyBlock block; // made on first use (Unity objects can't be made in field initializers)

        public void Setup(StageGame owner, MapStage mapStage, StageScroller stageScroller, RectTransform playfield, Font uiFont, Sprite boxSprite)
        {
            if (root != null || playfield == null) return;
            game = owner;
            stage = mapStage;
            scroller = stageScroller;
            cam = scroller != null ? scroller.cam : Camera.main;
            font = uiFont;
            sprite = boxSprite;
            ghostHolder = new GameObject("Map Creator (piece in hand)").transform;
            Build(playfield);
        }

        /// <summary>Opens the creator on the stage; map = null starts an empty map.</summary>
        public void Begin(StageMap map)
        {
            stage.ApplyLayout(map);
            stage.RenderThumbnails(cam);
            foreach (var tile in tiles)
            {
                var kind = stage.Find(tile.id);
                tile.picture.texture = kind != null ? kind.thumbnail : null;
                tile.picture.enabled = tile.picture.texture != null;
            }
            loadedPath = map != null ? map.path : null;
            nameField.SetTextWithoutNotify(map != null ? map.name : "");
            unsaved = exitArmed = false;
            overwriteArmed = null;
            brush = null;
            held = hover = null;
            viewX = scroller.startX;
            scroller.JumpTo(viewX);
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
            IsOpen = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Status(map != null ? "EDITING  " + map.name : "NEW MAP - PICK A PIECE BELOW AND CLICK ON THE STAGE", 5f);
            RefreshTiles();
        }

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (!IsOpen) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            bool typing = nameField.isFocused;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            float wheel = mouse != null && !overUI ? mouse.scroll.ReadValue().y : 0f;

            // Move along the stage
            if (!typing && kb != null)
            {
                float dir = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                viewX += dir * PanSpeed * (kb.shiftKey.isPressed ? 2f : 1f) * dt;
            }
            if (held == null && wheel != 0f) viewX -= Mathf.Sign(wheel) * 2f;
            viewX = Mathf.Clamp(viewX, scroller.startX, scroller.endX);
            scroller.JumpTo(viewX);

            // Keys
            if (!typing && kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (held != null) DropHeld();
                    else Exit();
                    return;
                }
                bool ctrl = kb.ctrlKey.isPressed;
                if (ctrl && kb.sKey.wasPressedThisFrame) Save();
                if (held != null)
                {
                    if (kb.qKey.wasPressedThisFrame) Resize(-1);
                    if (kb.eKey.wasPressedThisFrame) Resize(1);
                    if (kb.fKey.wasPressedThisFrame) Flip();
                    if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) DeleteHeld();
                }
                else if (hover != null && (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) DeletePiece(hover);
            }

            // Mouse on the stage
            Vector2? world = overUI ? null : MouseOnStage();
            bool shift = kb != null && kb.shiftKey.isPressed;
            if (held != null)
            {
                if (wheel != 0f) Resize(wheel > 0f ? 1 : -1);
                held.gameObject.SetActive(world.HasValue);
                if (world.HasValue)
                {
                    Vector2 at = Snap(world.Value, held, shift);
                    MapStage.Pose(held, at.x, at.y, held.size, held.flip);
                    bool valid = IsValid(held);
                    if (valid != heldValid) { heldValid = valid; Tint(held, valid ? GhostTint : BadTint); }
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        if (valid) Place();
                        else Status(stage.PieceCount >= MapStage.MaxPieces ? "THE MAP IS FULL" : "NOT THERE - KEEP THE START AND THE FINALE SPOTLIGHT CLEAR", 2.5f);
                    }
                }
                if (mouse != null && mouse.rightButton.wasPressedThisFrame) DropHeld();
            }
            else
            {
                var under = world.HasValue ? stage.PieceAt(world.Value) : null;
                if (under != hover)
                {
                    if (hover != null) Tint(hover, null);
                    hover = under;
                    if (hover != null) Tint(hover, HoverTint);
                }
                if (hover != null && mouse != null)
                {
                    if (mouse.leftButton.wasPressedThisFrame) PickUp(hover);
                    else if (mouse.rightButton.wasPressedThisFrame) DeletePiece(hover);
                }
            }

            UpdateLabels();
        }

        private void UpdateLabels()
        {
            if (status.enabled && Time.unscaledTime > statusUntil)
            {
                float a = Mathf.Clamp01(1f - (Time.unscaledTime - statusUntil) * 2f);
                status.color = new Color(status.color.r, status.color.g, status.color.b, a);
                if (a <= 0f) status.enabled = false;
            }
            Follow(startLabel, new Vector3(stage.SpawnPoint.x, 2.6f, 0f));
            Follow(finishLabel, new Vector3(stage.GoalX, 4.6f, 0f));
            if (held != null)
                info.text = stage.Find(held.kind).label + "   SIZE x" + held.size.ToString("0.##") + (held.flip ? "   FLIPPED" : "");
            else
                info.text = stage.PieceCount + " PIECES" + (unsaved ? "   (NOT SAVED)" : "");
        }

        private void Follow(Text label, Vector3 world)
        {
            Vector3 screen = cam.WorldToScreenPoint(world);
            bool visible = screen.z > 0f && cam.pixelRect.Contains(screen);
            label.enabled = visible;
            if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local))
                label.rectTransform.anchoredPosition = local;
        }

        /// <summary>Where the mouse points on the stage (the plane the performer runs on), or null when it's off the stage view.</summary>
        private Vector2? MouseOnStage()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null) return null;
            Vector2 sp = mouse.position.ReadValue();
            if (!cam.pixelRect.Contains(sp)) return null;
            Ray ray = cam.ScreenPointToRay(sp);
            if (Mathf.Abs(ray.direction.z) < 1e-4f) return null;
            Vector3 p = ray.origin + ray.direction * (-ray.origin.z / ray.direction.z);
            return new Vector2(p.x, p.y);
        }

        /// <summary>Grid position for the piece in hand: it lands on whatever is below the mouse, or stays in the air with Shift.</summary>
        private Vector2 Snap(Vector2 point, MapPieceTag piece, bool inAir)
        {
            var kind = stage.Find(piece.kind);
            float height = kind.height * piece.size;
            float x = Mathf.Round(point.x / Grid) * Grid;
            if (inAir) return new Vector2(x, Mathf.Max(0f, Mathf.Round((point.y - height * 0.5f) / Grid) * Grid));

            float halfWidth = kind.halfWidth * piece.size * 0.8f;
            float top = 0f; // the stage floor
            for (int i = -1; i <= 1; i++)
            {
                var hits = Physics2D.RaycastAll(new Vector2(x + i * halfWidth, point.y), Vector2.down, 30f);
                foreach (var hit in hits)
                {
                    if (hit.collider.isTrigger || hit.distance < 0.001f || hit.normal.y < 0.5f) continue;
                    if (hit.collider.GetComponentInParent<StagePlayer>() != null) continue;
                    top = Mathf.Max(top, hit.point.y);
                    break; // the first surface below the mouse
                }
            }
            return new Vector2(x, Mathf.Round(top * 100f) / 100f);
        }

        private bool IsValid(MapPieceTag piece)
        {
            var kind = stage.Find(piece.kind);
            Vector3 p = piece.transform.position;
            float halfWidth = kind.halfWidth * piece.size;
            if (heldIsNew && stage.PieceCount >= MapStage.MaxPieces) return false;
            return p.x - halfWidth >= stage.MinPieceX && p.x + halfWidth <= stage.MaxPieceX && p.y + kind.height * piece.size <= MapStage.MaxPieceTop;
        }

        // ------------------------------------------------------------------ editing

        private void SelectBrush(string id)
        {
            if (held != null && heldIsNew && brush == id) { DropHeld(); return; } // clicking it again puts it away
            DropHeld();
            brush = id;
            held = stage.Spawn(id, -1000f, -1000f, brushSize, brushFlip, false, ghostHolder);
            heldIsNew = true;
            heldValid = true;
            if (held != null) { held.gameObject.SetActive(false); Tint(held, GhostTint); }
            RefreshTiles();
        }

        private void PickUp(MapPieceTag piece)
        {
            Tint(piece, null);
            hover = null;
            held = piece;
            heldIsNew = false;
            heldValid = true;
            heldFrom = piece.transform.position;
            heldFromSize = piece.size;
            heldFromFlip = piece.flip;
            if (piece.colliderObject != null) piece.colliderObject.SetActive(false);
            Tint(piece, GhostTint);
            brush = null;
            RefreshTiles();
        }

        private void Place()
        {
            Vector3 p = held.transform.position;
            if (heldIsNew)
            {
                stage.Spawn(held.kind, p.x, p.y, held.size, held.flip, true, null); // and keep the piece in hand for the next one
            }
            else
            {
                if (held.colliderObject != null) held.colliderObject.SetActive(true);
                Tint(held, null);
                held = null;
            }
            MarkChanged();
        }

        /// <summary>Puts the piece in hand away: a piece from the list disappears, a piece being moved goes back where it was.</summary>
        private void DropHeld()
        {
            if (held == null) { brush = null; RefreshTiles(); return; }
            if (heldIsNew)
            {
                held.gameObject.SetActive(false);
                Destroy(held.gameObject);
            }
            else
            {
                MapStage.Pose(held, heldFrom.x, heldFrom.y, heldFromSize, heldFromFlip);
                held.gameObject.SetActive(true);
                if (held.colliderObject != null) held.colliderObject.SetActive(true);
                Tint(held, null);
            }
            held = null;
            brush = null;
            RefreshTiles();
        }

        private void DeleteHeld()
        {
            if (held == null) return;
            if (heldIsNew) { DropHeld(); return; }
            DeletePiece(held);
            held = null;
            RefreshTiles();
        }

        private void DeletePiece(MapPieceTag piece)
        {
            if (piece == hover) hover = null;
            piece.gameObject.SetActive(false);
            Destroy(piece.gameObject);
            MarkChanged();
        }

        private void Resize(int by)
        {
            if (held == null) return;
            int i = System.Array.IndexOf(Sizes, held.size);
            if (i < 0) i = 2;
            i = Mathf.Clamp(i + by, 0, Sizes.Length - 1);
            Vector3 p = held.transform.position;
            MapStage.Pose(held, p.x, p.y, Sizes[i], held.flip);
            if (heldIsNew) brushSize = Sizes[i];
        }

        private void Flip()
        {
            if (held == null) return;
            Vector3 p = held.transform.position;
            MapStage.Pose(held, p.x, p.y, held.size, !held.flip);
            if (heldIsNew) brushFlip = held.flip;
        }

        private void MarkChanged()
        {
            unsaved = true;
            exitArmed = false;
        }

        // ------------------------------------------------------------------ save / test / exit

        private bool Save()
        {
            if (held != null && !heldIsNew) DropHeld();
            string name = MapStorage.CleanName(nameField.text);
            if (name.Length == 0)
            {
                Status("TYPE A NAME FOR YOUR MAP FIRST (TOP LEFT)", 3f);
                nameField.Select();
                nameField.ActivateInputField();
                return false;
            }
            var existing = MapStorage.Find(name);
            if (existing != null && !SamePath(existing.path, loadedPath) && overwriteArmed != name)
            {
                overwriteArmed = name;
                Status("THERE IS ALREADY A MAP CALLED " + name + " - PRESS SAVE AGAIN TO REPLACE IT", 4f);
                return false;
            }
            try
            {
                var map = stage.ToMap(name);
                loadedPath = MapStorage.Save(map, loadedPath);
                nameField.SetTextWithoutNotify(name);
                GameSettings.MapName = name; // the map to play on
                unsaved = exitArmed = false;
                overwriteArmed = null;
                Status("SAVED  " + name, 2.5f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Pimba Stage: could not save the map: " + e);
                Status("COULD NOT SAVE THE MAP", 3f);
                return false;
            }
        }

        private static bool SamePath(string a, string b) =>
            !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), System.StringComparison.OrdinalIgnoreCase);

        private void Test()
        {
            if (Save()) game.PlayMapNow(MapStorage.CleanName(nameField.text));
        }

        private void Exit()
        {
            if (unsaved && !exitArmed)
            {
                exitArmed = true;
                Status("NOT SAVED!  PRESS EXIT AGAIN TO LEAVE WITHOUT SAVING", 4f);
                return;
            }
            IsOpen = false;
            game.LeaveCreator();
        }

        // ------------------------------------------------------------------ looks

        private void Status(string text, float seconds)
        {
            status.text = text;
            status.color = Gold;
            status.enabled = true;
            statusUntil = Time.unscaledTime + seconds;
        }

        private void RefreshTiles()
        {
            foreach (var tile in tiles) tile.back.color = tile.id == brush && held != null ? Gold : ButtonColor;
        }

        /// <summary>Tints a piece (null = its own colours again).</summary>
        private static void Tint(MapPieceTag piece, Color? tint)
        {
            if (piece == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                r.SetPropertyBlock(null);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (tint == null || mats[i] == null) { r.SetPropertyBlock(null, i); continue; }
                    block.Clear();
                    Color baseColor = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor")
                        : mats[i].HasProperty("_Color") ? mats[i].GetColor("_Color") : Color.white;
                    Color c = baseColor * tint.Value;
                    c.a = baseColor.a;
                    block.SetColor("_BaseColor", c);
                    block.SetColor("_Color", c);
                    r.SetPropertyBlock(block, i);
                }
            }
        }

        private void Build(RectTransform playfield)
        {
            root = MakeRect("Map Creator", playfield, Vector2.zero, Vector2.one);
            root.gameObject.SetActive(false);

            // Top bar: name, SAVE, TEST, EXIT
            var bar = MakeWindow("Top Bar", root, new Vector2(0.075f, 0.885f), new Vector2(0.925f, 0.985f), sprite, 5f);
            AddText("Name Label", bar, new Vector2(0.015f, 0f), new Vector2(0.15f, 1f), "MAP NAME", font, 24, Gold, TextAnchor.MiddleLeft);
            var field = MakeRect("Map Name", bar, new Vector2(0.15f, 0.16f), new Vector2(0.45f, 0.84f));
            var fieldBack = AddImage(field, sprite, new Color(0.22f, 0.11f, 0.15f), true);
            var textRect = MakeRect("Text", field, Vector2.zero, Vector2.one);
            textRect.offsetMin = new Vector2(14f, 0f);
            textRect.offsetMax = new Vector2(-14f, 0f);
            var text = textRect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 30;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var placeholderRect = MakeRect("Placeholder", field, Vector2.zero, Vector2.one);
            placeholderRect.offsetMin = textRect.offsetMin;
            placeholderRect.offsetMax = textRect.offsetMax;
            var placeholder = placeholderRect.gameObject.AddComponent<Text>();
            placeholder.font = font;
            placeholder.fontSize = 26;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = "TYPE A NAME";
            nameField = field.gameObject.AddComponent<InputField>();
            nameField.textComponent = text;
            nameField.placeholder = placeholder;
            nameField.targetGraphic = fieldBack;
            nameField.characterLimit = MapStorage.MaxNameLength;
            nameField.lineType = InputField.LineType.SingleLine;
            nameField.navigation = new Navigation { mode = Navigation.Mode.None };
            nameField.onValidateInput += (s, i, c) =>
            {
                c = char.ToUpperInvariant(c);
                return (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '-' ? c : '\0';
            };
            nameField.onValueChanged.AddListener(_ => { MarkChanged(); overwriteArmed = null; });

            info = AddText("Info", bar, new Vector2(0.46f, 0f), new Vector2(0.64f, 1f), "", font, 20, HintColor);
            BarButton(bar, "SAVE", 0.645f, () => Save());
            BarButton(bar, "TEST", 0.76f, Test);
            BarButton(bar, "EXIT", 0.875f, Exit);

            // Messages
            status = AddText("Message", root, new Vector2(0.1f, 0.8f), new Vector2(0.9f, 0.87f), "", font, 30, Gold);
            status.enabled = false;
            startLabel = AddText("Start", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "START", font, 30, new Color(0.55f, 1f, 0.55f));
            startLabel.rectTransform.sizeDelta = new Vector2(200f, 40f);
            finishLabel = AddText("Finish", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "FINISH", font, 30, Gold);
            finishLabel.rectTransform.sizeDelta = new Vector2(200f, 40f);

            // Object list: the game's 3D pieces
            var palette = MakeWindow("Object List", root, new Vector2(0.075f, 0.015f), new Vector2(0.925f, 0.2f), sprite, 5f);
            for (int i = 0; i < stage.Kinds.Count; i++)
            {
                var kind = stage.Kinds[i];
                var tile = MakeRectAt(kind.label, palette, new Vector2(0.075f + i * 0.13f, 0.5f), new Vector2(150f, 170f), Vector2.zero);
                var button = AddButton(tile, sprite, font, "", 20, ButtonColor, false, out _);
                var pictureRect = MakeRect("Picture", tile, new Vector2(0.07f, 0.25f), new Vector2(0.93f, 0.95f));
                var picture = pictureRect.gameObject.AddComponent<RawImage>();
                picture.raycastTarget = false;
                AddText("Name", tile, new Vector2(0f, 0.02f), new Vector2(1f, 0.25f), kind.label, font, 22, Color.white);
                string id = kind.id;
                button.onClick.AddListener(() => SelectBrush(id));
                tiles.Add((id, (Image)button.targetGraphic, picture));
            }
            AddText("Help", palette, new Vector2(0.69f, 0.05f), new Vector2(0.99f, 0.95f),
                "LEFT CLICK   place / pick up\nRIGHT CLICK   remove\nQ / E or WHEEL   size     F   flip\nSHIFT   place in the air\nA / D   move along the stage",
                font, 19, HintColor, TextAnchor.MiddleLeft);
        }

        private void BarButton(RectTransform bar, string label, float x, UnityEngine.Events.UnityAction click)
        {
            var rt = MakeRect(label, bar, new Vector2(x, 0.14f), new Vector2(x + 0.105f, 0.86f));
            var b = AddButton(rt, sprite, font, label, 28, ButtonColor, false, out _);
            b.onClick.AddListener(click);
        }
    }
}
