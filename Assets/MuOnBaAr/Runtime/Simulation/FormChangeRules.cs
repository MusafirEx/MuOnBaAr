using UnityEngine;

namespace MuOnBaAr.Runtime
{
    public enum FormChangeResult
    {
        Success,
        InvalidSetup,
        Defeated,
        InvalidTarget,
        AlreadyActive,
        OnCooldown,
        NotEnoughEnergy,
        TransitionNotAllowed
    }

    // Side-effect-free except for the state passed by ref.
    // Invoke only on the simulation state authority when networking is added.
    public static class FormChangeRules
    {
        public static FormChangeResult TryChange(
            ActorDefinition actor,
            ref FormChangeState state,
            int targetFormIndex,
            int currentTick,
            int ticksPerSecond)
        {
            if (actor == null || ticksPerSecond <= 0 || currentTick < 0)
                return FormChangeResult.InvalidSetup;

            if (!actor.TryGetForm(state.CurrentFormIndex, out var source))
                return FormChangeResult.InvalidSetup;

            if (state.Health <= 0)
                return FormChangeResult.Defeated;

            if (!actor.TryGetForm(targetFormIndex, out var target))
                return FormChangeResult.InvalidTarget;

            if (targetFormIndex == state.CurrentFormIndex)
                return FormChangeResult.AlreadyActive;

            if (currentTick < state.NextChangeTick)
                return FormChangeResult.OnCooldown;

            if (!target.AcceptsSource(source))
                return FormChangeResult.TransitionNotAllowed;

            if (state.Energy < target.ChangeEnergyCost)
                return FormChangeResult.NotEnoughEnergy;

            // Apply changes atomically after every validation has passed.
            int lockTicks = Mathf.CeilToInt(target.ChangeLockSeconds * ticksPerSecond);
            state.Energy -= target.ChangeEnergyCost;
            state.CurrentFormIndex = targetFormIndex;
            state.NextChangeTick = currentTick + Mathf.Max(0, lockTicks);
            return FormChangeResult.Success;
        }
    }
}
