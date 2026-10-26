using OneWayTickets.KeyboardMod;
using World.Characters;
using World.Characters.CharacterStates;

var tests = new (string Name, Action Run)[]
{
    ("AED keeps partially awakened groups pending", PartialGroup),
    ("Completion waits for the actual game state transition", DeferredCompletion),
    ("Single-target tools skip expired coma states", SkipExpiredTarget),
    ("A single request completes all six characters", OneRequestFinishesGroup),
    ("Targeted requests affect only the chosen characters", SelectedCharacters),
    ("Selecting a character narrows a simultaneous general request", NarrowRequest),
    ("Healthy, absent and non-coma characters are ignored", IgnoreOtherStates),
    ("A new mission cancels pending work without reporting success", MissionChange),
    ("Roster changes never imply a successful wake-up", RosterChange),
    ("Reordering the roster preserves character identity", RosterReorder),
    ("A fresh coma state is still recognized as pending", ReplacedComa),
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"{tests.Length} wake-up regression tests passed.");

static Character Asleep(int duration) => new() { state = new Coma { duration = duration } };
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void PartialGroup()
{
    var characters = new[] { Asleep(2), Asleep(8), Asleep(14) };
    var session = new CharacterWakeUpSession();
    var mission = new object();
    session.Begin(characters, mission);
    Check(session.Apply(2, true), "AED must apply to the group.");
    Check(characters.Select(c => c.state.duration).SequenceEqual(new[] { 0, 6, 12 }), "Every comatose character must receive the effect.");
    foreach (var character in characters) character.GameTick();
    Check(!session.Refresh(characters, mission), "One awake person must not complete the group.");
    Check(session.RemainingCount == 2, "Both remaining comatose characters must remain visible.");
    Check(session.Apply(2, true), "The remaining group must be retried.");
    Check(characters[1].state.duration == 3 && characters[2].state.duration == 9, "Retry must reach both remaining people.");
}

static void DeferredCompletion()
{
    var characters = new[] { Asleep(1) };
    var mission = new object();
    var session = new CharacterWakeUpSession();
    session.Begin(characters, mission);
    session.Apply(30, false);
    Check(!session.Refresh(characters, mission) && session.RemainingCount == 1, "A trigger is not proof of waking.");
    Check(!session.Apply(30, false), "Do not keep striking a duration that is already complete.");
    characters[0].GameTick();
    Check(session.Refresh(characters, mission), "Confirm completion after the game clears Coma.");
    Check(!session.Refresh(characters, mission), "Completion must be reported once.");
}

static void SkipExpiredTarget()
{
    var characters = new[] { Asleep(1), Asleep(50) };
    var session = new CharacterWakeUpSession();
    session.Begin(characters, new object());
    session.Apply(30, false);
    session.Apply(30, false); // Multiple mod logic steps may precede the next game tick.
    Check(characters[1].state.duration == 20, "An expired first target must not starve the next person.");
}

static void OneRequestFinishesGroup()
{
    foreach (var tool in new[] { (Effect: 30, Global: false, Cadence: 18), (Effect: 2, Global: false, Cadence: 18), (Effect: 2, Global: false, Cadence: 1), (Effect: 2, Global: true, Cadence: 1) })
    {
        var characters = new[] { Asleep(120), Asleep(240), Asleep(360), Asleep(480), Asleep(600), Asleep(900) };
        var mission = new object();
        var session = new CharacterWakeUpSession();
        session.Begin(characters, mission); // No additional key presses or held input.
        bool completed = false;
        for (int tick = 0; tick < 1200 && !completed; tick++)
        {
            if (tick % tool.Cadence == 0) session.Apply(tool.Effect, tool.Global);
            foreach (var character in characters) character.GameTick();
            completed = session.Refresh(characters, mission);
            Check(completed == characters.All(c => c.state == null), "Completion must agree with every actual character state.");
        }
        Check(completed && session.RemainingCount == 0, "A single request must finish the entire original group.");
    }
}

static void SelectedCharacters()
{
    var characters = new[] { Asleep(50), Asleep(50), Asleep(50), Asleep(50) };
    var session = new CharacterWakeUpSession();
    session.Begin(characters, new object(), (1 << 1) | (1 << 3));
    session.Apply(2, true);
    Check(session.RemainingCount == 2, "All simultaneously selected targets must be retained.");
    Check(characters.Select(c => c.state.duration).SequenceEqual(new[] { 50, 48, 50, 48 }), "Targeting must not affect unrelated characters.");
}

static void IgnoreOtherStates()
{
    var other = new CharacterState { duration = 100 };
    var characters = new[] { new Character(), null, new Character { state = other }, Asleep(20) };
    var session = new CharacterWakeUpSession();
    session.Begin(characters, new object());
    characters[0].state = new Coma { duration = 100 };
    session.Apply(2, true);
    Check(session.RemainingCount == 1 && other.duration == 100, "Only Coma is a wake-up target.");
    Check(characters[0].state.duration == 100, "A tap must only finish its original group, without capturing a later coma.");
}

static void NarrowRequest()
{
    var characters = new[] { Asleep(50), Asleep(50), Asleep(50) };
    var mission = new object();
    var session = new CharacterWakeUpSession();
    session.Begin(characters, mission);
    session.Begin(characters, mission, 1 << 1);
    session.Apply(30, false);
    Check(characters.Select(c => c.state.duration).SequenceEqual(new[] { 50, 20, 50 }), "The general request must not strike another character before targeting is applied.");
}

static void MissionChange()
{
    var characters = new[] { Asleep(50) };
    var session = new CharacterWakeUpSession();
    session.Begin(characters, new object());
    Check(!session.Refresh(characters, new object()), "Changing missions must not report success.");
    Check(session.RemainingCount == 0 && !session.Apply(30, true), "Old mission work must be cancelled.");
}

static void RosterChange()
{
    var characters = new List<Character> { Asleep(50), Asleep(50) };
    var mission = new object();
    var session = new CharacterWakeUpSession();
    session.Begin(characters, mission);
    var removed = characters[0];
    characters.RemoveAt(0);
    Check(!session.Refresh(characters, mission), "Removing a target is not a successful wake-up.");
    session.Apply(30, true);
    Check(removed.state.duration == 50, "Removed characters must no longer receive effects.");
    characters[0].state = null;
    Check(!session.Refresh(characters, mission), "Cancellation must not turn into a full-success message later.");
}

static void RosterReorder()
{
    var characters = new List<Character> { Asleep(50), Asleep(50) };
    var mission = new object();
    var session = new CharacterWakeUpSession();
    session.Begin(characters, mission, 1 << 1);
    characters.Reverse();
    session.Refresh(characters, mission);
    Check(session.FirstPendingIndex(characters) == 0, "Feedback must follow the actual character.");
    session.Apply(30, false);
    Check(characters[0].state.duration == 20 && characters[1].state.duration == 50, "Reordering must not change the requested character.");
}

static void ReplacedComa()
{
    var characters = new[] { Asleep(2) };
    var mission = new object();
    var session = new CharacterWakeUpSession();
    session.Begin(characters, mission);
    session.Apply(2, true);
    characters[0].state = new Coma { duration = 80 };
    Check(!session.Refresh(characters, mission), "A new Coma instance is still an unconscious character.");
    session.Apply(2, true);
    Check(characters[0].state.duration == 78, "The current Coma state must receive subsequent effects.");
}
