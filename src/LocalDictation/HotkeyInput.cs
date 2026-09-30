using System.Collections.Generic;
using System.Linq;

namespace LocalDictation
{
    // Replay contains only actual keys withheld from the current gesture.
    public sealed class HotkeyInput
    {
        public const string Binding = "ctrl-win";
        public string SelectedBinding { get; set; } = Binding;
        readonly HashSet<int> keys = new(), pendingWindows = new(), claimedWindows = new(), suppressed = new();
        HotkeyGesture gesture = new(), checkpoint;
        bool chord;
        string provisionalAction = "";
        public int HoldMilliseconds { set => gesture.HoldMs = value; }
        bool Ctrl => keys.Contains(0x11) || keys.Contains(0xA2) || keys.Contains(0xA3);
        bool Shift => keys.Contains(0x10) || keys.Contains(0xA0) || keys.Contains(0xA1);
        bool Win => keys.Contains(0x5B) || keys.Contains(0x5C);
        bool RequiredHeld => SelectedBinding == Binding ? Ctrl && Win : Ctrl && Shift && keys.Contains(0x20);
        static bool IsWin(int vk) => vk is 0x5B or 0x5C;
        bool IsMember(int vk) => vk is 0x11 or 0xA2 or 0xA3 || (SelectedBinding == Binding ? IsWin(vk) : vk is 0x10 or 0xA0 or 0xA1 or 0x20);
        public sealed record Result(bool Suppress, string Action, int[] ReplayWindows, bool ReplayCurrent);
        // KBDLLHOOKSTRUCT.time is the original DWORD message timestamp. Reconstruct
        // its TickCount64 epoch, including the 32-bit rollover, before timing gestures.
        // WASAPI startup may block this thread past HoldMs after a short physical tap.
        public static long EventTime(uint timestamp, long processedAt) => processedAt - unchecked((uint)processedAt - timestamp);
        public Result ProcessEvent(int vk, bool down, uint timestamp, long processedAt) => Process(vk, down, EventTime(timestamp, processedAt));
        public Result Process(int vk, bool down, long time)
        {
            bool fresh = down ? keys.Add(vk) : keys.Remove(vk);
            bool consume = down ? suppressed.Contains(vk) : suppressed.Remove(vk);
            var replay = new List<int>(); bool replayCurrent = false; string action = "";
            if (SelectedBinding == Binding)
            {
                if (IsWin(vk) && down)
                {
                    if (fresh) pendingWindows.Add(vk);
                    consume = pendingWindows.Contains(vk);
                }
                if (down && !IsMember(vk))
                {
                    // A longer shortcut owns this sequence, including the OEM touchpad toggle.
                    if (chord)
                    {
                        if (provisionalAction == "start") action = "discard-probe";
                        gesture = checkpoint; chord = false; provisionalAction = "";
                    }
                    replay.AddRange(pendingWindows); pendingWindows.Clear(); claimedWindows.Clear();
                    replayCurrent = replay.Count > 0; consume |= replayCurrent;
                }
            }
            bool allowed = SelectedBinding == Binding
                ? keys.All(IsMember) && pendingWindows.Count > 0
                : !Win && !keys.Contains(0x12) && !keys.Contains(0xA4) && !keys.Contains(0xA5);
            if (down && fresh && IsMember(vk) && RequiredHeld && !chord && allowed)
            {
                checkpoint = gesture.Copy(); chord = true;
                provisionalAction = gesture.Down(time);
                if (SelectedBinding == Binding)
                {
                    claimedWindows.UnionWith(pendingWindows);
                    // Audio starts immediately; stop/correction waits for release so a
                    // hardware shortcut cannot stop an existing dictation.
                    if (provisionalAction == "start") action = "start";
                }
                else
                {
                    action = provisionalAction;
                    if (vk == 0x20) { suppressed.Add(vk); consume = true; }
                }
            }
            else if (!RequiredHeld && chord)
            {
                chord = false; string released = gesture.Up(time);
                action = SelectedBinding == Binding && (provisionalAction is "stop" or "correction") ? provisionalAction : released;
                provisionalAction = "";
            }
            if (SelectedBinding == Binding && IsWin(vk) && !down && pendingWindows.Remove(vk))
            {
                consume = true;
                if (!claimedWindows.Remove(vk)) { replay.Add(vk); replayCurrent = true; }
            }
            return new Result(consume, action, replay.ToArray(), replayCurrent);
        }
        public void Reset()
        {
            gesture.Reset(); checkpoint = gesture.Copy(); provisionalAction = "";
            // Preserve withheld releases across cancellation/completion.
            chord = RequiredHeld;
        }
    }
}
