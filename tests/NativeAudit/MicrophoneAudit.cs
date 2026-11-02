using System;
using System.Collections.Generic;
using System.Linq;
using LocalDictation;
using NAudio.CoreAudioApi;

internal static class MicrophoneAudit
{
    public static void Run()
    {
        Equal("USB", MicrophoneCatalog.Bus("{1}.USB\\VID_046D&PID_0944&MI_04\\9&ABCDEF0&0&0004", "{1}.INTELAUDIO\\CTLR_DEV_E328", "INTELAUDIO"), "offloaded USB endpoint reports USB bus");
        Equal("BTHENUM", MicrophoneCatalog.Bus("{1}.BTHENUM\\{0000111E-0000-1000-8000-00805F9B34FB}_HCIBYPASS", "{1}.INTELAUDIO\\CTLR", "INTELAUDIO"), "offloaded Bluetooth endpoint reports Bluetooth bus");
        Equal("SOUNDWIRE", MicrophoneCatalog.Bus("", "{1}.SOUNDWIRE\\SDCA_FUNCTION_10", "SOUNDWIRE"), "parent device names the bus");
        Equal("HDAUDIO", MicrophoneCatalog.Bus("", "", "HDAudio"), "enumerator is the bus fallback");

        var laptop = Mic("laptop", "Microphone Array on SoundWire Device", "3- Cirrus Logic XU", "00000000-0000-0000-ffff-ffffffffffff", "SOUNDWIRE", 4, MicrophoneCatalog.MicrophoneArray);
        var jack = Mic("jack", "Headset Microphone on SoundWire Device", "5- Cirrus Logic XU", "e3348046", "SOUNDWIRE", 5, MicrophoneCatalog.PersonalMicrophone, DeviceState.Unplugged);
        var webcam = Mic("brio-usb", "Freisprechtelefon", "MX Brio", "004abe49", "USB", 6, MicrophoneCatalog.Speakerphone, DeviceState.NotPresent);
        var webcamOffload = Mic("brio-offload", "Freisprechtelefon", "MX Brio", "004abe49", "USB", 6, MicrophoneCatalog.Speakerphone, DeviceState.NotPresent);
        var bluetooth = Mic("sonos", "Kopfhörer", "Sonos Ace", "eddf0b43", "BTHENUM", 5, MicrophoneCatalog.HeadsetJack, DeviceState.NotPresent);
        var headset = Mic("corsair", "Mikrofon", "CORSAIR HS60 PRO SURROUND", "f662cebc", "USB", 4, MicrophoneCatalog.MicrophoneJack, DeviceState.NotPresent, true);
        var dock = Mic("dock", "Mikrofon", "USB Audio", "af7ae063", "USB", 4, MicrophoneCatalog.MicrophoneJack, DeviceState.NotPresent);
        var yeti = Mic("yeti", "Mikrofon", "Yeti Stereo Microphone", "a1", "USB", 4, MicrophoneCatalog.MicrophoneJack, DeviceState.NotPresent, true);
        var mix = Mic("mix", "Stereo Mix", "Cirrus Logic Virtual Capture Device", "00000000-0000-0000-ffff-ffffffffffff", "SWD", 10, Guid.Empty, DeviceState.Disabled);
        Equal("builtin/internal", Kind(laptop), "SoundWire microphone array is built in");
        Equal("headset/jack", Kind(jack), "combo jack headset microphone");
        Equal("webcam/usb", Kind(webcam), "webcam by name despite speakerphone jack");
        Equal("headset/bluetooth", Kind(bluetooth), "Bluetooth microphone is a headset");
        Equal("headset/usb", Kind(headset), "USB device with headphones is a headset");
        Equal("usb/usb", Kind(dock), "generic USB audio device");
        Equal("microphone/usb", Kind(yeti), "standalone microphone name wins over its headphone jack");
        Equal("virtual/software", Kind(mix), "software capture device is virtual");

        Equal("MX Brio", MicrophoneCatalog.Title(Entry(webcam)), "external device titled by hardware name");
        Equal("Webcam · USB", MicrophoneCatalog.Details(Entry(webcam)), "webcam details");
        Equal("Microphone Array on SoundWire Device", MicrophoneCatalog.Title(Entry(laptop)), "internal device titled by endpoint name");
        Check(MicrophoneCatalog.Details(Entry(laptop)).EndsWith("· Cirrus Logic XU"), "internal details name hardware without Windows index");
        var renamed = Entry(dock); renamed.Description = "Dock-Mikrofon";
        Equal("Dock-Mikrofon", MicrophoneCatalog.Title(renamed), "renamed endpoint keeps the user's name");
        Equal("USB audio device", MicrophoneCatalog.Details(Entry(dock)), "generic USB device avoids repeated USB label");
        Equal("Headset · Audio jack · Cirrus Logic XU", MicrophoneCatalog.Details(Entry(jack)), "combo jack headset names the connection");
        string language = L.Language; L.Language = "de";
        try { Equal("USB-Audiogerät", MicrophoneCatalog.Details(Entry(dock)), "microphone labels keep the German wording"); Equal("Headset · Klinke · Cirrus Logic XU", MicrophoneCatalog.Details(Entry(jack)), "German connection label"); }
        finally { L.Language = language; }

        var found = new List<MicrophoneInfo> { laptop, jack, webcam, webcamOffload, bluetooth, headset, dock, yeti, mix };
        var initial = MicrophonePriority.Initial("", found);
        Equal(7, initial.Count, "first run lists remembered microphones without disabled ones");
        Check(initial.Single(x => x.Ids.Contains("brio-usb")).Ids.Contains("brio-offload"), "endpoints of one physical device share an entry");
        Equal("yeti,jack,corsair,dock,brio-usb,laptop,sonos", String.Join(",", initial.Select(x => x.Ids[0])), "first run ranks dedicated microphones and wired headsets first and Bluetooth below built-in");
        Equal("dock", MicrophonePriority.Initial("dock", found)[0].Ids[0], "previous selection stays first");

        var list = MicrophonePriority.Initial("", new[] { laptop });
        Check(!MicrophonePriority.Merge(list, new[] { dock }, false), "absent devices are not added later");
        var connectedDock = Mic("dock", "Mikrofon", "USB Audio", "af7ae063", "USB", 4, MicrophoneCatalog.MicrophoneJack);
        Check(MicrophonePriority.Merge(list, new[] { laptop, connectedDock }, false) && list.Last().Ids[0] == "dock", "newly connected microphone is appended");
        Check(!MicrophonePriority.Merge(list, new[] { laptop, connectedDock }, false), "known microphones do not change the list");
        var newEndpoint = Mic("dock-2", "Mikrofon", "USB Audio", "af7ae063", "USB", 4, MicrophoneCatalog.MicrophoneJack);
        Check(MicrophonePriority.Merge(list, new[] { newEndpoint }, false) && list.Count == 2 && list[1].Ids.Contains("dock-2"), "new endpoint joins its known device");

        var order = new List<MicrophoneEntry> { new MicrophoneEntry { Ids = { "dock", "dock-2" } }, new MicrophoneEntry { Ids = { "laptop" } } };
        Equal("dock-2,laptop,webcam", String.Join(",", MicrophonePriority.Candidates(order, new[] { "webcam", "laptop", "dock-2" }, "webcam")), "highest connected priority first, then default, then others");
        Equal("laptop", MicrophonePriority.Candidates(order, new[] { "laptop" }, "laptop")[0], "missing preferred microphone falls back to the next one");
        Equal("webcam", MicrophonePriority.Candidates(new MicrophoneEntry[0], new[] { "headset", "webcam" }, "webcam")[0], "empty list follows Windows default");
        Equal(0, MicrophonePriority.Candidates(order, new string[0], "").Count, "no connected microphone");

        Check(RuleOrder.Move(order, order[1], order[0], false) && order[0].Ids[0] == "laptop", "microphone drag reorders entries");
        var rows = new[] { 36.0, 36, 36, 36, 36 };
        Equal(0, RuleOrder.Slot(rows, 2, -72), "drag to the top edge reaches the first slot");
        Equal(4, RuleOrder.Slot(rows, 2, 72), "drag to the bottom edge reaches the last slot");
        Equal(2, RuleOrder.Slot(rows, 2, -17), "small drag keeps the slot");
        Equal(1, RuleOrder.Slot(rows, 2, -19), "passing the middle of the row above takes its slot");
        Equal(3, RuleOrder.Slot(rows, 2, 19), "passing the middle of the row below takes its slot");
        Equal(0, RuleOrder.Slot(new[] { 20.0, 60 }, 1, -20), "taller row reaches the first slot");
        var settings = new Settings { Microphone = "legacy", Microphones = order }; Storage.SaveSettings(settings); var loaded = Storage.LoadSettings();
        Equal("laptop,dock", String.Join(",", loaded.Microphones.Select(x => x.Ids[0])), "settings keep microphone priority");
        Equal("legacy", loaded.Microphone, "legacy microphone setting stays for older versions");
        Equal(2, loaded.Version, "microphone priority keeps settings readable by older versions");
    }

    static MicrophoneInfo Mic(string id, string description, string device, string container, string bus, int formFactor, Guid jack, DeviceState state = DeviceState.Active, bool headphones = false)
    {
        var m = new MicrophoneInfo { Id = id, Name = description + " (" + device + ")", Description = description, Device = device, Container = container, Bus = bus, FormFactor = formFactor, Jack = jack, State = state, HeadphoneSibling = headphones };
        MicrophoneCatalog.Classify(m); return m;
    }
    static MicrophoneEntry Entry(MicrophoneInfo m) { var list = new List<MicrophoneEntry>(); MicrophonePriority.Merge(list, new[] { m }, true); return list[0]; }
    static string Kind(MicrophoneInfo m) => m.Kind + "/" + m.Connection;

    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    static void Equal<T>(T expected, T actual, string name)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual), name + " [" + actual + "]");
    }
}
