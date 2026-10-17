using UnityEngine;

namespace TingLu.Levels.Editor
{
    // Separate MonoScript identity lets Unity restore this serialized undo buffer after domain reload.
    public sealed class LevelEditBuffer : ScriptableObject { public string json; }
}
