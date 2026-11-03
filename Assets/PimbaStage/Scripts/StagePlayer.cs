using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PimbaStage
{
    /// <summary>
    /// Player 1, the performer doing the parkour on stage (grown from the original CarPlayer:
    /// momentum running with acceleration and friction, plus jumping).
    /// Keyboard: A / D to move, W or Space to jump (press again in the air for a double jump).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class StagePlayer : MonoBehaviour
    {
        [Header("Running")]
        [SerializeField] private float maxSpeed = 7f;
        [SerializeField] private float acceleration = 70f;
        [SerializeField] private float friction = 60f;
        [SerializeField] private float turnFriction = 110f;
        [Range(0f, 1f)] [SerializeField] private float airControl = 0.7f;

        [Header("Jumping")]
        [SerializeField] private float jumpHeight = 2.7f;
        [SerializeField] private int extraAirJumps = 1;
        [SerializeField] private float gravityScale = 3f;
        [SerializeField] private float fallGravityMultiplier = 1.6f;
        [SerializeField] private float lowJumpGravityMultiplier = 2.2f;
        [SerializeField] private float maxFallSpeed = 22f;
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("Player 1 controls")]
        [Tooltip("Which connected gamepad Player 1 uses (0 = first). A/D/W/Space always work.")]
        public int gamepadIndex = 0;

        [Header("Visuals")]
        public Transform model;
        [Tooltip("Pool of light on the stage floor that follows the performer.")]
        public Transform floorSpot;
        [Tooltip("Plays the character's frames; runs faster when moving.")]
        public SpriteFrameAnimator animator;
        [Tooltip("Tick if the character art looks the wrong way when running right.")]
        public bool spriteFacesLeft;

        [Header("Animations (Characters/PerformerIdle_*, Walk_*, Jump_*, Fall_*, Hurt_*)")]
        [Tooltip("Standing still.")]
        public Sprite[] idleFrames = new Sprite[0];
        [Tooltip("Moving on the ground (plays faster the faster he runs).")]
        public Sprite[] walkFrames = new Sprite[0];
        [Tooltip("In the air, going up.")]
        public Sprite[] jumpFrames = new Sprite[0];
        [Tooltip("In the air, going down.")]
        public Sprite[] fallFrames = new Sprite[0];
        [Tooltip("Plays once when he gets damaged.")]
        public Sprite[] hurtFrames = new Sprite[0];
        [Tooltip("Frames per second (the idle GIF uses 170 ms per frame).")]
        public float idleFps = 5.9f;
        [Tooltip("Frames per second at walking pace (the walk GIF uses 170 ms per frame).")]
        public float walkFps = 5.9f;
        public float jumpFps = 10f;
        [Tooltip("Frames per second (the hurt GIF uses 140 ms per frame).")]
        public float hurtFps = 7.1f;

        public bool ControlsEnabled { get; set; }
        public bool Invulnerable => Time.time < invulnerableUntil;
        /// <summary>True while the hurt animation is playing.</summary>
        public bool Hurting => hurtFrames != null && hurtFrames.Length > 0 && Time.time < hurtUntil;
        public bool OnGround => onGround;
        public Rigidbody2D Body => rb;
        public float HalfWidth => col.bounds.extents.x;

        private Rigidbody2D rb;
        private CapsuleCollider2D col;
        private ContactFilter2D solidFilter;
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(8);
        private SpriteRenderer[] renderers;
        private int[] baseOrders;
        private bool underStage;

        private float moveX;
        private bool jumpHeld, onGround, bouncing;
        private int facing = 1, airJumpsLeft;
        private float lastGroundedTime = -999f, lastJumpPressedTime = -999f, lastJumpTime = -999f;
        private float lastVerticalSpeed, invulnerableUntil, hurtUntil;
        private Sprite[] defaultFrames;
        private float defaultFps;
        private Vector2 carryVelocity;
        private Vector2 squash = Vector2.one;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<CapsuleCollider2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.gravityScale = gravityScale;
            rb.sharedMaterial = new PhysicsMaterial2D("PerformerNoFriction") { friction = 0f, bounciness = 0f };

            solidFilter = new ContactFilter2D { useTriggers = false };
            solidFilter.SetLayerMask(Physics2D.DefaultRaycastLayers);

            if (model == null) model = transform.Find("Model");
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseOrders = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) baseOrders[i] = renderers[i].sortingOrder;
            if (animator != null)
            {
                defaultFrames = animator.frames;
                defaultFps = animator.framesPerSecond;
            }
        }

        private void Update()
        {
            moveX = 0f;
            bool jumpPressed = false;
            jumpHeld = false;

            if (ControlsEnabled)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.aKey.isPressed) moveX -= 1f;
                    if (kb.dKey.isPressed) moveX += 1f;
                    jumpPressed |= kb.wKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                    jumpHeld |= kb.wKey.isPressed || kb.spaceKey.isPressed;
                }
                var pad = gamepadIndex >= 0 && gamepadIndex < Gamepad.all.Count ? Gamepad.all[gamepadIndex] : null;
                if (pad != null)
                {
                    float stick = pad.leftStick.ReadValue().x;
                    if (pad.dpad.left.isPressed) stick = -1f;
                    if (pad.dpad.right.isPressed) stick = 1f;
                    if (Mathf.Abs(stick) > 0.25f) moveX = Mathf.Clamp(moveX + stick, -1f, 1f);
                    jumpPressed |= pad.buttonSouth.wasPressedThisFrame;
                    jumpHeld |= pad.buttonSouth.isPressed;
                }
            }

            if (Mathf.Abs(moveX) > 0.1f) facing = moveX > 0f ? 1 : -1;
            if (jumpPressed) lastJumpPressedTime = Time.time;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool wasGrounded = onGround;
            GroundCheck();
            if (onGround)
            {
                lastGroundedTime = Time.time;
                airJumpsLeft = extraAirJumps;
                if (!wasGrounded && lastVerticalSpeed < -8f) squash = new Vector2(1.3f, 0.7f);
            }

            Vector2 v = rb.linearVelocity;

            float speed = v.x - carryVelocity.x;
            float target = moveX * maxSpeed;
            float rate;
            if (Mathf.Abs(moveX) > 0.01f)
            {
                bool reversing = Mathf.Abs(speed) > 0.1f && Mathf.Sign(moveX) != Mathf.Sign(speed);
                rate = reversing ? turnFriction : acceleration;
            }
            else rate = friction;
            if (!onGround) rate *= airControl;
            speed = Mathf.MoveTowards(speed, target, rate * dt);
            v.x = speed + carryVelocity.x;

            if (Time.time - lastJumpPressedTime <= jumpBufferTime)
            {
                if (Time.time - lastGroundedTime <= coyoteTime) Jump(ref v);
                else if (airJumpsLeft > 0)
                {
                    airJumpsLeft--;
                    Jump(ref v);
                }
            }

            float scale = gravityScale;
            if (v.y < 0f) scale *= fallGravityMultiplier;
            else if (v.y > 0f && !jumpHeld && !bouncing) scale *= lowJumpGravityMultiplier;
            if (v.y <= 0f) bouncing = false;
            rb.gravityScale = scale;
            v.y = Mathf.Max(v.y, -maxFallSpeed);

            lastVerticalSpeed = v.y;
            rb.linearVelocity = v;
        }

        private void LateUpdate()
        {
            if (model != null)
            {
                squash = Vector2.Lerp(squash, Vector2.one, 12f * Time.deltaTime);
                float flip = spriteFacesLeft ? -facing : facing;
                model.localScale = new Vector3(squash.x * flip, squash.y, 1f);
            }

            if (animator != null) Animate();

            // Blink while invulnerable (not during the hurt animation, which flashes by itself)
            bool visible = !Invulnerable || Hurting || ((int)(Time.time * 14f)) % 2 == 0;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = visible;

            // Drop behind the stage edge when falling into a trapdoor
            bool under = transform.position.y < -0.35f;
            if (under != underStage)
            {
                underStage = under;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].sortingOrder = under ? -32 : baseOrders[i];
            }

            if (floorSpot != null)
            {
                Vector3 p = floorSpot.position;
                floorSpot.position = new Vector3(transform.position.x, p.y, p.z);
            }
        }

        /// <summary>Picks the animation for what the performer is doing: hurt, jump, fall, walk or idle.</summary>
        private void Animate()
        {
            float run = Mathf.Abs(rb.linearVelocity.x - carryVelocity.x);
            // A short grace time so a one-frame gap in the ground check doesn't flash the fall frames.
            bool airborne = !onGround && Time.time - lastGroundedTime > 0.06f;
            bool moving = run > 0.3f || Mathf.Abs(moveX) > 0.1f;

            Sprite[] clip;
            float fps, speed = 1f;
            if (Hurting)
            {
                clip = hurtFrames;
                fps = hurtFps;
            }
            else if (airborne)
            {
                bool rising = rb.linearVelocity.y > 0.1f;
                clip = rising ? Either(jumpFrames, fallFrames) : Either(fallFrames, jumpFrames);
                fps = jumpFps;
            }
            else if (moving)
            {
                clip = walkFrames;
                fps = walkFps;
                speed = Mathf.Lerp(0.8f, 1.5f, run / Mathf.Max(0.1f, maxSpeed));
            }
            else
            {
                clip = idleFrames;
                fps = idleFps;
            }

            if (clip == null || clip.Length == 0)
            {
                // No art for this move: keep the one looping animation, sped up when moving.
                clip = defaultFrames;
                fps = defaultFps;
                speed = airborne ? 1.4f : moving ? Mathf.Lerp(0.9f, 1.6f, run / Mathf.Max(0.1f, maxSpeed)) : 0.45f;
            }
            animator.SpeedMultiplier = speed;
            animator.Play(clip, fps);
        }

        private static Sprite[] Either(Sprite[] first, Sprite[] second) =>
            first != null && first.Length > 0 ? first : second;

        /// <summary>Plays the hurt animation once (called by the game when the performer loses a heart).</summary>
        public void PlayHurt()
        {
            if (hurtFrames == null || hurtFrames.Length == 0) return;
            hurtUntil = Time.time + hurtFrames.Length / Mathf.Max(0.1f, hurtFps);
            if (animator != null)
            {
                animator.SpeedMultiplier = 1f;
                animator.Play(hurtFrames, hurtFps, true);
            }
        }

        private void GroundCheck()
        {
            onGround = false;
            carryVelocity = Vector2.zero;
            if (Time.time - lastJumpTime < 0.1f) return;

            int count = rb.Cast(Vector2.down, solidFilter, hits, 0.08f);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].normal.y <= 0.6f) continue;
                onGround = true;
                var piece = hits[i].collider.GetComponentInParent<MovingSetPiece>();
                if (piece != null) carryVelocity = piece.Velocity;
                break;
            }
        }

        private void Jump(ref Vector2 v)
        {
            float gravity = -Physics2D.gravity.y * gravityScale;
            v.y = Mathf.Sqrt(2f * gravity * jumpHeight) + Mathf.Max(0f, carryVelocity.y);
            onGround = false;
            lastJumpPressedTime = -999f;
            lastGroundedTime = -999f;
            lastJumpTime = Time.time;
            squash = new Vector2(0.75f, 1.3f);
        }

        /// <summary>True if a wall is directly to the right (used to detect being squashed by the scrolling stage).</summary>
        public bool IsBlockedRight(float distance)
        {
            int count = rb.Cast(Vector2.right, solidFilter, hits, distance);
            for (int i = 0; i < count; i++)
                if (hits[i].normal.x < -0.5f) return true;
            return false;
        }

        public void Knockback(float fromX)
        {
            float dir = transform.position.x < fromX ? -1f : 1f;
            rb.linearVelocity = new Vector2(dir * 5f, 9f);
            bouncing = true;
            lastJumpTime = Time.time;
        }

        public void MakeInvulnerable(float seconds)
        {
            invulnerableUntil = Time.time + seconds;
        }

        public void Respawn(Vector2 position)
        {
            rb.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            rb.linearVelocity = Vector2.zero;
            carryVelocity = Vector2.zero;
            bouncing = false;
            lastJumpPressedTime = -999f;
        }

        /// <summary>Little dance bounce on the beat when standing still.</summary>
        public void OnBeat()
        {
            if (onGround && Mathf.Abs(rb.linearVelocity.x) < 0.5f) squash = new Vector2(1.12f, 0.88f);
        }
    }
}
