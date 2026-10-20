namespace WhiteKnuckleTogether {
    public static class WKTDeathConsts {
        /// <summary>Real seconds the "You Died" popup shows before the dead player starts spectating.</summary>
        public const float SPECTATE_DELAY = 2f;
        /// <summary>How far behind the spectated player's head the camera orbits, with nothing in the way.</summary>
        public const float ORBIT_DISTANCE = 3f;
        /// <summary>The closest the camera comes to the spectated player's head when geometry pushes it in.</summary>
        public const float MIN_ORBIT_DISTANCE = 0.4f;
        /// <summary>The radius of the camera's sphere cast, so the near plane never clips into geometry.</summary>
        public const float CAMERA_RADIUS = 0.25f;
        /// <summary>Extra space kept between the camera's sphere and whatever it hit.</summary>
        public const float CAMERA_SKIN = 0.05f;
        /// <summary>Metres per second the camera moves back out once geometry no longer pushes it in. Pulling in is instant.</summary>
        public const float ORBIT_RETURN_SPEED = 4f;
        /// <summary>The same pitch limits as the game's own camera.</summary>
        public const float MIN_PITCH = -80f;
        public const float MAX_PITCH = 88f;
        /// <summary>Looking slightly down on the spectated player when spectating starts.</summary>
        public const float START_PITCH = 15f;
    }
}
