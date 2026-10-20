using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Marks an object that belongs to this machine alone in a synced co-op run, such as a player's own artifact choices: it is
    /// never shared, bound or hidden by <see cref="WKTWorldSync"/>.
    /// </summary>
    internal sealed class WKTLocalObject : MonoBehaviour {
    }
}
