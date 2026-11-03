using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Platformer2D
{
    /// <summary>
    /// 2D side-scrolling platformer player, rebuilt from the original CarPlayer.
    ///
    /// What carried over from CarPlayer:
    ///   - MoveAction / moveInput (Input System)      -> left/right running
    ///   - speed + friction (gradual acceleration)      -> momentum-based running and skidding
    ///   - onGround (was a y-position check)            -> real ground detection (Rigidbody2D.Cast)
    ///   - currentSpeed (+1 / -1 direction)             -> facing (+1 right / -1 left)
    ///   - N key boost (speed *= 1.1 every frame)       -> hold N (or Shift) to run faster
    ///   - T key scale (doubled every frame held)       -> press T to toggle giant mode
    /// New: jump, double jump, coyote time, jump buffering, variable jump height,
    ///      moving-platform riding, bounce pads, respawning.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class PlatformerPlayer : MonoBehaviour
    {
        [Header("Running (momentum, like the car)")]
        [SerializeField] private float maxSpeed = 8f;
        [SerializeField] private float acceleration = 60f;
        [Tooltip("How fast you slow down on the ground with no input.")]
        [SerializeField] private float friction = 50f;
        [Tooltip("Deceleration when pushing the opposite way (skid).")]
        [SerializeField] private float turnFriction = 90f;
        [Range(0f, 1f)]
        [SerializeField] private float airControl = 0.65f;
        [Tooltip("Speed multiplier while holding N / Shift.")]
        [SerializeField] private float boostMultiplier = 1.6f;

        [Header("Jumping")]
        [SerializeField] private float jumpHeight = 3.2f;
        [SerializeField] private int extraAirJumps = 1;
        [SerializeField] private float gravityScale = 3f;
        [SerializeField] private float fallGravityMultiplier = 1.6f;
        [Tooltip("Extra gravity when jump is released early (short hop).")]
        [SerializeField] private float lowJumpGravityMultiplier = 2.2f;
        [SerializeField] private float maxFallSpeed = 22f;
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Header("Ground check")]
        [SerializeField] private LayerMask groundMask = Physics2D.DefaultRaycastLayers;
        [SerializeField] private float groundCheckDistance = 0.08f;

        [Header("Visuals")]
        [Tooltip("Child that holds the player's sprites (flipped to face the run direction).")]
        [SerializeField] private Transform model;
        [SerializeField] private float giantScale = 2f;

        [Header("State (read only)")]
        [SerializeField] private bool onGround;
        [SerializeField] private float speed;
        [SerializeField] private int facing = 1;

        [Header("Input (default bindings are added if left empty)")]
        public InputAction MoveAction;
        public InputAction JumpAction;
        public InputAction BoostAction;
        public InputAction GrowAction;
        public Vector2 moveInput;

        private Rigidbody2D rb;
        private ContactFilter2D groundFilter;
        private readonly List<RaycastHit2D> groundHits = new List<RaycastHit2D>(8);
        private Vector2 carryVelocity;      // velocity of the moving platform we stand on
        private Vector2 squash = Vector2.one;
        private bool jumpHeld, boosting, giant, bouncing;
        private int airJumpsLeft;
        private float lastGroundedTime = -999f;
        private float lastJumpPressedTime = -999f;
        private float lastJumpTime = -999f;
        private float lastVerticalSpeed;

        public bool OnGround => onGround;
        public int Facing => facing;
        public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;

        /// <summary>Returns the player that owns this collider, or null.</summary>
        public static PlatformerPlayer FromCollider(Collider2D other)
        {
            if (other == null) return null;
            var body = other.attachedRigidbody;
            return body != null ? body.GetComponent<PlatformerPlayer>() : other.GetComponentInParent<PlatformerPlayer>();
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.gravityScale = gravityScale;
            // Zero friction stops the player sticking to walls.
            rb.sharedMaterial = new PhysicsMaterial2D("PlayerNoFriction") { friction = 0f, bounciness = 0f };

            groundFilter = new ContactFilter2D { useTriggers = false };
            groundFilter.SetLayerMask(groundMask);

            if (model == null) model = transform.Find("Model");
            SetupDefaultBindings();
        }

        private void OnEnable()
        {
            MoveAction.Enable();
            JumpAction.Enable();
            BoostAction.Enable();
            GrowAction.Enable();
        }

        private void OnDisable()
        {
            MoveAction.Disable();
            JumpAction.Disable();
            BoostAction.Disable();
            GrowAction.Disable();
        }

        private void Update()
        {
            moveInput = MoveAction.ReadValue<Vector2>();
            if (Mathf.Abs(moveInput.x) > 0.1f) facing = moveInput.x > 0f ? 1 : -1;

            if (JumpAction.WasPressedThisFrame()) lastJumpPressedTime = Time.time;
            jumpHeld = JumpAction.IsPressed();
            boosting = BoostAction.IsPressed();
            if (GrowAction.WasPressedThisFrame()) giant = !giant;

            // Giant mode (the old T key), eased instead of doubling every frame.
            Vector3 targetScale = new Vector3(1f, 1f, 1f) * (giant ? giantScale : 1f);
            if (transform.localScale != targetScale)
                transform.localScale = Vector3.MoveTowards(transform.localScale, targetScale, 4f * Time.deltaTime);

            if (model != null)
            {
                squash = Vector2.Lerp(squash, Vector2.one, 12f * Time.deltaTime);
                model.localScale = new Vector3(squash.x * facing, squash.y, 1f);
            }
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

            // ---- Horizontal: accelerate / brake like the car did ----
            speed = v.x - carryVelocity.x;
            float target = moveInput.x * maxSpeed * (boosting ? boostMultiplier : 1f);
            float rate;
            if (Mathf.Abs(moveInput.x) > 0.01f)
            {
                bool reversing = Mathf.Abs(speed) > 0.1f && Mathf.Sign(moveInput.x) != Mathf.Sign(speed);
                rate = reversing ? turnFriction : acceleration;
            }
            else
            {
                rate = friction;
            }
            if (!onGround) rate *= airControl;
            speed = Mathf.MoveTowards(speed, target, rate * dt);
            v.x = speed + carryVelocity.x;

            // ---- Jumping (buffered press + coyote time, then air jumps) ----
            if (Time.time - lastJumpPressedTime <= jumpBufferTime)
            {
                if (Time.time - lastGroundedTime <= coyoteTime)
                {
                    Jump(ref v);
                }
                else if (airJumpsLeft > 0)
                {
                    airJumpsLeft--;
                    Jump(ref v);
                }
            }

            // ---- Snappier gravity: fall faster, short hop when jump is released ----
            float scale = gravityScale;
            if (v.y < 0f) scale *= fallGravityMultiplier;
            else if (v.y > 0f && !jumpHeld && !bouncing) scale *= lowJumpGravityMultiplier;
            if (v.y <= 0f) bouncing = false;
            rb.gravityScale = scale;
            v.y = Mathf.Max(v.y, -maxFallSpeed);

            lastVerticalSpeed = v.y;
            rb.linearVelocity = v;
        }

        private void GroundCheck()
        {
            onGround = false;
            carryVelocity = Vector2.zero;
            if (Time.time - lastJumpTime < 0.1f) return; // just jumped, don't snap back to ground

            int count = rb.Cast(Vector2.down, groundFilter, groundHits, groundCheckDistance);
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = groundHits[i];
                if (hit.normal.y <= 0.6f) continue; // walls don't count
                onGround = true;
                var platform = hit.collider.GetComponentInParent<MovingPlatform>();
                if (platform != null) carryVelocity = platform.Velocity;
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

        /// <summary>Launches the player upward (used by bounce pads).</summary>
        public void Bounce(float upSpeed)
        {
            Vector2 v = rb.linearVelocity;
            v.y = upSpeed;
            rb.linearVelocity = v;
            bouncing = true;
            onGround = false;
            airJumpsLeft = extraAirJumps;
            lastGroundedTime = -999f;
            lastJumpTime = Time.time;
            squash = new Vector2(0.7f, 1.4f);
        }

        /// <summary>Teleports the player and clears its motion.</summary>
        public void Respawn(Vector2 position)
        {
            rb.position = position;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            rb.linearVelocity = Vector2.zero;
            speed = 0f;
            carryVelocity = Vector2.zero;
            bouncing = false;
            lastJumpPressedTime = -999f;
        }

        private void SetupDefaultBindings()
        {
            if (MoveAction == null || MoveAction.bindings.Count == 0)
            {
                MoveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
                MoveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
                MoveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
                MoveAction.AddBinding("<Gamepad>/leftStick");
                MoveAction.AddBinding("<Gamepad>/dpad");
            }
            if (JumpAction == null || JumpAction.bindings.Count == 0)
            {
                JumpAction = new InputAction("Jump", InputActionType.Button);
                JumpAction.AddBinding("<Keyboard>/space");
                JumpAction.AddBinding("<Keyboard>/w");
                JumpAction.AddBinding("<Keyboard>/upArrow");
                JumpAction.AddBinding("<Gamepad>/buttonSouth");
            }
            if (BoostAction == null || BoostAction.bindings.Count == 0)
            {
                BoostAction = new InputAction("Boost", InputActionType.Button);
                BoostAction.AddBinding("<Keyboard>/n");
                BoostAction.AddBinding("<Keyboard>/leftShift");
                BoostAction.AddBinding("<Gamepad>/buttonWest");
            }
            if (GrowAction == null || GrowAction.bindings.Count == 0)
            {
                GrowAction = new InputAction("Grow", InputActionType.Button);
                GrowAction.AddBinding("<Keyboard>/t");
                GrowAction.AddBinding("<Gamepad>/buttonNorth");
            }
        }
    }
}
