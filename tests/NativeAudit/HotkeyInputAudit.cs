using System;
using System.Collections.Generic;
using System.Linq;
using LocalDictation;

internal static class HotkeyInputAudit
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        // Reproduce the old clock bug: a 90 ms tap is handled after 500 ms WASAPI startup.
        var oldClock = new HotkeyInput();
        oldClock.Process(0xA2, true, 990); oldClock.Process(0x5B, true, 1000);
        Check(oldClock.Process(0x5B, false, 1500).Action == "stop", "Processing-time timing reproduces premature stop");
        foreach (string binding in new[] { "ctrl-win", "ctrl-shift-space" })
        foreach (int delay in new[] { 0, 200, 400, 500, 1500 })
        foreach (bool ctrlReleasedFirst in new[] { false, true })
        {
            var delayed = new HotkeyInput { SelectedBinding = binding };
            int trigger = binding == "ctrl-win" ? 0x5B : 0x20;
            delayed.ProcessEvent(0xA2, true, 980, 980);
            if (binding != "ctrl-win") delayed.ProcessEvent(0xA0, true, 990, 990);
            Check(delayed.ProcessEvent(trigger, true, 1000, 1000).Action == "start", "Start without delaying audio");
            int released = ctrlReleasedFirst ? 0xA2 : trigger;
            int other = ctrlReleasedFirst ? trigger : 0xA2;
            Check(delayed.ProcessEvent(released, false, 1090, 1090 + delay).Action == "", "Delayed release remains a short tap");
            delayed.ProcessEvent(other, false, 1100, 1100 + delay);
            // A deliberate later activation still stops normally.
            delayed.ProcessEvent(0xA2, true, 3000, 3000 + delay);
            var nextDown = delayed.ProcessEvent(trigger, true, 3010, 3010 + delay);
            var nextUp = delayed.ProcessEvent(trigger, false, 3100, 3100 + delay);
            Check(nextDown.Action == "stop" || nextUp.Action == "stop", "Later tap stops the original recording");
        }
        var longHold = new HotkeyInput();
        longHold.ProcessEvent(0xA2, true, 1000, 1900);
        longHold.ProcessEvent(0x5B, true, 1010, 1901);
        Check(longHold.ProcessEvent(0x5B, false, 1610, 1902).Action == "stop", "Real hold survives compressed queue processing");
        var doubleTap = new HotkeyInput();
        doubleTap.ProcessEvent(0xA2, true, 1000, 1000);
        doubleTap.ProcessEvent(0x5B, true, 1010, 1010);
        doubleTap.ProcessEvent(0x5B, false, 1080, 1600);
        doubleTap.ProcessEvent(0x5B, true, 1190, 1900);
        Check(doubleTap.ProcessEvent(0x5B, false, 1250, 2100).Action == "correction", "Delayed double tap uses real event interval");
        const long rollover = 1L << 32;
        Check(HotkeyInput.EventTime(uint.MaxValue - 9, rollover + 100) == rollover - 10, "Timestamp unwrap before rollover");
        Check(HotkeyInput.EventTime(20, rollover + 100) == rollover + 20, "Timestamp unwrap after rollover");
        var wrapped = new HotkeyInput();
        wrapped.ProcessEvent(0xA2, true, uint.MaxValue - 29, rollover - 30);
        wrapped.ProcessEvent(0x5B, true, uint.MaxValue - 19, rollover - 20);
        Check(wrapped.ProcessEvent(0x5B, false, 30, rollover + 600).Action == "", "Short tap spanning timer rollover remains recording");
        foreach (int ctrl in new[] { 0x11, 0xA2, 0xA3 })
        foreach (int win in new[] { 0x5B, 0x5C })
        foreach (bool winFirst in new[] { false, true })
        foreach (bool reverseRelease in new[] { false, true })
        {
            int first = winFirst ? win : ctrl, second = winFirst ? ctrl : win;
            foreach (bool touchpad in new[] { false, true })
            {
                var filter = new HotkeyInput();
                var delivered = new List<(int Key, bool Down)>();
                var actions = new List<string>();
                long time = 0;
                void Feed(int vk, bool down)
                {
                    var result = filter.Process(vk, down, time += 10);
                    foreach (int key in result.ReplayWindows) delivered.Add((key, true));
                    if (result.ReplayCurrent || !result.Suppress) delivered.Add((vk, down));
                    if (result.Action != "") actions.Add(result.Action);
                }
                Feed(first, true); Feed(second, true);
                Check(actions.SequenceEqual(new[] { "start" }), "Ctrl+Win starts without waiting for release");
                if (touchpad) { Feed(0x87, true); Feed(0x87, false); }
                Feed(reverseRelease ? first : second, false); Feed(reverseRelease ? second : first, false);
                if (touchpad)
                {
                    Check(actions.SequenceEqual(new[] { "start", "discard-probe" }), "OEM chord discards only probe");
                    Check(delivered.Count(x => x.Key == 0x87 && x.Down) == 1, "Exactly one user F24 passes");
                    Check(delivered.FindIndex(x => x.Key == win && x.Down) < delivered.FindIndex(x => x.Key == 0x87), "Win precedes F24");
                    Check(delivered.FindIndex(x => x.Key == ctrl && x.Down) < delivered.FindIndex(x => x.Key == 0x87), "Ctrl precedes F24");
                }
                else
                {
                    Check(actions.SequenceEqual(new[] { "start" }), "Manual tap keeps dictation");
                    Check(!delivered.Any(x => x.Key == win || x.Key == 0x87), "Manual hotkey sends no Win or F24 to shell");
                }
                foreach (int key in new[] { ctrl, win, 0x87 })
                    Check(delivered.Count(x => x.Key == key && x.Down) == delivered.Count(x => x.Key == key && !x.Down), "All forwarded keys balanced");
            }
        }
        var input = new HotkeyInput();
        input.Process(0xA2, true, 0);
        input.Process(0x5B, true, 1);
        input.Process(0x5B, false, 50);
        Check(input.Process(0x5B, true, 100).Action == "", "Correction waits for complete chord");
        Check(input.Process(0x5B, false, 140).Action == "correction", "Double tap correction retained");
        input.Reset();
        input.Process(0x5B, true, 500);
        Check(input.Process(0x5B, false, 1000).Action == "stop", "Hold release stops");
        input.Reset();
        input.Process(0x5B, true, 2000); input.Process(0x5B, false, 2050);
        input.Process(0x5B, true, 2600);
        Check(input.Process(0x87, true, 2601).Action == "", "Touchpad does not cancel existing dictation");
        input.Process(0x87, false, 2602); input.Process(0x5B, false, 2603);
        input.Process(0x5B, true, 3000);
        Check(input.Process(0x5B, false, 3050).Action == "stop", "Touchpad leaves toggle state intact");
        input = new HotkeyInput();
        Check(input.Process(0x5B, true, 0).Suppress, "Win press buffered");
        var alone = input.Process(0x5B, false, 20);
        Check(alone.ReplayWindows.SequenceEqual(new[] { 0x5B }) && alone.ReplayCurrent && alone.Action == "", "Standalone Win replayed for Start");
        input = new HotkeyInput();
        input.Process(0x5B, true, 0);
        var explorer = input.Process(0x45, true, 1);
        Check(explorer.ReplayWindows.SequenceEqual(new[] { 0x5B }) && explorer.ReplayCurrent && explorer.Action == "", "Win+E forwarded in order");
        Check(!input.Process(0x45, false, 2).Suppress && !input.Process(0x5B, false, 3).Suppress, "Win+E releases forwarded");
        input = new HotkeyInput();
        input.Process(0xA2, true, 0); input.Process(0x5B, true, 1); input.Reset();
        var afterReset = input.Process(0x5B, false, 2);
        Check(afterReset.Suppress && !afterReset.ReplayCurrent && afterReset.Action == "", "Reset does not leak Win release");
        input = new HotkeyInput { SelectedBinding = "ctrl-shift-space" };
        input.Process(0xA2, true, 0); input.Process(0xA0, true, 1);
        var space = input.Process(0x20, true, 2);
        Check(space.Suppress && space.Action == "start", "Alternative shortcut retained");
        Check(input.Process(0x20, false, 3).Suppress, "Space release suppressed");
        Func<int, bool> notHeld = k => false;
        input = new HotkeyInput();
        input.Process(0x27, true, 0); input.Process(0xA2, true, 10);
        Check(input.Process(0x5B, true, 11).Action == "", "Missed release blocks the shortcut without key state");
        input = new HotkeyInput();
        Func<int, bool> arrowReleased = k => k != 0x27;
        input.Process(0x27, true, 0); input.Process(0xA2, true, 10, arrowReleased);
        Check(input.Process(0x5B, true, 11, arrowReleased).Action == "start", "Missed release is dropped using Windows key state");
        input = new HotkeyInput();
        input.Process(0x5B, true, 0);
        Check(input.Process(0xA2, true, 1, notHeld).Action == "start", "Withheld Win survives key-state pruning");
        input = new HotkeyInput();
        input.Process(0xA2, true, 0, k => true); input.Process(0x27, true, 1, k => true);
        Check(input.Process(0x5B, true, 2, k => true).Action == "", "Physically held extra key still blocks the shortcut");
        input = new HotkeyInput();
        input.Process(0x5B, true, 0); input.Process(0x4C, true, 1); input.ClearKeys();
        input.Process(0xA2, true, 5000);
        Check(input.Process(0x5B, true, 5001).Action == "start", "Win+L releases lost on the lock screen are cleared");
        Storage.SaveSettings(new Settings { Hotkey = "ctrl-win" });
        Check(Storage.LoadSettings().Hotkey == "ctrl-win", "Preferred shortcut preserved");
        Storage.SaveSettings(new Settings { Hotkey = "ctrl-shift-space" });
        Check(Storage.LoadSettings().Hotkey == "ctrl-shift-space", "Alternative setting preserved");
        Console.WriteLine("PASS hotkey routing: " + checks + " checks; no actual keyboard input generated");
    }
}
