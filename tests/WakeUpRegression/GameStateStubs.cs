// Minimal game model contracts, verified against the installed World.dll:
// Coma.trigger only subtracts duration; Character.tick clears it on a later tick.
// Keep Unity out of this runner so it can execute without launching the game.
namespace World.Characters
{
    public class CharacterState
    {
        public int duration;
        public virtual void trigger(int effect) { }
    }

    public sealed class Character
    {
        public CharacterState state;

        public void GameTick()
        {
            if (state == null) return;
            if (state.duration > 0) state.duration--;
            else state = null;
        }
    }
}

namespace World.Characters.CharacterStates
{
    public sealed class Coma : CharacterState
    {
        public override void trigger(int effect) => duration -= effect;
    }
}
