using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public static PlayerMovement Instance { get; private set; }

    [Header("References")]
    public CharacterController controller;
    public Transform cameraHead;     // Camera Head child — mouse look rotates this
    public Transform bobTarget;      // Bob Target child — head bob applies here
    public Transform graphics;       // Graphics child (mesh) — optional
    public Camera mainCam;
    public Camera overlayCam;

    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float crouchSpeed = 2.5f;
    public float groundAcceleration = 50f;  // how snappy ground movement feels
    public float groundDeceleration = 40f;  // how fast you stop on ground
    public float speedMultiplier = 1f;      // overall multiplier — change from outside at runtime
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("Air Control")]
    [Range(0f, 1f)]
    public float airControlAmount = 0.4f;   // 0 = no air control, 1 = full ground-like control
    public float airAcceleration = 15f;     // how fast you can steer mid-air
    public float airDrag = 0.2f;            // passive slowdown when no input in air

    [Header("Jumping")]
    public float jumpForce = 7f;
    public float gravity = -20f;
    public int airJumps = 0;                // extra jumps while airborne (change at runtime)
    public float coyoteTime = 0.12f;        // grace period after leaving ground where you can still jump

    [Header("Ground Check")]
    public float groundCheckRadius = 0.3f;
    public float groundCheckDistance = 0.15f;
    public LayerMask groundMask = ~0;       // set in inspector to exclude player layer

    [Header("Crouch")]
    public float standHeight = 2f;
    public float crouchHeight = 1.2f;
    public float crouchTransitionSpeed = 10f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 89f;
    public bool invertY = false;

    [Header("Head Bob")]
    public bool enableBob = true;
    public float bobFrequency = 10f;
    public float bobAmountY = 0.05f;
    public float bobAmountX = 0.03f;
    public float sprintBobMultiplier = 1.4f;
    public float crouchBobMultiplier = 0.5f;
    public float bobSmooth = 8f;

    [Header("Jump / Land Particles")]
    public GameObject jumpLandParticlesPrefab;       // leave empty to disable — nothing changes if null
    public bool playParticlesOnJump = true;
    public bool playParticlesOnLand = true;
    public float minLandFallSpeed = 3f;              // how fast you must be falling for the land burst to play
    public float particlesYOffset = 0.05f;           // small lift so the burst isn't buried in the floor
    public float particlesLifetime = 3f;             // seconds before the spawned object is destroyed (0 = never)

    [Header("Dynamic FOV")]
    public bool enableDynamicFov = true;
    [Range(0f, 1f)] public float dynamicFovIntensity = 1f; // Overall multiplier for the FOV effect
    public bool applyFovWhenRunning = true;
    public bool applyFovWhenWalking = false;
    public bool applyFovWhenInAir = true;
    public bool includeVerticalVelocity = false; // If true, falling/jumping speed also increases FOV
    public float fovHorizontalMinSpeed = 5f;
    public float fovVerticalMinSpeed = 5f;
    public float fovHorizontalMultiplier = 1f;
    public float fovVerticalMultiplier = 1f;
    public float fovSpeedMultiplier = 1f;   // How much FOV increases per unit of speed
    public float maxFov = 130f;             // Hard cap so it doesn't break the camera
    public float fovSmoothSpeed = 8f;       // How fast FOV transitions

    // ─── Runtime state (read-only from outside) ────────────────────────
    [Header("Fall shake")]
    public GuardJuice juice;                // camera shake (found on this object if empty)
    public float fallShakeStartSpeed = 6f;  // falling faster than this starts a very slight shake
    public float fallShakeFullSpeed = 20f;
    public float fallShakeMax = 0.3f;       // degrees at full speed

    [Header("Slide")]
    // sprint + crouch (press) on the ground: a quick slide that slows down by friction
    public bool enableSlide = true;
    public float slideMinSpeed = 6f;        // need to be going about this fast (sprinting)
    public float slideBoost = 2f;           // m/s added when the slide starts
    public float slideMaxSpeed = 12f;
    public float slideFriction = 7f;        // m/s² it slows down by
    public float slideEndSpeed = 3f;        // ends below this
    public float slideMaxTime = 1.2f;
    public float slideSteer = 1.2f;         // rad/s it can be steered with the movement keys
    public float slideCooldown = 0.5f;
    public float slideShake = 0.12f;        // degrees of rumble while sliding
    public Animator handsAnim;              // Camera Head animator ("Sliding" bool); found if empty

    [Header("Knockback")]
    public float knockbackDrag = 10f;       // m/s² it slows down by (10 m/s -> ~5 m)

    [HideInInspector] public Vector3 velocity;
    [HideInInspector] public bool isGrounded;
    [HideInInspector] public bool isSprinting;
    [HideInInspector] public float baseFov = 90f;
     public bool isCrouching;

    // ─── Private ────────────────────────────────────────────────────────
    float xRotation;
    int airJumpsRemaining;
    float bobTimer;
    Vector3 bobTargetLocalOrigin;
    float targetHeight;
    bool movementEnabled = true;
    bool lookEnabled = true;

    // slide
    bool isSliding;
    Vector3 slideDir;
    float slideSpeed, slideTime, nextSlideTime;
    bool hasSlidingParam;
    public bool IsSliding => isSliding;

    Vector3 knockVelocity;      // from Knockback(), decays by knockbackDrag

    // ground / landing sync
    bool wasGroundedLastFrame;
    float lastGroundedTime;     // for coyote time
    bool controllerGroundContact;   // last Move() touched a groundMask surface below us
    float defaultCenterY;

    // crouch visual offsets (original local Y of child transforms)
    float bobTargetOriginalY;
    float graphicsOriginalY;

    void Start()
    {
        Instance = this;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (controller == null)
            controller = GetComponent<CharacterController>();

        standHeight = controller.height;
        targetHeight = standHeight;
        defaultCenterY = controller.center.y;

        if (bobTarget != null)
        {
            bobTargetLocalOrigin = bobTarget.localPosition;
            bobTargetOriginalY = bobTarget.localPosition.y;
        }

        if (graphics != null)
            graphicsOriginalY = graphics.localPosition.y;

        knockVelocity = Vector3.zero;

        if (juice == null) juice = GetComponent<GuardJuice>();
        if (handsAnim == null && cameraHead != null) handsAnim = cameraHead.GetComponent<Animator>();
        hasSlidingParam = handsAnim != null && System.Array.Exists(handsAnim.parameters, p => p.name == "Sliding");
        isSliding = false;
        nextSlideTime = 0f;
        SetSlidingAnim(false);
    }

    void Update()
    {
        // Grab fall speed before the ground check clamps velocity.y
        float fallSpeed = -velocity.y;

        UpdateGroundCheck();

        // Landing this frame
        if (isGrounded && !wasGroundedLastFrame && playParticlesOnLand && fallSpeed >= minLandFallSpeed)
            PlayJumpLandParticles();

        if (lookEnabled)
            HandleMouseLook();

        if (movementEnabled)
        {
            UpdateSlide();
            if (isSliding) HandleSlideMovement();
            else HandleMovement();
            HandleCrouch();
            HandleJump();
        }
        else if (isSliding) EndSlide();

        ApplyGravity();
        // knockback rides on top of the walk velocity (which HandleMovement rewrites every frame)
        controller.Move(velocity * Time.deltaTime + KnockbackStep(Time.deltaTime));

        if (enableBob && movementEnabled)
            HandleHeadBob();

        HandleDynamicFov();
        UpdateMoveShake();

        wasGroundedLastFrame = isGrounded;
    }

    // ══════════════════════════════════════════════════════════════════
    //  GROUND CHECK  (SphereCast from bottom of controller)
    // ══════════════════════════════════════════════════════════════════
    void UpdateGroundCheck()
    {
        // Sphere origin sits just inside the bottom of the capsule
        float bottomY = controller.center.y - controller.height * 0.5f + groundCheckRadius;
        Vector3 sphereOrigin = transform.position + Vector3.up * bottomY;

        isGrounded = Physics.SphereCast(sphereOrigin, groundCheckRadius, Vector3.down,
                        out _, groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);

        // Fallback for edges/slopes the SphereCast misses. controller.isGrounded
        // ignores layers, so only trust it when the surface we actually touched
        // was on the ground mask.
        if (!isGrounded && controller.isGrounded && controllerGroundContact)
            isGrounded = true;

        controllerGroundContact = false;

        if (isGrounded)
        {
            lastGroundedTime = Time.time;

            if (velocity.y < 0f)
                velocity.y = -2f; // small downward force keeps grounded

            airJumpsRemaining = airJumps;
        }
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Only count contacts beneath us that are on the ground mask
        if (hit.normal.y > 0.5f && (groundMask.value & (1 << hit.gameObject.layer)) != 0)
            controllerGroundContact = true;
    }

    // ══════════════════════════════════════════════════════════════════
    //  MOUSE LOOK
    // ══════════════════════════════════════════════════════════════════
    void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        if (invertY) mouseY = -mouseY;

        // Yaw — rotate whole player
        transform.Rotate(Vector3.up * mouseX);

        // Pitch — rotate camera head
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);
        cameraHead.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }

    // ══════════════════════════════════════════════════════════════════
    //  MOVEMENT  (velocity-based, ground vs air)
    // ══════════════════════════════════════════════════════════════════
    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 inputDir = (transform.right * h + transform.forward * v).normalized;
        bool hasInput = inputDir.sqrMagnitude > 0.01f;

        // Determine target speed
        isSprinting = Input.GetKey(sprintKey) && v > 0f && !isCrouching;
        float maxSpeed;

        if (!hasInput)
            maxSpeed = 0f;
        else if (isCrouching)
            maxSpeed = crouchSpeed;
        else if (isSprinting)
            maxSpeed = sprintSpeed;
        else
            maxSpeed = walkSpeed;

        maxSpeed *= speedMultiplier;

        // Current horizontal velocity
        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 targetVel = inputDir * maxSpeed;

        if (isGrounded)
        {
            // ── Grounded: snappy, responsive ──
            // Scale accel/decel by speedMultiplier so movement feels equally
            // responsive at any multiplier (no skating at high values)
            float mul = Mathf.Max(speedMultiplier, 1f);
            float accelRate = (hasInput ? groundAcceleration : groundDeceleration) * mul;
            horizontalVel = Vector3.MoveTowards(horizontalVel, targetVel, accelRate * Time.deltaTime);
        }
        else
        {
            // ── Airborne: momentum + steerable ──
            if (hasInput)
            {
                // Scale air control by sqrt of multiplier — keeps some looseness
                // at high speed but doesn't become uncontrollable
                float airMul = Mathf.Sqrt(Mathf.Max(speedMultiplier, 1f));

                // Blend between current direction and desired direction
                // airControlAmount controls how much influence input has
                Vector3 wishVel = Vector3.Lerp(horizontalVel, targetVel, airControlAmount * airMul * Time.deltaTime * 10f);

                // Also accelerate toward target so you can actually change direction
                horizontalVel = Vector3.MoveTowards(horizontalVel, wishVel, airAcceleration * airMul * Time.deltaTime);

                // Clamp so air control can't exceed max speed
                if (horizontalVel.magnitude > maxSpeed && maxSpeed > 0.01f)
                    horizontalVel = horizontalVel.normalized * Mathf.Max(horizontalVel.magnitude, maxSpeed);
            }
            else
            {
                // No input — apply passive air drag
                if (airDrag > 0f)
                    horizontalVel = Vector3.MoveTowards(horizontalVel, Vector3.zero, airDrag * Time.deltaTime);
            }
        }

        velocity.x = horizontalVel.x;
        velocity.z = horizontalVel.z;
    }

    // ══════════════════════════════════════════════════════════════════
    //  CROUCH
    // ══════════════════════════════════════════════════════════════════
    void HandleCrouch()
    {
        bool wantsCrouch = Input.GetKey(crouchKey) || isSliding;

        if (wantsCrouch && !isCrouching)
        {
            isCrouching = true;
            targetHeight = crouchHeight;
        }
        else if (!wantsCrouch && isCrouching)
        {
            // Check if there is room to stand up — cast from head upward
            float checkDist = standHeight - crouchHeight;
            Vector3 castOrigin = transform.position + Vector3.up * controller.height;
            if (!Physics.Raycast(castOrigin, Vector3.up, checkDist + 0.1f, groundMask, QueryTriggerInteraction.Ignore))
            {
                isCrouching = false;
                targetHeight = standHeight;
            }
        }

        // Smoothly adjust controller height & center
        // Center stays at height/2 so the bottom of the capsule stays at transform.position.y
        if (!Mathf.Approximately(controller.height, targetHeight))
        {
            controller.height = Mathf.MoveTowards(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
            controller.center = new Vector3(0f, controller.height * 0.5f, 0f);
        }

        // Smoothly move child transforms (camera, graphics) to match crouch offset
        float heightDiff = controller.height - standHeight;   // negative when crouching
        float crouchOffset = heightDiff * 0.5f;               // how much to lower children

        if (bobTarget != null)
        {
            Vector3 bobPos = bobTarget.localPosition;
            float targetY = bobTargetOriginalY + crouchOffset;
            bobPos.y = Mathf.MoveTowards(bobPos.y, targetY, crouchTransitionSpeed * Time.deltaTime);
            bobTarget.localPosition = bobPos;

            // Update bob origin so head bob oscillates around the crouched position
            bobTargetLocalOrigin = new Vector3(bobTargetLocalOrigin.x, targetY, bobTargetLocalOrigin.z);
        }

        if (graphics != null)
        {
            Vector3 gfxPos = graphics.localPosition;
            float targetY = graphicsOriginalY + crouchOffset;
            gfxPos.y = Mathf.MoveTowards(gfxPos.y, targetY, crouchTransitionSpeed * Time.deltaTime);
            graphics.localPosition = gfxPos;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  JUMP  (with coyote time)
    // ══════════════════════════════════════════════════════════════════
    void HandleJump()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // Coyote time: allow jumping shortly after leaving ground
            bool canGroundJump = isGrounded || (Time.time - lastGroundedTime <= coyoteTime);

            if (canGroundJump)
            {
                velocity.y = jumpForce;
                lastGroundedTime = -1f; // consume coyote time
                if (isSliding) EndSlide();   // slide-jump: keeps the slide's speed into the air

                if (playParticlesOnJump)
                    PlayJumpLandParticles();
            }
            else if (airJumpsRemaining > 0)
            {
                velocity.y = jumpForce;
                airJumpsRemaining--;

                if (playParticlesOnJump)
                    PlayJumpLandParticles();
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GRAVITY
    // ══════════════════════════════════════════════════════════════════
    void ApplyGravity()
    {
        velocity.y += gravity * Time.deltaTime;
    }

    // ══════════════════════════════════════════════════════════════════
    //  JUMP / LAND PARTICLES
    // ══════════════════════════════════════════════════════════════════
    void PlayJumpLandParticles()
    {
        if (jumpLandParticlesPrefab == null) return;

        GameObject fx = Instantiate(jumpLandParticlesPrefab,
                                    transform.position + Vector3.up * particlesYOffset,
                                    Quaternion.identity);

        if (particlesLifetime > 0f)
            Destroy(fx, particlesLifetime);
    }

    // ══════════════════════════════════════════════════════════════════
    //  HEAD BOB
    // ══════════════════════════════════════════════════════════════════
    void HandleHeadBob()
    {
        if (bobTarget == null) return;

        float speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
        bool isMoving = speed > 0.2f && isGrounded && !isSliding;

        if (isMoving)
        {
            float multiplier = isSprinting ? sprintBobMultiplier
                             : isCrouching ? crouchBobMultiplier
                             : 1f;

            bobTimer += Time.deltaTime * bobFrequency * multiplier;

            float bobY = Mathf.Sin(bobTimer) * bobAmountY * multiplier;
            float bobX = Mathf.Cos(bobTimer * 0.5f) * bobAmountX * multiplier;

            Vector3 targetPos = bobTargetLocalOrigin + new Vector3(bobX, bobY, 0f);
            bobTarget.localPosition = Vector3.Lerp(bobTarget.localPosition, targetPos, Time.deltaTime * bobSmooth);
        }
        else
        {
            // Smoothly return to origin
            bobTimer = 0f;
            bobTarget.localPosition = Vector3.Lerp(bobTarget.localPosition, bobTargetLocalOrigin, Time.deltaTime * bobSmooth);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  DYNAMIC FOV
    // ══════════════════════════════════════════════════════════════════
    void HandleDynamicFov()
    {
        if (mainCam == null || overlayCam == null) return;

        float targetFov = baseFov;

        if (enableDynamicFov)
        {
            bool shouldApplyFov = (!isGrounded && applyFovWhenInAir) || 
                                  (isGrounded && (isSprinting || isSliding) && applyFovWhenRunning) ||
                                  (isGrounded && !isSprinting && applyFovWhenWalking);

            if (shouldApplyFov)
            {
                float rawHoriz = new Vector3(velocity.x, 0f, velocity.z).magnitude;
                float horizSpeed = Mathf.Max(0f, rawHoriz - fovHorizontalMinSpeed) * fovHorizontalMultiplier;

                float rawVert = includeVerticalVelocity ? Mathf.Abs(velocity.y) : 0f;
                float vertSpeed = Mathf.Max(0f, rawVert - fovVerticalMinSpeed) * fovVerticalMultiplier;
                
                float combinedSpeed = Mathf.Sqrt(horizSpeed * horizSpeed + vertSpeed * vertSpeed);

                float extraFov = combinedSpeed * fovSpeedMultiplier * dynamicFovIntensity;
                targetFov += extraFov;
                targetFov = Mathf.Min(targetFov, maxFov);
            }
        }

        // Smoothly transition both cameras to the target FOV
        float smoothedFov = Mathf.Lerp(mainCam.fieldOfView, targetFov, Time.deltaTime * fovSmoothSpeed);
        mainCam.fieldOfView = smoothedFov;
        overlayCam.fieldOfView = smoothedFov;
    }

    // ══════════════════════════════════════════════════════════════════
    //  PUBLIC API — Enable / Disable
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Enable or disable all movement (WASD, jump, crouch). Gravity still applies.</summary>
    public void SetMovementEnabled(bool enabled)
    {
        movementEnabled = enabled;

        if (!enabled)
        {
            velocity.x = 0f;
            velocity.z = 0f;
        }
    }

    /// <summary>Enable or disable mouse look.</summary>
    public void SetLookEnabled(bool enabled)
    {
        lookEnabled = enabled;
    }

    /// <summary>Enable or disable both movement AND look at once.</summary>
    public void SetPlayerControlsEnabled(bool enabled)
    {
        SetMovementEnabled(enabled);
        SetLookEnabled(enabled);
    }

    /// <summary>Teleport the player to the given world position. Resets velocity so the player doesn't
    /// carry old momentum. Works correctly with CharacterController (disables it during the move).</summary>
    public void Teleport(Vector3 position)
    {
        velocity = Vector3.zero;
        knockVelocity = Vector3.zero;
        if (isSliding) EndSlide();
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
    }

    /// <summary>Teleport the player and also set their look direction to match the given forward vector.</summary>
    public void Teleport(Vector3 position, Vector3 forward)
    {
        Teleport(position);

        // Apply yaw to the player body
        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z).normalized;
        if (flatForward.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);

        // Apply pitch to the camera head
        xRotation = -Mathf.Asin(Mathf.Clamp(forward.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);
        if (cameraHead != null)
            cameraHead.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }

    // ══════════════════════════════════════════════════════════════════
    //  SLIDE
    // ══════════════════════════════════════════════════════════════════
    void UpdateSlide()
    {
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        if (!isSliding)
        {
            if (enableSlide && isGrounded && Input.GetKeyDown(crouchKey) && flat.magnitude >= slideMinSpeed && Time.time >= nextSlideTime)
                StartSlide(flat);
            return;
        }

        slideTime += Time.deltaTime;
        if (!isGrounded) { EndSlide(); return; }   // off a ledge: normal air control takes the momentum
        if (!Input.GetKey(crouchKey) || slideSpeed <= slideEndSpeed || slideTime >= slideMaxTime) EndSlide();
    }

    void StartSlide(Vector3 flatVelocity)
    {
        isSliding = true;
        slideDir = flatVelocity.normalized;
        slideSpeed = Mathf.Min(flatVelocity.magnitude + slideBoost, slideMaxSpeed);
        slideTime = 0f;
        isCrouching = true;
        targetHeight = crouchHeight;
        SetSlidingAnim(true);
    }

    void EndSlide()
    {
        isSliding = false;
        nextSlideTime = Time.time + slideCooldown;
        SetSlidingAnim(false);
    }

    void HandleSlideMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 wish = transform.right * h + transform.forward * v;
        if (wish.sqrMagnitude > 0.01f)
            slideDir = Vector3.RotateTowards(slideDir, wish.normalized, slideSteer * Time.deltaTime, 0f);
        slideSpeed = Mathf.MoveTowards(slideSpeed, 0f, slideFriction * Time.deltaTime);
        velocity.x = slideDir.x * slideSpeed;
        velocity.z = slideDir.z * slideSpeed;
    }

    void SetSlidingAnim(bool on)
    {
        if (hasSlidingParam) handsAnim.SetBool("Sliding", on);
    }

    /// <summary>Very slight camera rumble: grows with fall speed, and a touch while sliding.</summary>
    void UpdateMoveShake()
    {
        if (juice == null) return;
        float fall = isGrounded ? 0f : -velocity.y;
        float amount = Mathf.InverseLerp(fallShakeStartSpeed, fallShakeFullSpeed, fall) * fallShakeMax;
        if (isSliding) amount = Mathf.Max(amount, slideShake * Mathf.Clamp01(slideSpeed / Mathf.Max(slideMinSpeed, 0.01f)));
        juice.SetSustainedShake(amount);
    }

    /// <summary>This frame's knockback movement, integrated exactly so the slide is the same length at any frame rate.</summary>
    Vector3 KnockbackStep(float dt)
    {
        float speed = knockVelocity.magnitude;
        if (speed < 0.0001f) { knockVelocity = Vector3.zero; return Vector3.zero; }
        Vector3 dir = knockVelocity / speed;
        float drag = Mathf.Max(knockbackDrag, 0.0001f);
        float stopTime = speed / drag;
        if (dt >= stopTime) { knockVelocity = Vector3.zero; return dir * (0.5f * speed * stopTime); }
        knockVelocity = dir * (speed - drag * dt);
        return dir * (speed * dt - 0.5f * drag * dt * dt);
    }

    /// <summary>Shove the player: horizontal part slides and slows down by knockbackDrag, upward part is a hop.</summary>
    public void Knockback(Vector3 push)
    {
        if (PlayerExecution.IsExecuting) return;   // an execution owns the player's position
        knockVelocity = new Vector3(push.x, 0f, push.z);
        if (push.y > 0f) velocity.y = Mathf.Max(velocity.y, push.y);
    }

    /// <summary>Set the number of extra air jumps available (takes effect next time player lands).</summary>
    public void SetAirJumps(int count)
    {
        airJumps = count;
        if (isGrounded)
            airJumpsRemaining = airJumps;
    }
}
