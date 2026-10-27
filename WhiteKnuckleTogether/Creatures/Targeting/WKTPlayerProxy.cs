using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Host only, in a synced co-op run: stands in for a remote player in the host's world, so enemies see, chase and hit
    /// them. It is shaped, layered and tagged like the local player's body, so the game's own target scans find it. Hits
    /// are forwarded to that player's machine; the proxy itself never takes damage or dies. Trigger zones ignore it, since
    /// each player's own machine handles what the player walks into.
    /// </summary>
    internal sealed class WKTPlayerProxy : GameEntity {
        /// <summary>Further than any touch: where a proxy's closest point is said to be.</summary>
        private const float c_out_of_reach = 1000f;
        private static readonly Dictionary<byte, WKTPlayerProxy> s_byPlayer = new();
        private static readonly HashSet<int> s_colliders = new();
        private CapsuleCollider? m_collider;

        public byte PlayerId { get; private set; }
        /// <summary>The remote player's body this stands in for.</summary>
        public WKTRemotePlayer? Remote { get; private set; }
        /// <summary>Where the player looks from: what enemies aim at instead of the player's camera.</summary>
        public Camera Head { get; private set; } = null!;

        public static bool TryGet(byte playerId, out WKTPlayerProxy? proxy) => s_byPlayer.TryGetValue(playerId, out proxy) && proxy != null;

        /// <summary>Whether a collider belongs to a proxy. Cheap when no proxies exist, as in singleplayer.</summary>
        public static bool IsProxyCollider(Collider? collider) =>
            s_colliders.Count != 0 && collider != null && s_colliders.Contains(collider.GetInstanceID());

        /// <summary>
        /// Replaces <c>Physics.OverlapSphere(position, radius)</c> where remote players decide for themselves what they touch.
        /// The game's own array comes back unless a proxy is in it.
        /// </summary>
        public static Collider[] OverlapSphereWithoutProxies(Vector3 position, float radius) => WithoutProxies(Physics.OverlapSphere(position, radius));

        public static Collider[] WithoutProxies(Collider[] hits) {
            var proxies = 0;
            foreach (var hit in hits) {
                if (IsProxyCollider(hit))
                    proxies++;
            }
            if (proxies == 0) return hits;
            var kept = new Collider[hits.Length - proxies];
            var next = 0;
            foreach (var hit in hits) {
                if (!IsProxyCollider(hit))
                    kept[next++] = hit;
            }
            return kept;
        }

        /// <summary>
        /// Replaces <c>collider.ClosestPoint(point)</c> where remote players decide for themselves what they touch: a proxy is
        /// never close enough.
        /// </summary>
        public static Vector3 ClosestPointUnlessProxy(Collider collider, Vector3 point) =>
            IsProxyCollider(collider) ? point + Vector3.up * c_out_of_reach : collider.ClosestPoint(point);

        /// <summary>Gives a remote player's body a proxy shaped like <paramref name="localPlayer"/>, until it is given its own shape (see <see cref="SetShape"/>).</summary>
        public static WKTPlayerProxy Create(Transform remoteRoot, byte playerId, ENT_Player localPlayer) {
            var root = new GameObject($"WKT Proxy {playerId}");
            root.SetActive(false);
            root.transform.SetParent(remoteRoot, false);
            root.layer = localPlayer.gameObject.layer;
            var collider = root.AddComponent<CapsuleCollider>();
            collider.isTrigger = true;
            var controller = localPlayer.cCon;
            if (controller != null) {
                collider.radius = controller.radius;
                collider.height = controller.height;
                collider.center = controller.center;
            }
            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            if (localPlayer.cam != null)
                head.transform.localPosition = localPlayer.transform.InverseTransformPoint(localPlayer.cam.transform.position);
            var camera = head.AddComponent<Camera>();
            camera.enabled = false;
            var proxy = root.AddComponent<WKTPlayerProxy>();
            proxy.PlayerId = playerId;
            proxy.Remote = remoteRoot.GetComponent<WKTRemotePlayer>();
            proxy.Head = camera;
            proxy.m_collider = collider;
            proxy.health = proxy.maxHealth = float.MaxValue;
            proxy.entityPrefabID = string.Empty;
            proxy.canSave = false;
            var tagger = root.GetComponent<ObjectTagger>();
            var localTagger = localPlayer.GetComponent<ObjectTagger>();
            if (tagger != null && localTagger != null)
                tagger.tags = new List<string>(localTagger.tags);
            root.SetActive(true);
            return proxy;
        }

        /// <summary>
        /// A strider's footsteps shake every player's camera near it (<c>DEN_Strider.ShakeScreen</c> broadcasts this message). The
        /// player's own machine shakes from the strider it shows, so the proxy only takes the message.
        /// </summary>
        [UsedImplicitly]
        private void ShakeCamera(float amount) { }

        /// <summary>
        /// Shapes the proxy like the remote player's own controller, sized by their size buffs and crouch: its capsule, in the
        /// body's space, and the height of the eyes enemies aim at.
        /// </summary>
        public void SetShape(float radius, float height, float center, float eyeHeight) {
            if (m_collider != null) {
                m_collider.radius = radius;
                m_collider.height = height;
                m_collider.center = new Vector3(0f, center, 0f);
            }
            var head = Head.transform;
            var eyes = head.localPosition;
            head.localPosition = new Vector3(eyes.x, eyeHeight, eyes.z);
        }

        /// <summary>Mirrors whether the player is dead, so enemies stop going for them.</summary>
        public void SetDead(bool isDead) => dead = isDead;

        /// <summary>A hit lands on the player's own machine instead.</summary>
        public override bool Damage(Damageable.DamageInfo? info) {
            // Enemies whose hits the player's own machine decides already hit them there, as does their own copy of an explosion.
            if (info == null || dead || WKTSharedEffectScope.IsOpen || WKTAttackModules.IsClientDecided(info.sourceEntity)) return false;
            WKTTargets.SendEffect(PlayerId, WKTTargets.DamageEffect(info));
            return true;
        }

        /// <summary>A kill happens on the player's own machine instead.</summary>
        public override void Kill(string type, Damageable.DamageInfo? damageInfo) {
            if (dead) return;
            WKTTargets.SendEffect(PlayerId, WKTTargets.KillEffect(type, damageInfo, transform.position));
        }

        public override void OnEnable() {
            base.OnEnable();
            s_byPlayer[PlayerId] = this;
            if (m_collider != null)
                s_colliders.Add(m_collider.GetInstanceID());
        }

        public override void OnDisable() {
            base.OnDisable();
            if (s_byPlayer.TryGetValue(PlayerId, out var registered) && registered == this)
                s_byPlayer.Remove(PlayerId);
            if (m_collider != null)
                s_colliders.Remove(m_collider.GetInstanceID());
        }
    }
}
