using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Rendering;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Placeholder body of another player in a multiplayer run: a capsule torso and a cube head as tall together as the
    /// player's controller (sized by their buffs, see <see cref="SetBody"/>) but thinner, that shrink toward the feet as they
    /// crouch and redden as they lose health, the head leaning a little with the look (<see cref="WKTAvatarShape"/>), a visor that shows where they look, two hands that hold on where they grab and redden as their grip tires (and are held out
    /// along their aim while holding an item or reaching), the items they hold, and the sounds their body makes. Their name
    /// shows on screen while the local player looks at them (<see cref="WKTMultiplayerHud"/>). It has no colliders and alerts
    /// no listeners, so it can never affect physics, spawning, enemies or the local player.
    /// </summary>
    /// <remarks>Its LateUpdate follows <see cref="WKTPlayerSync"/>, which places it.</remarks>
    [DefaultExecutionOrder(WKTPlayerSync.EXECUTION_ORDER + 1)]
    internal sealed class WKTRemotePlayer : MonoBehaviour {
        /// <summary>The body is drawn thinner than the controller it matches, which looks bulky.</summary>
        public const float BODY_WIDTH_SCALE = 0.75f;
        /// <summary>
        /// The game places its camera this far above the controller's pivot when standing, before scaling, and lowers it by
        /// as much as the controller shrinks.
        /// </summary>
        private const float c_head_height = 0.45f;
        /// <summary>The controller's radius before scaling and the width buff, as <c>ENT_Player.FixedUpdate</c> sets it.</summary>
        private const float c_controller_radius = 0.3f;
        private const float c_visor_width = 0.35f;
        private const float c_visor_height = 0.12f;
        private const float c_visor_depth = 0.15f;
        private const float c_hand_radius = 0.15f;
        /// <summary>Space between a resting hand and the side of the body.</summary>
        private const float c_hand_rest_gap = 0.05f;
        /// <summary>How far below the body's center a resting hand hangs, as a fraction of the body's height.</summary>
        private const float c_hand_rest_drop = 0.12f;
        /// <summary>Seconds a hand takes to reach a grab point from its side, and back.</summary>
        private const float c_hand_reach_duration = 0.12f;
        /// <summary>How fast a remote flashlight turns after the head, as the game turns a stowed one after the camera.</summary>
        private const float c_lamp_follow_rate = 8f;
        private const int c_gib_count = 14;
        private const float c_gib_size = 0.14f;
        private const float c_gib_speed = 6f;
        private const float c_gib_lifetime = 10f;
        private const int c_gib_blood = 120;
        private const string c_gib_particle = "blood-regular-small";
        /// <summary>Gibs collide with the world only, never with players, enemies or items (the game's dead-creature layer).</summary>
        private const string c_gib_layer = "Interactable Only World Collide";
        /// <summary>Where a hand held out (holding an item, or reaching to grab) sits from the eyes, before scaling: out to the side, down and forward.</summary>
        private static readonly Vector3 s_aimOffset = new(0.3f, -0.25f, 0.55f);
        /// <summary>
        /// Where an open hand (Grab Assists) is held out: further forward and closer to the line of sight than an item, so it
        /// is a meaningful reach away and shows where the player aims.
        /// </summary>
        private static readonly Vector3 s_openOffset = new(0.2f, -0.1f, 1.1f);
        /// <summary>An open hand, offered for another player to take hold of.</summary>
        private static readonly Color s_openColor = new(0.3f, 1f, 0.35f);
        /// <summary>
        /// The mod's unlit shader with the game's vertex snapping, warp and fog (WKT_RetroUnlit in the asset bundle). Unlit, so
        /// the colors read in the game's dark levels: its world shaders are lit by its own globals, which the default material
        /// ignores and renders black.
        /// </summary>
        private const string c_retro_shader = "WKT_RetroUnlit";
        /// <summary>Plain unlit shaders, tried in order if the bundle is missing.</summary>
        private static readonly string[] s_shaderNames = { "Unlit/Color", "Sprites/Default" };
        private static readonly int s_srcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int s_dstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int s_zWriteId = Shader.PropertyToID("_ZWrite");
        /// <summary>Body, head and hands at full health: a mid gray, so the avatar reads against both bright and dark levels.</summary>
        private static readonly Color s_bodyColor = new(0.5f, 0.5f, 0.5f);
        private static readonly Color s_visorColor = new(0.15f, 0.15f, 0.15f);
        /// <summary>What the body and hands blend toward as health and grip run out, from <see cref="s_bodyColor"/>.</summary>
        private static readonly Color s_spentColor = Color.red;
        private static Shader? s_shader;
        /// <summary>Whether <see cref="s_shader"/> is the retro shader rather than a fallback.</summary>
        private static bool s_isRetroShader;
        /// <summary>How opaque another player is in instanced co-op, where they are a ghost from another copy of the world.</summary>
        private const float c_ghost_alpha = 0.15f;
        /// <summary>How fast a talking dead player's ghost head follows their voice's loudness, in alpha per second.</summary>
        private const float c_ghost_fade_rate = 0.5f;
        /// <summary>Unlit and blended, for ghosts when the retro shader is missing.</summary>
        private const string c_ghost_shader = "Sprites/Default";
        private static bool s_shaderResolved;
        private readonly RemoteHand m_leftHand = new();
        private readonly RemoteHand m_rightHand = new();
        private Transform m_body = null!;
        private Transform m_head = null!;
        private Transform m_headModel = null!;
        private Transform m_visor = null!;
        private WKTRemotePlayerAudio m_audio = null!;
        private WKTPlayerProxy? m_proxy;
        private Material? m_bodyMaterial;
        private Material? m_headMaterial;
        private Material? m_visorMaterial;
        private Vector3 m_handRestOffset;
        private float m_scale = 1f;
        private float m_bodyCenter;
        private float m_bodyHalfHeight;
        private float m_createdTime;
        private float m_alpha = 1f;
        private Transform? m_ghostHead;
        private Material? m_ghostMaterial;
        private float m_ghostAlpha;
        private CL_Lamp? m_lamp;
        private string m_lampPrefab = string.Empty;
        private WKTPlayerLightState m_light;
        /// <summary>The body last shaped, by its size buffs (see <see cref="SetBody"/>).</summary>
        private WKTPlayerBodyState m_shape;
        /// <summary>The game's scale of a player and its controller's heights, before any buff (local to the controller).</summary>
        private float m_baseScale;
        private float m_baseStandingHeight;
        private float m_baseCrouchHeight;
        /// <summary>How big the body is next to a normal one: one plus its scale buff. Head, visor and hands are sized by it.</summary>
        private float m_size = 1f;
        /// <summary>World units, like every dimension below.</summary>
        private float m_standingHeight;
        private float m_crouchHeight;
        private float m_radius;
        private float m_headHeight;
        /// <summary>The head's offset from the eyes for the current look, up and forward (see <see cref="WKTAvatarShape.GetHeadOffset"/>).</summary>
        private Vector2 m_headOffset;
        /// <summary>Where the eyes are above the pivot at the current crouch.</summary>
        private float m_eyeHeight;
        /// <summary>Starts out of range, so the first pose always shapes the body.</summary>
        private int m_crouch = -1;
        /// <summary>Starts out of range, so the first pose always colors the body.</summary>
        private int m_health = -1;
        private bool m_leftHanging;
        private bool m_rightHanging;
        private bool m_isVisible = true;
        private bool m_hasApplied;

        public byte PlayerId { get; private set; }
        public string DisplayName { get; private set; } = string.Empty;
        /// <summary>Where the player's eyes are, turned the way they look.</summary>
        public Transform Head => m_head;
        /// <summary>The middle of the body, standing or crouched.</summary>
        public Vector3 BodyCenter => transform.position + transform.up * m_bodyCenter;
        /// <summary>Whether the body is shown: the player is alive.</summary>
        public bool IsShown => m_isVisible;
        /// <summary>
        /// Whether the player's level is loaded here (see <see cref="WKTPlayerRooms"/>). Away, their body is hidden and its
        /// sounds and proximity voice are silent.
        /// </summary>
        public bool IsPresent { get; set; } = true;
        /// <summary>Seconds since this body was first shown.</summary>
        public float ShownSince => Time.time - m_createdTime;

        [UsedImplicitly]
        private void LateUpdate() {
            if (m_lamp == null) return;
            var lampTransform = m_lamp.transform;
            // As the game moves a stowed flashlight's lamp with the camera: from the eyes, where the player's own camera is, not
            // from the head as it nods on its neck.
            lampTransform.position = transform.TransformPoint(new Vector3(0f, m_eyeHeight, 0f));
            lampTransform.rotation = Quaternion.Lerp(lampTransform.rotation, m_head.rotation, Time.deltaTime * c_lamp_follow_rate);
            m_lamp.intensity = m_isVisible ? m_light.Intensity : 0f;
            m_lamp.animAmplitude = m_light.FlickerAmplitude;
            m_lamp.animSpeed = m_light.FlickerSpeed;
            m_lamp.MoveToFrontOfQueue();
        }

        [UsedImplicitly]
        private void OnDestroy() {
            if (m_lamp != null)
                Destroy(m_lamp.gameObject);
            DestroyMaterial(m_bodyMaterial);
            DestroyMaterial(m_headMaterial);
            DestroyMaterial(m_visorMaterial);
            DestroyMaterial(m_ghostMaterial);
            DestroyMaterial(m_leftHand.material);
            DestroyMaterial(m_rightHand.material);
        }

        /// <summary>
        /// A copy of an item's world model that only renders: it is built inactive and stripped of everything but its meshes
        /// and sprites (its outline hidden), so none of the item's own code ever runs and nothing collides with it. It is turned and placed in the
        /// hand by its shape (see <see cref="WKTHeldItemPose"/>). Null if the item has no model.
        /// </summary>
        private static GameObject? CreateItemVisual(string prefabName, Transform parent, List<WKTOutlinePart> outlines) {
            Item_Object? prefab;
            try {
                prefab = CL_AssetManager.GetItemObjectPrefab(prefabName);
            }
            catch (System.Exception) {
                prefab = null;
            }
            if (prefab == null) return null;
            var holder = new GameObject("Held " + prefabName);
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var copy = Instantiate(prefab.gameObject, holder.transform, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            WKTHeldItemPose.Apply(prefabName, prefab, copy.transform);
            StripToVisuals(copy, outlines);
            holder.SetActive(true);
            return holder;
        }

        private static void StripToVisuals(GameObject root, List<WKTOutlinePart> outlines) {
            // Behaviours first, then joints and bodies, then colliders: later ones may be required by earlier ones.
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                DestroyImmediate(behaviour);
            foreach (var joint in root.GetComponentsInChildren<Joint>(true))
                DestroyImmediate(joint);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                DestroyImmediate(body);
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(collider);
            foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                DestroyImmediate(source);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                DestroyImmediate(light);
            // The outline marks an item lying in the world, and one in a hand only while it can be stolen (see WKTItemStealing).
            WKTItemOutline.HideOutlines(root, outlines);
            // Stopped and hidden rather than switched off with their object: some models carry them on their root.
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true)) {
                var emission = particles.emission;
                emission.enabled = false;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (particles.TryGetComponent<ParticleSystemRenderer>(out var particleRenderer))
                    particleRenderer.enabled = false;
            }
        }

        /// <param name="localPlayer">
        /// Sized like it without its buffs, and sounds like it. Its controller's dimensions are local to it, which the game
        /// scales up.
        /// </param>
        /// <param name="voices">The player's <see cref="WKTLobbyConsts.KEY_VOICES"/>.</param>
        public static WKTRemotePlayer Create(WKTPlayer player, ENT_Player localPlayer, string? voices) {
            var root = new GameObject(string.Format("{0} ({1})", nameof(WKTRemotePlayer), player.Id));
            var remote = root.AddComponent<WKTRemotePlayer>();
            remote.PlayerId = player.Id;
            // Never the local player's own size or crouch: a locally crouching or shrunk player never makes others short.
            remote.m_baseScale = localPlayer.playerScale;
            remote.m_baseStandingHeight = localPlayer.standingHeight;
            remote.m_baseCrouchHeight = localPlayer.crouchHeight;
            remote.m_createdTime = Time.time;
            remote.m_body = CreatePrimitive(PrimitiveType.Capsule, root.transform, s_bodyColor, out remote.m_bodyMaterial);
            remote.m_head = new GameObject("Head").transform;
            remote.m_head.SetParent(root.transform, false);
            remote.m_headModel = CreatePrimitive(PrimitiveType.Cube, remote.m_head, s_bodyColor, out remote.m_headMaterial);
            remote.m_visor = CreatePrimitive(PrimitiveType.Cube, remote.m_head, s_visorColor, out remote.m_visorMaterial);
            remote.m_leftHand.Initialize(root.transform, "Left Hand");
            remote.m_rightHand.Initialize(root.transform, "Right Hand");
            remote.DisplayName = player.friend.Name ?? string.Empty;
            remote.m_crouch = 0;
            remote.m_shape = new WKTPlayerBodyState(player.Id, 0f, 0f, null);
            // Shapes the body standing.
            remote.ApplyShape();
            remote.m_audio = new WKTRemotePlayerAudio(root, localPlayer);
            remote.m_audio.SetVoices(voices);
            return remote;
        }

        /// <summary>
        /// Sizes the body by the player's buffs, as the game sizes their controller (Roach Mode halves it), and gives it the
        /// sounds of their perks. Cheap when nothing changed.
        /// </summary>
        public void SetBody(in WKTPlayerBodyState body) {
            if (!body.HasVoicePerks(m_shape.VoicePerks))
                m_audio.SetPerkVoices(body.VoicePerks);
            var reshape = !body.HasSameShape(m_shape);
            m_shape = body;
            if (reshape)
                ApplyShape();
        }

        /// <summary>The game's sizes: <c>playerScale * (1 + addPlayerScale)</c>, and a radius of <c>0.3 + addPlayerWidth</c> before scaling.</summary>
        private void ApplyShape() {
            m_size = 1f + m_shape.ScaleBuff;
            m_scale = m_baseScale * m_size;
            m_standingHeight = m_baseStandingHeight * m_scale;
            m_crouchHeight = m_baseCrouchHeight * m_scale;
            m_radius = (c_controller_radius + m_shape.WidthBuff) * m_scale * BODY_WIDTH_SCALE;
            m_headHeight = c_head_height * m_scale;
            var headSize = WKTAvatarShape.GetHeadSize(m_radius);
            m_headModel.localScale = Vector3.one * headSize;
            // Half outside the head, so the look direction reads at a glance.
            m_visor.localPosition = new Vector3(0f, 0f, headSize * 0.5f);
            m_visor.localScale = new Vector3(c_visor_width, c_visor_height, c_visor_depth) * m_size;
            if (m_ghostHead != null)
                m_ghostHead.localScale = Vector3.one * headSize;
            m_handRestOffset.x = m_radius + (c_hand_radius + c_hand_rest_gap) * m_size;
            m_leftHand.SetSize(m_size);
            m_rightHand.SetSize(m_size);
            SetCrouch((byte)m_crouch);
        }

        /// <param name="grabAssists">Whether open hands are shown as offered (see <see cref="WKTSession.GrabAssists"/>).</param>
        public void Apply(in WKTPlayerPose pose, float worldOffset, bool grabAssists) {
            var position = pose.Position;
            position.y += worldOffset;
            // A jump this far is a teleport: everything about the body moves with it at once, nothing trails across.
            var teleported = m_hasApplied && (position - transform.position).sqrMagnitude > WKTSnapshotBuffer.TELEPORT_DISTANCE * WKTSnapshotBuffer.TELEPORT_DISTANCE;
            m_hasApplied = true;
            IsGrounded = pose.Grounded;
            m_leftHanging = pose.Left.IsHanging;
            m_rightHanging = pose.Right.IsHanging;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, pose.Yaw, 0f));
            m_head.localRotation = Quaternion.Euler(pose.Pitch, 0f, 0f);
            m_headOffset = WKTAvatarShape.GetHeadOffset(pose.Pitch, m_radius);
            if (pose.Crouch != m_crouch)
                SetCrouch(pose.Crouch);
            else
                PlaceHead();
            if (pose.Health != m_health) {
                m_health = pose.Health;
                var color = WithAlpha(Color.Lerp(s_spentColor, s_bodyColor, WKTAvatarShape.GetSpentBlend(pose.HealthPercent)), m_alpha);
                m_bodyMaterial!.color = color;
                m_headMaterial!.color = color;
            }
            var reach = Time.deltaTime / c_hand_reach_duration;
            var restOffset = m_handRestOffset;
            var aimOffset = s_aimOffset * m_scale;
            var openOffset = s_openOffset * m_scale;
            var aimRotation = m_head.rotation;
            m_rightHand.Apply(pose.Right, transform.TransformPoint(restOffset), m_head.TransformPoint(aimOffset), m_head.TransformPoint(openOffset),
                aimRotation, worldOffset, reach, teleported, grabAssists);
            restOffset.x = -restOffset.x;
            aimOffset.x = -aimOffset.x;
            openOffset.x = -openOffset.x;
            m_leftHand.Apply(pose.Left, transform.TransformPoint(restOffset), m_head.TransformPoint(aimOffset), m_head.TransformPoint(openOffset),
                aimRotation, worldOffset, reach, teleported, grabAssists);
            // After moving, so the sounds start where the body now is. Nothing is heard of a player whose level is not loaded.
            if (IsPresent)
                m_audio.Apply(pose, Time.deltaTime);
        }

        /// <summary>
        /// The player died: their body bursts into chunks and blood, as the local player's death splatters their screen. The
        /// chunks fall and bounce on the world for a while, then go.
        /// </summary>
        public void Explode() {
            var origin = BodyCenter;
            try {
                FXManager.PlayParticle(c_gib_particle, origin, c_gib_blood);
            }
            catch (System.Exception) {
                // Particles are only decoration.
            }
            var layer = LayerMask.NameToLayer(c_gib_layer);
            for (var i = 0; i < c_gib_count; i++) {
                var gib = CreatePrimitive(PrimitiveType.Cube, null, Color.Lerp(s_spentColor, s_bodyColor, Random.value * 0.4f), out var material, true);
                var offset = Random.insideUnitSphere * m_radius;
                offset.y = (Random.value - 0.5f) * m_bodyHalfHeight * 2f;
                gib.position = origin + offset;
                gib.rotation = Random.rotation;
                gib.localScale = Vector3.one * (c_gib_size * m_scale * Random.Range(0.6f, 1.4f));
                if (layer >= 0)
                    gib.gameObject.layer = layer;
                var body = gib.gameObject.AddComponent<Rigidbody>();
                body.linearVelocity = (offset.normalized + Vector3.up * 0.8f) * (c_gib_speed * Random.Range(0.5f, 1f));
                body.angularVelocity = Random.insideUnitSphere * 10f;
                Destroy(gib.gameObject, c_gib_lifetime + Random.value);
                Destroy(material, c_gib_lifetime + 1.5f);
            }
        }

        /// <summary>Hides the body, hands and name, such as while the player is dead. The body keeps following their pose.</summary>
        public void SetVisible(bool visible) {
            if (visible == m_isVisible) return;
            m_isVisible = visible;
            m_body.gameObject.SetActive(visible);
            m_head.gameObject.SetActive(visible);
            m_leftHand.SetVisible(visible);
            m_rightHand.SetVisible(visible);
        }

        /// <param name="voices">The player's <see cref="WKTLobbyConsts.KEY_VOICES"/>.</param>
        public void SetVoices(string? voices) => m_audio.SetVoices(voices);

        /// <summary>
        /// Shows the player as a ghost, mostly see-through, as in instanced co-op where they play in their own copy of the world.
        /// </summary>
        public void MakeGhost() {
            if (!CanBeSeeThrough()) return;
            m_alpha = c_ghost_alpha;
            m_leftHand.alpha = c_ghost_alpha;
            m_rightHand.alpha = c_ghost_alpha;
            foreach (var material in new[] { m_bodyMaterial, m_headMaterial, m_visorMaterial, m_leftHand.material, m_rightHand.material }) {
                if (material == null) continue;
                MakeSeeThrough(material);
                material.color = WithAlpha(material.color, c_ghost_alpha);
            }
        }

        /// <summary>
        /// While this player is dead and talks to the living, a faint head of theirs shows at <paramref name="position"/>, where
        /// their spectator camera is, fading toward <paramref name="alpha"/> (0 hides it). Call every frame.
        /// </summary>
        public void SetGhostHead(Vector3 position, float alpha) {
            if (m_ghostHead == null) {
                if (alpha <= 0f || !CanBeSeeThrough()) return;
                m_ghostHead = CreatePrimitive(PrimitiveType.Cube, transform, WithAlpha(WKTPlayerColors.Get(PlayerId), 0f), out m_ghostMaterial);
                m_ghostHead.name = "Ghost Head";
                m_ghostHead.localScale = Vector3.one * WKTAvatarShape.GetHeadSize(m_radius);
                MakeSeeThrough(m_ghostMaterial);
            }
            m_ghostAlpha = Mathf.MoveTowards(m_ghostAlpha, alpha, Time.unscaledDeltaTime * c_ghost_fade_rate);
            var shown = m_ghostAlpha > 0f;
            if (m_ghostHead.gameObject.activeSelf != shown)
                m_ghostHead.gameObject.SetActive(shown);
            if (!shown) return;
            m_ghostMaterial!.color = WithAlpha(m_ghostMaterial.color, m_ghostAlpha);
            var camera = Camera.main;
            var facing = camera != null ? camera.transform.position - position : Vector3.forward;
            facing.y = 0f;
            m_ghostHead.SetPositionAndRotation(position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : Quaternion.identity);
        }

        /// <summary>Whether this machine has a shader that can draw the body see-through.</summary>
        private static bool CanBeSeeThrough() => (GetShader() != null && s_isRetroShader) || Shader.Find(c_ghost_shader) != null;

        private static void MakeSeeThrough(Material material) {
            if (GetShader() != null && s_isRetroShader) {
                material.SetFloat(s_srcBlendId, (float)BlendMode.SrcAlpha);
                material.SetFloat(s_dstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat(s_zWriteId, 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
                material.shader = Shader.Find(c_ghost_shader);
        }

        private static Color WithAlpha(Color color, float alpha) {
            color.a = alpha;
            return color;
        }

        /// <summary>
        /// Lights the player's flashlight on this machine: a copy of that flashlight's own lamp, shining from their head, so the
        /// same places are lit for everyone.
        /// </summary>
        public void SetLight(in WKTPlayerLightState state) {
            m_light = state;
            if (state.Prefab == m_lampPrefab) return;
            m_lampPrefab = state.Prefab;
            if (m_lamp != null)
                Destroy(m_lamp.gameObject);
            m_lamp = state.IsOn ? CreateLamp(state.Prefab, m_head) : null;
        }

        private static CL_Lamp? CreateLamp(string prefabName, Transform head) {
            Item_Object_Flashlight? flashlight;
            try {
                flashlight = CL_AssetManager.GetItemObjectPrefab(prefabName) as Item_Object_Flashlight;
            }
            catch (System.Exception) {
                flashlight = null;
            }
            if (flashlight == null || flashlight.lamp == null) return null;
            var holder = new GameObject("Remote Flashlight");
            holder.SetActive(false);
            var copy = Instantiate(flashlight.lamp.gameObject, holder.transform, false);
            copy.transform.SetParent(null, false);
            copy.transform.SetPositionAndRotation(head.position, head.rotation);
            Destroy(holder);
            copy.SetActive(true);
            return copy.GetComponent<CL_Lamp>();
        }

        /// <summary>Where a hand is shown: 0 left, 1 right.</summary>
        public Vector3 GetHandPosition(int hand) => hand == 0 ? m_leftHand.Position : m_rightHand.Position;

        /// <summary>Shows or hides the outline of the item in a hand (0 left, 1 right), which shows while it can be stolen.</summary>
        public void SetHeldOutline(int hand, bool shown) => (hand == 0 ? m_leftHand : m_rightHand).SetOutlined(shown);

        /// <summary>
        /// Where this body holds a hand (0 left, 1 right) out open, whether or not it does now: what another player's hand
        /// holds on to when the two are linked, so it moves with this body.
        /// </summary>
        public Vector3 GetOpenHandPoint(int hand) {
            var offset = s_openOffset * m_scale;
            if (hand == 0)
                offset.x = -offset.x;
            return m_head.TransformPoint(offset);
        }

        /// <summary>
        /// The body as drawn, an upright cylinder, for players to bump into (<see cref="WKTPlayerCollision"/>). False while it
        /// is hidden (dead), a ghost, or in a level not loaded here.
        /// </summary>
        public bool TryGetBody(out Vector3 center, out float bottom, out float top, out float radius) {
            center = transform.position;
            radius = m_radius;
            bottom = center.y + m_bodyCenter - m_bodyHalfHeight;
            top = bottom + m_bodyHalfHeight * 2f;
            return m_isVisible && IsPresent && m_alpha >= 1f;
        }

        /// <summary>Whether the player stood on the ground in their last pose.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>Whether a hand (0 left, 1 right) hung from a hold in the last pose: geometry, or another player's hand.</summary>
        public bool IsHandHanging(int hand) => hand == 0 ? m_leftHanging : m_rightHanging;

        /// <summary>Whether a hand (0 left, 1 right) is held out open for Grab Assists right now.</summary>
        public bool IsHandOpen(int hand) => hand == 0 ? m_leftHand.IsOpen : m_rightHand.IsOpen;

        /// <summary>Shows the items the player holds, by prefab name, empty for none.</summary>
        public void SetHeldItems(in WKTHeldItemsState state) {
            m_leftHand.SetItem(state.Left, transform);
            m_rightHand.SetItem(state.Right, transform);
        }

        /// <summary>Host only: gives this body a <see cref="WKTPlayerProxy"/> for enemies to go for, once.</summary>
        public WKTPlayerProxy EnsureProxy(ENT_Player localPlayer) {
            if (m_proxy != null) return m_proxy;
            m_proxy = WKTPlayerProxy.Create(transform, PlayerId, localPlayer);
            ShapeProxy();
            return m_proxy;
        }

        /// <summary>Removes the proxy, once this machine no longer simulates the world for everyone.</summary>
        public void DestroyProxy() {
            if (m_proxy == null) return;
            Destroy(m_proxy.gameObject);
            m_proxy = null;
        }

        /// <summary>
        /// Shapes the body like the game's controller: it shrinks toward its feet, which stay where they are when standing,
        /// and the eyes, with the head on them, drop by as much as it shrinks. The torso fills the rest down to the feet.
        /// </summary>
        private void SetCrouch(byte crouch) {
            m_crouch = crouch;
            var height = Mathf.Lerp(m_standingHeight, m_crouchHeight, WKTFraction.ToFloat(crouch));
            var shrink = height - m_standingHeight;
            var center = shrink * 0.5f;
            var feet = center - height * 0.5f;
            m_eyeHeight = m_headHeight + shrink;
            WKTAvatarShape.GetTorso(feet, m_eyeHeight, m_radius, out var torsoCenter, out var torsoHeight);
            // Unity's capsule is 2 tall and 1 wide.
            m_body.localPosition = new Vector3(0f, torsoCenter, 0f);
            m_body.localScale = new Vector3(m_radius * 2f, torsoHeight * 0.5f, m_radius * 2f);
            m_bodyCenter = center;
            m_bodyHalfHeight = height * 0.5f;
            m_handRestOffset.y = center - height * c_hand_rest_drop;
            PlaceHead();
            ShapeProxy();
        }

        /// <summary>Gives the proxy enemies go for this body's size and crouch, with the game's controller radius rather than the drawn one.</summary>
        private void ShapeProxy() {
            if (m_proxy != null)
                m_proxy.SetShape(m_radius / BODY_WIDTH_SCALE, m_bodyHalfHeight * 2f, m_bodyCenter, m_eyeHeight);
        }

        /// <summary>Puts the head on the eyes for the current crouch, nodding on its neck with the look.</summary>
        private void PlaceHead() => m_head.localPosition = new Vector3(0f, m_eyeHeight + m_headOffset.x, m_headOffset.y);

        /// <param name="material">Created for the primitive alone; the caller destroys it.</param>
        /// <param name="keepCollider">Only for gibs, which bounce on the world on their own layer.</param>
        private static Transform CreatePrimitive(PrimitiveType type, Transform? parent, Color color, out Material material, bool keepCollider = false) {
            var primitive = GameObject.CreatePrimitive(type);
            // Immediate, so the collider never takes part in a physics step or query.
            if (!keepCollider)
                DestroyImmediate(primitive.GetComponent<Collider>());
            var renderer = primitive.GetComponent<Renderer>();
            var shader = GetShader();
            material = shader != null ? new Material(shader) : new Material(renderer.sharedMaterial);
            material.color = color;
            renderer.sharedMaterial = material;
            var primitiveTransform = primitive.transform;
            primitiveTransform.SetParent(parent, false);
            return primitiveTransform;
        }

        private static Shader? GetShader() {
            if (s_shaderResolved) return s_shader;
            s_shaderResolved = true;
            if (WKTAssets.TryLoadShader(c_retro_shader, out var retro)) {
                s_shader = retro;
                s_isRetroShader = true;
                return s_shader;
            }
            foreach (var name in s_shaderNames) {
                s_shader = Shader.Find(name);
                if (s_shader != null) return s_shader;
            }
            WKT.Instance.Logger.LogWarning("Found no unlit shader for remote players. They may render dark.");
            return null;
        }

        private static void DestroyMaterial(Material? material) {
            if (material != null)
                Destroy(material);
        }

        /// <summary>
        /// Whether <paramref name="ray"/> passes through the body (its capsule, a little widened so a thin body is easy to
        /// aim at), and the point on the body's axis nearest it, <paramref name="along"/> units down the ray.
        /// </summary>
        public bool TryHit(in Ray ray, out Vector3 point, out float along) {
            var up = transform.up;
            var center = BodyCenter;
            var bottom = center - up * Mathf.Max(0f, m_bodyHalfHeight - m_radius);
            var top = center + up * Mathf.Max(0f, m_bodyHalfHeight - m_radius);
            WKTRayMath.ClosestPoints(ray.origin, ray.direction, bottom, top, out along, out point, out var onRay);
            return along > 0f && (onRay - point).sqrMagnitude <= (m_radius * 1.5f) * (m_radius * 1.5f);
        }

        /// <summary>
        /// One hand. It blends between its resting place beside the body and the last grab point, both recomputed every
        /// frame, so it follows the body exactly while resting and stays exactly on the grab point while holding.
        /// </summary>
        private sealed class RemoteHand {
            public Material? material;
            public float alpha = 1f;
            private Transform m_transform = null!;
            /// <summary>0 at rest, 1 on the grab point.</summary>
            private float m_reach;
            /// <summary>In world-offset-corrected space, like poses, so a world shift during the release blend is followed.</summary>
            private Vector3 m_grabPoint;
            /// <summary>Starts out of range, so the first pose always colors the hand.</summary>
            private int m_stamina = -1;
            /// <summary>0 at rest, 1 held out in front.</summary>
            private float m_aim;
            /// <summary>0 held out like an item, 1 held out open for Grab Assists.</summary>
            private float m_open;
            private bool m_shownOpen;
            private readonly List<WKTOutlinePart> m_outlines = new();
            private GameObject? m_item;
            private string m_itemName = string.Empty;
            private bool m_outlined;

            public void Initialize(Transform parent, string name) {
                m_transform = CreatePrimitive(PrimitiveType.Sphere, parent, s_bodyColor, out material);
                m_transform.name = name;
                SetSize(1f);
            }

            /// <param name="size">Next to a normal body's hand.</param>
            public void SetSize(float size) {
                // Unity's sphere is 1 wide.
                m_transform.localScale = Vector3.one * (c_hand_radius * 2f * size);
            }

            public Vector3 Position => m_transform.position;
            public bool IsOpen { get; private set; }

            public void SetVisible(bool visible) {
                m_transform.gameObject.SetActive(visible);
                if (m_item != null)
                    m_item.SetActive(visible);
            }

            public void SetItem(string prefabName, Transform parent) {
                if (prefabName == m_itemName) return;
                m_itemName = prefabName;
                if (m_item != null)
                    Destroy(m_item);
                m_outlines.Clear();
                m_item = string.IsNullOrEmpty(prefabName) ? null : CreateItemVisual(prefabName, parent, m_outlines);
                // A new item in a hand being aimed at shows its outline at once.
                if (m_outlined) {
                    foreach (var part in m_outlines)
                        part.Show(true);
                }
                if (m_item != null)
                    m_item.SetActive(m_transform.gameObject.activeSelf);
            }

            public void SetOutlined(bool shown) {
                if (shown == m_outlined) return;
                m_outlined = shown;
                foreach (var part in m_outlines)
                    part.Show(shown);
            }

            public void Apply(in WKTHandPose pose, Vector3 restPoint, Vector3 aimPoint, Vector3 openPoint, Quaternion aimRotation, float worldOffset,
                float reach, bool snap, bool grabAssists) {
                IsOpen = grabAssists && pose.IsOpen;
                if (pose.IsGrabbing)
                    m_grabPoint = pose.Position;
                // After a teleport the hand is where it now belongs, never reaching back to where it held on before.
                if (snap)
                    reach = 1f;
                m_reach = Mathf.MoveTowards(m_reach, pose.IsGrabbing ? 1f : 0f, reach);
                m_aim = Mathf.MoveTowards(m_aim, pose.IsReaching ? 1f : 0f, reach);
                m_open = Mathf.MoveTowards(m_open, IsOpen ? 1f : 0f, reach);
                var grabPoint = m_grabPoint;
                grabPoint.y += worldOffset;
                var heldOut = Vector3.Lerp(aimPoint, openPoint, Mathf.SmoothStep(0f, 1f, m_open));
                var free = Vector3.Lerp(restPoint, heldOut, Mathf.SmoothStep(0f, 1f, m_aim));
                m_transform.position = Vector3.Lerp(free, grabPoint, Mathf.SmoothStep(0f, 1f, m_reach));
                if (m_item != null)
                    m_item.transform.SetPositionAndRotation(m_transform.position, aimRotation);
                if (pose.Stamina == m_stamina && IsOpen == m_shownOpen) return;
                m_stamina = pose.Stamina;
                m_shownOpen = IsOpen;
                // Open hands fade from green, others from white, toward red as their grip runs out.
                material!.color = WithAlpha(Color.Lerp(s_spentColor, IsOpen ? s_openColor : s_bodyColor, WKTAvatarShape.GetSpentBlend(pose.StaminaPercent)), alpha);
            }
        }
    }
}
