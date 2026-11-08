using Fusion;
using UnityEngine;

namespace MuOnBaAr.Networking
{
    public enum RiderInputButton
    {
        CycleForm = 0
    }

    // One input payload per Fusion simulation tick.
    public struct RiderNetworkInput : INetworkInput
    {
        public Vector2 Move;
        public NetworkButtons Buttons;
    }
}
