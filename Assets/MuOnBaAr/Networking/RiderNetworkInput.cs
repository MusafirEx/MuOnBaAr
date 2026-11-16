using Fusion;
using UnityEngine;

namespace MuOnBaAr.Networking
{
    public enum RiderInputButton
    {
        CycleForm = 0,
        BasicAttack = 1,
        Sprint = 2,
        Skill0 = 3,
        Skill1 = 4,
        Skill2 = 5,
        ConfirmForm = 6,
        Skill0Held = 7,
        Skill1Held = 8,
        Skill2Held = 9,
        DodgeForward = 10,
        DodgeLeft = 11,
        DodgeBack = 12,
        DodgeRight = 13,
        Jump = 14,
        JumpHeld = 15,
        FlyDescendHeld = 16
    }

    // One input payload per Fusion simulation tick.
    public struct RiderNetworkInput : INetworkInput
    {
        public Vector2 Move;
        public Vector2 Aim;
        public int RequestedFormIndex;
        public NetworkButtons Buttons;
    }
}
