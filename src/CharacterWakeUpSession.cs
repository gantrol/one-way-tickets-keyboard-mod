using System.Collections.Generic;
using World.Characters;
using World.Characters.CharacterStates;

namespace OneWayTickets.KeyboardMod
{
    // A tool trigger only reduces Coma.duration. Keep each requested character pending
    // until the game's own character tick actually removes the Coma state.
    internal sealed class CharacterWakeUpSession
    {
        private readonly List<Character> _pending = new List<Character>();
        private object _context;
        private bool _lostTarget;

        public int RemainingCount => _pending.Count;

        public void Begin(IReadOnlyList<Character> characters, object context, int targetMask = -1)
        {
            Clear();
            _context = context;
            for (int i = 0; i < characters.Count; i++)
            {
                Character character = characters[i];
                if ((targetMask & (1 << i)) != 0 && character?.state is Coma)
                {
                    _pending.Add(character);
                }
            }
        }

        // Returns true only for a confirmed completion, never for a scene change or
        // a character removed from the active roster.
        public bool Refresh(IReadOnlyList<Character> characters, object context)
        {
            if (!ReferenceEquals(_context, context))
            {
                Clear();
                return false;
            }

            bool wasPending = _pending.Count > 0;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Character character = _pending[i];
                if (IndexOf(characters, character) < 0)
                {
                    _lostTarget = true;
                    _pending.RemoveAt(i);
                }
                else if (!(character.state is Coma))
                {
                    _pending.RemoveAt(i);
                }
            }

            return wasPending && _pending.Count == 0 && !_lostTarget;
        }

        public int FirstPendingIndex(IReadOnlyList<Character> characters)
        {
            return _pending.Count > 0 ? IndexOf(characters, _pending[0]) : -1;
        }

        public bool Apply(int effect, bool global)
        {
            bool applied = false;
            foreach (Character character in _pending)
            {
                if (!(character.state is Coma coma) || coma.duration <= 0)
                {
                    // A completed duration awaits the game's next tick. It must not
                    // consume a single-target tool action intended for the next person.
                    continue;
                }

                coma.trigger(effect);
                applied = true;
                if (!global)
                {
                    break;
                }
            }
            return applied;
        }

        public void Clear()
        {
            _pending.Clear();
            _context = null;
            _lostTarget = false;
        }

        private static int IndexOf(IReadOnlyList<Character> characters, Character character)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (ReferenceEquals(characters[i], character))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
