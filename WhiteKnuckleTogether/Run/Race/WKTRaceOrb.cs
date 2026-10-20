using UnityEngine;
using UnityEngine.Rendering;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The race orb as seen: a huge sphere drawn with the mod's own shader (WKT_RaceOrb in the asset bundle), clear up close
    /// and more opaque the further each part of it is, so its distance reads at a glance, with two of the game's cloud
    /// textures churning across it. Falls back to a faint see-through sphere if the bundle is missing.
    /// </summary>
    internal sealed class WKTRaceOrb {
        private const string c_shader = "WKT_RaceOrb";
        private const string c_fallback_shader = "Sprites/Default";
        private const string c_cloud_a = "WKT_Clouds_01";
        private const string c_cloud_b = "WKT_Clouds_Strange_01";
        private static readonly Color s_color = new(0.55f, 0.85f, 1f, 1f);
        private static readonly Color s_fallbackColor = new(0.55f, 0.85f, 1f, 0.12f);
        private static readonly int s_farDistance = Shader.PropertyToID("_FarDistance");
        private static readonly int s_colorId = Shader.PropertyToID("_Color");
        private static readonly int s_radiusId = Shader.PropertyToID("_Radius");
        private static readonly int s_cloudAId = Shader.PropertyToID("_CloudA");
        private static readonly int s_cloudBId = Shader.PropertyToID("_CloudB");
        private readonly Transform m_transform;
        private readonly Material m_material;

        public WKTRaceOrb(float radius) {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "WKT Race Orb";
            Object.Destroy(sphere.GetComponent<Collider>());
            m_transform = sphere.transform;
            // Unity's sphere is 1 across.
            m_transform.localScale = Vector3.one * (radius * 2f);
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (WKTAssets.TryLoadShader(c_shader, out var shader)) {
                m_material = new Material(shader);
                m_material.SetColor(s_colorId, s_color);
                // Most opaque across the orb's far side.
                m_material.SetFloat(s_farDistance, radius * 2f);
                m_material.SetFloat(s_radiusId, radius);
                SetCloud(m_material, s_cloudAId, c_cloud_a);
                SetCloud(m_material, s_cloudBId, c_cloud_b);
            }
            else {
                m_material = new Material(Shader.Find(c_fallback_shader)) { color = s_fallbackColor };
            }
            renderer.sharedMaterial = m_material;
        }

        /// <summary>
        /// Gives the orb a cloud texture, filtered by point and tiled like the game's own pixel-crisp textures. Without it the
        /// orb is still drawn, just without clouds.
        /// </summary>
        private static void SetCloud(Material material, int property, string name) {
            if (!WKTAssets.TryLoad<Texture2D>(name, out var texture)) return;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            material.SetTexture(property, texture);
        }

        public Vector3 Position {
            get => m_transform.position;
            set => m_transform.position = value;
        }

        public void Destroy() {
            if (m_transform != null)
                Object.Destroy(m_transform.gameObject);
            Object.Destroy(m_material);
        }
    }
}
