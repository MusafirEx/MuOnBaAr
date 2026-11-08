using System;

namespace MuOnBaAr.Runtime
{
    // Runtime values belong to an authoritative simulation, never to a shared asset.
    [Serializable]
    public struct FormChangeState
    {
        public int CurrentFormIndex;
        public int Health;
        public int Energy;
        public int NextChangeTick;

        public static FormChangeState Create(ActorDefinition actor)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            return new FormChangeState
            {
                CurrentFormIndex = actor.StartingFormIndex,
                Health = actor.MaxHealth,
                Energy = actor.MaxEnergy,
                NextChangeTick = 0
            };
        }
    }
}
