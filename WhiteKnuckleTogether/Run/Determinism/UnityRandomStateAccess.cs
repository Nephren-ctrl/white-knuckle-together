using UnityEngine;

namespace WhiteKnuckleTogether {
    internal sealed class UnityRandomStateAccess : IRandomStateAccess<Random.State> {
        public static UnityRandomStateAccess Instance { get; } = new();

        public Random.State State {
            get => Random.state;
            set => Random.state = value;
        }
    }
}
