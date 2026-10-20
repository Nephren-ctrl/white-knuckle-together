using HarmonyLib;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Crouches the local player the way the game does when Crouch is pressed. The game stands them back up by itself once
    /// there is room above them and Crouch is not held, so a player moved somewhere low, such as a vent, is never stuck
    /// standing in it.
    /// </summary>
    internal static class WKTPlayerCrouch {
        private const string c_change_height = "ChangeHeight";

        public static void Crouch(ENT_Player player) {
            if (player.IsCrouching()) return;
            Fields.Crouching(player) = true;
            // The capsule shrinks at once, as the player may be moved somewhere low this frame; the camera follows over the
            // game's crouch transition.
            var controller = player.cCon;
            var standing = controller.height;
            controller.height = player.crouchHeight;
            controller.center = player.GetPlayerUpVector() * ((player.crouchHeight - standing) * 0.5f);
            player.StopCoroutine(c_change_height);
            player.StartCoroutine(c_change_height, player.crouchHeight);
        }

        /// <summary>Found when first used: the test runtime cannot load the game's types eagerly.</summary>
        private static class Fields {
            public static readonly AccessTools.FieldRef<ENT_Player, bool> Crouching = AccessTools.FieldRefAccess<ENT_Player, bool>("crouching");
        }
    }
}
