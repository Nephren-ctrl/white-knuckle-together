using System.Collections.Generic;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// The levers of in-world computers (<c>OS_Computer_Interface.lever</c>), such as the one pulled to inject a chosen perk.
    /// A computer runs for whoever uses it, and its script waits for its own lever, then puts it back up with
    /// <c>SetState(false)</c>. Each machine's lever is therefore its own: never synced as a button or through its events.
    /// Copying one player's pull to everyone left the others' levers down, so their first pull only put it back up.
    /// </summary>
    internal static class WKTComputerLevers {
        private static readonly List<OS_Computer_Interface> s_computers = new();

        /// <summary>Adds the instance ids of the computer levers in <paramref name="level"/> to <paramref name="levers"/>.</summary>
        public static void Collect(M_Level level, HashSet<int> levers) {
            level.GetComponentsInChildren(true, s_computers);
            foreach (var computer in s_computers) {
                if (computer != null && computer.lever != null)
                    levers.Add(computer.lever.GetInstanceID());
            }
            s_computers.Clear();
        }
    }
}
