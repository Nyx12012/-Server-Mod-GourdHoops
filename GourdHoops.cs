using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Alta.Inventory;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(GourdHoops.Mod), "GourdHoops", GourdHoops.Mod.Version, "Shadow")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace GourdHoops
{
    public class Mod : MelonMod
    {
        public const string Version = "1.0.0";
        internal static MelonLogger.Instance Log;
        static float next;
        static string lastError;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Config.Load();
            Log.Msg("GourdHoops " + Version + " is running. Settings live in " + Config.FilePath);
        }

        public override void OnUpdate()
        {
            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 2f;
            try
            {
                if (Config.ReloadIfChanged()) Hoops.Undo();
                Hoops.Apply(Config.Current);
            }
            catch (Exception e)
            {
                string message = "Update failed: " + e.Message;
                if (message == lastError) return;
                lastError = message;
                Log.Warning(message + "\n" + e.StackTrace);
            }
        }

        internal static void Say(string message)
        {
            if (Config.Current.Log) Log.Msg(message);
        }
    }

    static class Hoops
    {
        const int GiveUpAfter = 10;
        static readonly FieldInfo positionsField = typeof(Item).GetField("customPositions", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo minField = typeof(PickupDockSettings).GetField("minimumCraftingSize", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo maxField = typeof(PickupDockSettings).GetField("maximumCraftingSize", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly List<Change> changes = new List<Change>();
        static readonly HashSet<string> described = new HashSet<string>();
        static bool done;
        static int tries;

        class Change
        {
            public PickupDockSettings Dock;
            public Item Item;
            public bool AddedItem;
            public Item.CustomDockPositioning Spot;
            public bool Sized;
            public int OldMin;
            public int OldMax;
        }

        internal static void Apply(Config c)
        {
            if (done)
            {
                foreach (Change change in changes)
                {
                    if (change.Dock == null || change.Item == null || !change.Dock.IncludedItems.Contains(change.Item))
                    {
                        Mod.Log.Warning("A hoop change went missing, putting it back.");
                        Undo();
                        break;
                    }
                }
                if (done) return;
            }
            tries++;
            bool report = tries == GiveUpAfter;
            List<PickupDockSettings> docks = FindDocks(c, report);
            List<Item> items = FindItems(c, report);
            if (docks.Count == 0 || items.Count == 0) return;
            foreach (PickupDockSettings dock in docks)
            {
                Describe(dock);
                foreach (Item item in items) Add(dock, item, c);
            }
            HashSet<string> names = new HashSet<string>();
            foreach (PickupDockSettings dock in docks) names.Add(dock.name.ToLowerInvariant());
            done = names.Count == c.Docks.Count && items.Count == c.Items.Count;
            if (!done && report) Mod.Log.Warning("Using what was found and still looking for the rest.");
        }

        static List<PickupDockSettings> FindDocks(Config c, bool report)
        {
            List<PickupDockSettings> found = new List<PickupDockSettings>();
            HashSet<string> seen = new HashSet<string>();
            foreach (PickupDockSettings dock in Resources.FindObjectsOfTypeAll<PickupDockSettings>())
            {
                if (dock == null) continue;
                string name = dock.name.ToLowerInvariant();
                if (!c.Docks.Contains(name)) continue;
                found.Add(dock);
                seen.Add(name);
            }
            if (report)
            {
                foreach (string name in c.Docks)
                {
                    if (!seen.Contains(name)) Mod.Log.Warning("No dock rule named '" + name + "' found yet, still looking.");
                }
            }
            return found;
        }

        static List<Item> FindItems(Config c, bool report)
        {
            Dictionary<uint, Item> byHash = new Dictionary<uint, Item>();
            foreach (Item item in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (item != null && c.Items.Contains(item.Hash) && !byHash.ContainsKey(item.Hash)) byHash[item.Hash] = item;
            }
            List<Item> found = new List<Item>();
            foreach (uint hash in c.Items)
            {
                Item item;
                if (byHash.TryGetValue(hash, out item)) found.Add(item);
                else if (report) Mod.Log.Warning("No item with hash " + hash.ToString(CultureInfo.InvariantCulture) + " found yet, still looking.");
            }
            return found;
        }

        static void Add(PickupDockSettings dock, Item item, Config c)
        {
            foreach (Change old in changes)
            {
                if (old.Dock == dock && old.Item == item) return;
            }
            Change change = new Change();
            change.Dock = dock;
            change.Item = item;
            if (!dock.IncludedItems.Contains(item))
            {
                dock.IncludedItems.Add(item);
                change.AddedItem = true;
            }
            int size = item.Size;
            if (size >= 0 && minField != null && maxField != null)
            {
                int min = (int)minField.GetValue(dock);
                int max = (int)maxField.GetValue(dock);
                if ((min >= 0 && min > size) || (max >= 0 && max < size))
                {
                    change.Sized = true;
                    change.OldMin = min;
                    change.OldMax = max;
                    if (min >= 0 && min > size) minField.SetValue(dock, size);
                    if (max >= 0 && max < size) maxField.SetValue(dock, size);
                    Mod.Say("Widened the size limit on " + dock.name + " from " + min + ".." + max + " so " + item.name + " (size " + size + ") fits.");
                }
            }
            if (positionsField != null && !HasSpot(item, dock))
            {
                Item.CustomDockPositioning[] spots = (Item.CustomDockPositioning[])positionsField.GetValue(item) ?? new Item.CustomDockPositioning[0];
                Item.CustomDockPositioning spot = new Item.CustomDockPositioning(dock, c.Offset, Vector3.zero);
                Item.CustomDockPositioning[] grown = new Item.CustomDockPositioning[spots.Length + 1];
                Array.Copy(spots, grown, spots.Length);
                grown[spots.Length] = spot;
                positionsField.SetValue(item, grown);
                change.Spot = spot;
            }
            changes.Add(change);
            Mod.Say(dock.name + " now takes " + item.name + " (" + item.Hash.ToString(CultureInfo.InvariantCulture) + ").");
        }

        static bool HasSpot(Item item, PickupDockSettings dock)
        {
            Item.CustomDockPositioning[] spots = (Item.CustomDockPositioning[])positionsField.GetValue(item);
            if (spots == null) return false;
            foreach (Item.CustomDockPositioning spot in spots)
            {
                if (spot != null && spot.Type == dock) return true;
            }
            return false;
        }

        static void Describe(PickupDockSettings dock)
        {
            if (!Config.Current.Log || !described.Add(dock.name)) return;
            StringBuilder b = new StringBuilder(dock.name).Append(" before GourdHoops: items ");
            Names(b, dock.IncludedItems);
            b.Append("; tags ");
            Names(b, dock.IncludedTags);
            b.Append("; not tags ");
            Names(b, dock.ExcludedTags);
            b.Append("; size ").Append(dock.MinimumCraftingSize).Append("..").Append(dock.MaximumCraftingSize);
            b.Append("; by hand ").Append(dock.IsHandDockingAllowed ? "yes" : "no");
            Mod.Log.Msg(b.ToString());
        }

        static void Names<T>(StringBuilder b, List<T> list) where T : UnityEngine.Object
        {
            if (list == null || list.Count == 0)
            {
                b.Append("none");
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) b.Append(", ");
                b.Append(list[i] == null ? "null" : list[i].name);
            }
        }

        internal static void Undo()
        {
            for (int i = changes.Count - 1; i >= 0; i--)
            {
                Change change = changes[i];
                if (change.Dock == null || change.Item == null) continue;
                if (change.AddedItem) change.Dock.IncludedItems.Remove(change.Item);
                if (change.Sized)
                {
                    minField.SetValue(change.Dock, change.OldMin);
                    maxField.SetValue(change.Dock, change.OldMax);
                }
                if (change.Spot != null)
                {
                    Item.CustomDockPositioning[] spots = (Item.CustomDockPositioning[])positionsField.GetValue(change.Item);
                    if (spots != null)
                    {
                        List<Item.CustomDockPositioning> kept = new List<Item.CustomDockPositioning>(spots);
                        kept.Remove(change.Spot);
                        positionsField.SetValue(change.Item, kept.ToArray());
                    }
                }
            }
            changes.Clear();
            done = false;
            tries = 0;
        }
    }

    public class Config
    {
        internal const string FileName = "GourdHoops.txt";
        internal static string FilePath;
        internal static Config Current = new Config();
        static DateTime stamp;
        static string lastWarning;

        public readonly List<uint> Items = new List<uint> { 31744 };
        public readonly List<string> Docks = new List<string> { "potion hoops" };
        public Vector3 Offset = Vector3.zero;
        public bool Log = true;
        public readonly List<string> Problems = new List<string>();

        internal static readonly string[] Template =
        {
            "items = 31744",
            "docks = Potion Hoops",
            "offset = 0 0 0",
            "log = on",
        };

        public static Config Parse(string[] lines)
        {
            Config c = new Config();
            bool flag;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string where = "line " + (i + 1) + ": ";
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    c.Problems.Add(where + "expected 'name = value', got '" + line + "'");
                    continue;
                }
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "items":
                        List<uint> items = new List<uint>();
                        bool ok = true;
                        foreach (string part in value.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            uint hash;
                            if (uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out hash))
                            {
                                if (!items.Contains(hash)) items.Add(hash);
                            }
                            else ok = false;
                        }
                        if (!ok || items.Count == 0)
                        {
                            c.Problems.Add(where + "'" + value + "' is not a list of item hashes");
                            continue;
                        }
                        c.Items.Clear();
                        c.Items.AddRange(items);
                        break;
                    case "docks":
                        List<string> docks = new List<string>();
                        foreach (string part in value.Split(','))
                        {
                            string name = part.Trim().ToLowerInvariant();
                            if (name.Length > 0 && !docks.Contains(name)) docks.Add(name);
                        }
                        if (docks.Count == 0)
                        {
                            c.Problems.Add(where + "docks needs at least one dock rule name");
                            continue;
                        }
                        c.Docks.Clear();
                        c.Docks.AddRange(docks);
                        break;
                    case "offset":
                        Vector3 offset;
                        if (Vector(value, out offset)) c.Offset = offset;
                        else c.Problems.Add(where + "offset must be three numbers from -1 to 1, like 0 -0.1 0");
                        break;
                    case "log":
                        if (Switch(value, out flag)) c.Log = flag;
                        else c.Problems.Add(where + "log must be on or off");
                        break;
                    default:
                        c.Problems.Add(where + "unknown setting '" + key + "'");
                        break;
                }
            }
            return c;
        }

        static bool Vector(string text, out Vector3 result)
        {
            result = Vector3.zero;
            string[] parts = text.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;
            float[] v = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
                if (float.IsNaN(v[i]) || v[i] < -1f || v[i] > 1f) return false;
            }
            result = new Vector3(v[0], v[1], v[2]);
            return true;
        }

        static bool Switch(string text, out bool result)
        {
            string t = text.ToLowerInvariant();
            result = t == "on" || t == "yes" || t == "true";
            return result || t == "off" || t == "no" || t == "false";
        }

        public string Describe()
        {
            StringBuilder b = new StringBuilder("items ");
            for (int i = 0; i < Items.Count; i++)
            {
                if (i > 0) b.Append(',');
                b.Append(Items[i].ToString(CultureInfo.InvariantCulture));
            }
            b.Append(", docks ").Append(string.Join(", ", Docks.ToArray()));
            b.Append(", offset ").Append(Offset.x.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(Offset.y.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(Offset.z.ToString(CultureInfo.InvariantCulture));
            b.Append(", log ").Append(Log ? "on" : "off");
            return b.ToString();
        }

        internal static void Load()
        {
            string mods = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string userData = Path.Combine(Directory.GetParent(mods).FullName, "UserData");
            FilePath = Path.Combine(userData, FileName);
            try
            {
                if (!File.Exists(FilePath))
                {
                    Directory.CreateDirectory(userData);
                    File.WriteAllLines(FilePath, Template);
                    Mod.Log.Msg("Wrote " + FilePath + " with the default settings.");
                }
                Read();
            }
            catch (Exception e)
            {
                Mod.Log.Error("Could not read " + FilePath + ", using the default settings. " + e.Message);
            }
        }

        static void Read()
        {
            DateTime seen = File.GetLastWriteTimeUtc(FilePath);
            string[] lines = File.ReadAllLines(FilePath);
            stamp = seen;
            lastWarning = null;
            Config c = Parse(lines);
            foreach (string problem in c.Problems) Mod.Log.Warning(FileName + " " + problem);
            Current = c;
            Mod.Log.Msg("Settings: " + c.Describe());
        }

        internal static bool ReloadIfChanged()
        {
            if (FilePath == null) return false;
            try
            {
                if (!File.Exists(FilePath) || File.GetLastWriteTimeUtc(FilePath) == stamp) return false;
                Read();
                return true;
            }
            catch (Exception e)
            {
                string warning = "Could not reload " + FileName + ", keeping the last settings. " + e.Message;
                if (warning != lastWarning) Mod.Log.Warning(warning);
                lastWarning = warning;
                return false;
            }
        }
    }
}
