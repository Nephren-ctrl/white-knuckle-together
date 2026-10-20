using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The Remains' proximity hazard from <c>DEN_Remains_Active.Update</c>, for one viewer: it builds up while the player is
    /// within its radius, faster the closer they are, and once full for <see cref="KILL_DELAY"/> seconds the Remains kills
    /// them. The blend drives the danger effect and sound. Kept as the game has it, including the kill timer only easing off
    /// while the hazard is not yet full. Pure, so it can be tested without Unity's runtime.
    /// </summary>
    internal struct WKTRemainsHazard {
        /// <summary>How long the hazard must stay full before the kill, from the game's code.</summary>
        public const float KILL_DELAY = 2f;

        public float Count;
        public float KillTime;
        public float Blend;

        /// <summary>
        /// Steps the hazard by <paramref name="deltaTime"/> with the player <paramref name="distance"/> away. <paramref name="noise"/>
        /// is the game's Perlin noise for this moment. True when the kill should start; the caller checks the player can be killed.
        /// </summary>
        public bool Step(float distance, float radius, float buildRate, float noise, float deltaTime) {
            var due = false;
            if (distance < radius) {
                Count = Mathf.Clamp01(Count + deltaTime * buildRate * (1f - distance / radius));
                if (Count >= 1f) {
                    KillTime += deltaTime;
                    due = KillTime > KILL_DELAY;
                }
                else if (KillTime > 0f)
                    KillTime -= deltaTime * 2f;
            }
            else
                Count = Mathf.Clamp01(Count - deltaTime * buildRate);
            Blend = Mathf.Lerp(Blend, noise * 0.5f * Count + 0.5f * Count, deltaTime * 10f);
            return due;
        }

        /// <summary>The proximity loop's volume for this hazard, as the game sets it.</summary>
        public readonly float LoopVolume => Count * 0.2f;
    }
}
