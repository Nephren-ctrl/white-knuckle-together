using System;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Sorts the targets of a level's serialized events. A world target changes the shared world (doors, spawners, objects
    /// switched on or off) and must happen on the host for everyone. A local target acts on one machine's own player, camera,
    /// screen or sound, and must stay on the machine that triggered it. Pure, so it can be tested without Unity.
    /// </summary>
    internal static class WKTWorldWiring {
        /// <summary>
        /// Machines a player trades with using their own currency or items. Each player uses them for themselves: what they
        /// hand out reaches the world as that player's drop, never by the host repeating the purchase.
        /// </summary>
        public static readonly string[] PersonalInteractables = {
            nameof(ENV_VendingMachine), nameof(ENV_RationVendor), nameof(ENV_RoachTrader), nameof(ENV_Barterer), nameof(ENV_RhoAltar),
            nameof(ENV_EquipmentLocker), nameof(ENV_ArtifactDevice)
        };
        private const string c_spawn = "Spawn";
        private static readonly string[] s_localTypeNames = {
            nameof(ENT_Player), nameof(Inventory), nameof(CL_UIManager), nameof(CL_CameraControl), nameof(FXManager),
            nameof(UT_ZoneTitler), nameof(UT_AudioActivationZone), nameof(UT_CameraTakeover), nameof(UT_ItemDumper),
            nameof(UT_Teleport), nameof(UT_SeamlessTeleporter)
        };
        /// <summary>
        /// <c>OS_</c> is the in-world computers: using one moves, locks and films the local player, so it opens only for whoever
        /// pressed its keyboard.
        /// </summary>
        private static readonly string[] s_localPrefixes = { "UI_", "Hand_", "HandItem", "OS_" };
        /// <summary>Types whose methods only change what this machine sees or hears, matched by name on the type or a base type.</summary>
        private static readonly string[] s_presentationTypeNames = {
            "AudioSource", "ParticleSystem", "Light", "Animator", "Renderer", nameof(CL_Lamp), nameof(UT_AudioClipHandler), nameof(UT_LightGroupController)
        };

        /// <summary>Whether calling a method on a <paramref name="targetType"/> only affects the machine it runs on.</summary>
        public static bool IsLocalTarget(Type? targetType) {
            if (targetType == null) return false;
            for (var type = targetType; type != null && type != typeof(object); type = type.BaseType) {
                var name = type.Name;
                if (Array.IndexOf(s_localTypeNames, name) >= 0 || Array.IndexOf(PersonalInteractables, name) >= 0) return true;
                foreach (var prefix in s_localPrefixes) {
                    if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether calling a method on a <paramref name="targetType"/> changes the goo's speed, height or rising: the goo is
        /// shared, and the host's alone moves it. Its controls add up (<c>AdjustSpeed</c>, <c>RaiseOverTime</c>), so a trigger
        /// that fires again for each player who passes it would speed it up once per player. Such an event runs once.
        /// </summary>
        public static bool IsGooTarget(Type? targetType) =>
            targetType != null && (typeof(UT_GooController).IsAssignableFrom(targetType) || typeof(DEN_DeathFloor).IsAssignableFrom(targetType));

        /// <summary>
        /// Whether a listener of an entity's own event (its death, attack or state events) only presents what happened and so
        /// runs on every machine showing the entity, rather than changing the world, which only the host does. Spawners and
        /// other entities are always the world's. Targets inside the entity's own body, the local machine's player, camera or
        /// screen, and pure presentation (sound, particles, lights, animators, renderers) are local.
        /// </summary>
        public static bool IsEntityLocalTarget(Type? targetType, bool insideEntity) {
            if (targetType == null) return false;
            for (var type = targetType; type != null && type != typeof(object); type = type.BaseType) {
                if (type.Name.IndexOf(c_spawn, StringComparison.Ordinal) >= 0) return false;
            }
            if (IsLocalTarget(targetType)) return true;
            if (typeof(GameEntity).IsAssignableFrom(targetType)) return false;
            if (insideEntity) return true;
            for (var type = targetType; type != null && type != typeof(object); type = type.BaseType) {
                if (Array.IndexOf(s_presentationTypeNames, type.Name) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a door is a gate the group must pass together: open from the start, and closed by a trigger the players
        /// walk into once they are through.
        /// </summary>
        public static bool IsGateWiring(bool doorStartsOpen, string methodName, bool triggeredByPlayers) =>
            doorStartsOpen && triggeredByPlayers && (methodName == nameof(UT_Door.Close) || methodName == nameof(UT_Door.Toggle));

        /// <summary>
        /// Door animators known to open again when their bool is set back, checked in the game's assets: the silo doors'
        /// <c>close</c> bool goes from idle to Close and back. Only these can be grouped, since a door that cannot reopen would
        /// shut the group out for good.
        /// </summary>
        public static readonly string[] ReversibleDoorAnimators = { "SiloDoor" };

        /// <summary>Whether a listener sets an animator bool through the game's simple animator controller.</summary>
        public static bool IsAnimatorBoolWiring(Type targetType, string methodName) =>
            targetType.Name == nameof(UT_SimpleAnimatorController) && methodName == nameof(UT_SimpleAnimatorController.SetAnimatorBool);

        public static bool IsReversibleDoorAnimator(string? controllerName) =>
            controllerName != null && Array.IndexOf(ReversibleDoorAnimators, controllerName) >= 0;

        /// <summary>Parses the game's <c>"parameter:bool"</c> argument the way <c>SetAnimatorBool</c> does.</summary>
        public static bool TryParseAnimatorBool(string? argument, out string parameter, out bool value) {
            parameter = string.Empty;
            value = false;
            if (string.IsNullOrEmpty(argument)) return false;
            var separator = argument!.IndexOf(':');
            if (separator <= 0 || argument.IndexOf(':', separator + 1) >= 0 || !bool.TryParse(argument.Substring(separator + 1), out value)) return false;
            parameter = argument.Substring(0, separator);
            return true;
        }

        /// <summary>Whether a trigger reacting to these tags reacts to players.</summary>
        public static bool IsTriggeredByPlayers(string[]? tags) => tags != null && Array.IndexOf(tags, "Player") >= 0;
    }
}
