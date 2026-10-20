namespace WhiteKnuckleTogether {
    /// <summary>
    /// One rider's bucking timer for a flier, as <c>HandholdModule_FlierSteering</c> keeps it for its single rider: while held
    /// it counts down and bucks every buck rate, and after more than the most bucks it throws the rider off; while not held it
    /// recharges toward the buck wait and forgets its bucks. Each rider of a flier several players ride keeps their own. Pure,
    /// so it can be tested without Unity.
    /// </summary>
    internal struct WKTBuckTimer {
        public float Time { get; private set; }
        public int Hits { get; private set; }

        public WKTBuckTimer(float time, int hits) {
            Time = time;
            Hits = hits;
        }

        /// <summary>A rider who never rode it before, rested: the first buck comes after <paramref name="wait"/>.</summary>
        public static WKTBuckTimer Rested(float wait) => new(wait, 0);

        /// <summary>
        /// Advances the timer while riding. True if the flier bucks this rider now; <paramref name="throwOff"/> is then whether
        /// this buck throws them off.
        /// </summary>
        public bool Tick(float deltaTime, in WKTBuckSettings settings, out bool throwOff) {
            throwOff = false;
            Time -= deltaTime;
            if (Time > 0f) return false;
            Time = settings.Rate;
            Hits++;
            throwOff = Hits > settings.MaxHits;
            return true;
        }

        /// <summary>
        /// <paramref name="seconds"/> off the flier: the timer recharges toward the wait, as the game's does each frame it is
        /// not held, and its bucks are forgotten.
        /// </summary>
        public void Recharge(float seconds, in WKTBuckSettings settings) {
            Hits = 0;
            if (Time >= settings.Wait || seconds <= 0f) return;
            var recharged = Time + seconds * settings.RechargeSpeed;
            Time = recharged < settings.Wait ? recharged : settings.Wait;
        }
    }

    /// <summary>A flier's bucking tuning, from its <c>HandholdModule_FlierSteering</c>.</summary>
    internal readonly struct WKTBuckSettings {
        public readonly float Wait;
        public readonly float Rate;
        public readonly int MaxHits;
        public readonly float RechargeSpeed;

        public WKTBuckSettings(float wait, float rate, int maxHits, float rechargeSpeed) {
            Wait = wait;
            Rate = rate;
            MaxHits = maxHits;
            RechargeSpeed = rechargeSpeed;
        }
    }
}
