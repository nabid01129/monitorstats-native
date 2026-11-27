using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MonitorStatsNative {
    // Raw Windows counters count bytes, not sampled rates. Never integrate an
    // instantaneous KB/s value: short bursts between polls must not be lost.
    internal sealed class DriveWriteHistory : IDisposable {
        const long BucketTicks = TimeSpan.TicksPerMinute * 5;
        const int Slots = 8641, DayBuckets = 288, MonthBuckets = 8640;
        const int Signature = 0x31574844;
        sealed class Entry {
            public readonly long[] Epochs = new long[Slots];
            public readonly ulong[] Bytes = new ulong[Slots];
            public DateTime Started, LastSample;
            public ulong Read, Write, Clock;
            public bool Baseline, Dirty;
            public long TotalsEpoch = -1;
            public double Day, Month;
        }
        readonly string folder;
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        DateTime nextSave;
        public DriveWriteHistory() : this(Path.Combine(Options.Folder, "drive-write-history")) { }
        internal DriveWriteHistory(string folder) { this.folder = folder; }
        string FileFor(string id) {
            using (var hash = SHA256.Create())
                return Path.Combine(folder, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id.ToUpperInvariant()))).Replace("-", "") + ".bin");
        }
        Entry Load(string id, DateTime now) {
            var entry = new Entry { Started = now, Dirty = true };
            try {
                string path = FileFor(id);
                if (!File.Exists(path)) return entry;
                using (var reader = new BinaryReader(File.OpenRead(path))) {
                    if (reader.BaseStream.Length > 32 + Slots * 16 || reader.ReadInt32() != Signature) return entry;
                    long started = reader.ReadInt64(); int count = reader.ReadInt32();
                    if (started < 0 || started > now.Ticks || count < 0 || count > Slots || reader.BaseStream.Length != 16 + count * 16L) return entry;
                    var loaded = entry;
                    loaded.Started = new DateTime(started, DateTimeKind.Utc);
                    long current = now.Ticks / BucketTicks;
                    for (int i = 0; i < count; i++) {
                        long epoch = reader.ReadInt64(); ulong bytes = reader.ReadUInt64();
                        if (epoch <= current - MonthBuckets || epoch > current) continue;
                        int slot = (int)(epoch % Slots);
                        if (loaded.Epochs[slot] != 0) return new Entry { Started = now, Dirty = true };
                        loaded.Epochs[slot] = epoch; loaded.Bytes[slot] = bytes;
                    }
                    loaded.Dirty = false;
                    return loaded;
                }
            } catch { return new Entry { Started = now, Dirty = true }; }
        }
        static void Totals(Entry entry, long current) {
            if (entry.TotalsEpoch == current) return;
            entry.Day = 0; entry.Month = 0;
            for (int i = 0; i < Slots; i++) {
                long age = current - entry.Epochs[i];
                if (age >= 0 && age < MonthBuckets) {
                    entry.Month += entry.Bytes[i];
                    if (age < DayBuckets) entry.Day += entry.Bytes[i];
                }
            }
            entry.TotalsEpoch = current;
        }
        internal Data Sample(string id, ulong read, ulong write, ulong clock, ulong frequency, DateTime now) {
            if (String.IsNullOrEmpty(id)) return new Data();
            Entry entry;
            bool persistent = !id.StartsWith("session:",StringComparison.Ordinal);
            if (!entries.TryGetValue(id, out entry)) { entry = persistent?Load(id, now):new Entry{Started=now}; entries.Add(id, entry); }
            long current = now.Ticks / BucketTicks;
            Totals(entry, current);
            double? readRate = null, writeRate = null;
            // Baselines deliberately are not persisted. A restart, counter reset,
            // missing identity or backwards clock must not create invented writes.
            if (entry.Baseline && clock > entry.Clock && frequency > 0 && now >= entry.LastSample) {
                double seconds = (double)(clock - entry.Clock) / frequency;
                if(read>=entry.Read)readRate = (read - entry.Read) / seconds / 1024;
                ulong delta = write>=entry.Write?write-entry.Write:0;
                if(write>=entry.Write)writeRate = delta / seconds / 1024;
                // After a long pause the counter delta has no reliable event
                // times. Do not place possibly old writes in the newest bucket.
                if (delta > 0 && now-entry.LastSample<=TimeSpan.FromMinutes(5)) {
                    int slot = (int)(current % Slots);
                    if (entry.Epochs[slot] != current) { entry.Epochs[slot] = current; entry.Bytes[slot] = 0; }
                    if (UInt64.MaxValue - entry.Bytes[slot] >= delta) {
                        entry.Bytes[slot] += delta; entry.Day += delta; entry.Month += delta; entry.Dirty = true;
                    }
                }
            }
            entry.Read = read; entry.Write = write; entry.Clock = clock;
            entry.LastSample = now; entry.Baseline = true;
            var result = persistent?Snapshot(id,now):new Data();
            result["readKBps"]=readRate; result["writeKBps"]=writeRate;
            return result;
        }
        internal Data Snapshot(string id,DateTime now) {
            Entry entry;
            if(String.IsNullOrEmpty(id)||!entries.TryGetValue(id,out entry))return new Data();
            Totals(entry,now.Ticks/BucketTicks);
            return Data.Of("writes24hBytes",entry.Day,"writes30dBytes",entry.Month,"writeHistoryStartedUtc",entry.Started,
                "writeHistorySource","Windows logical-volume write counters; recorded while MonitorStats runs; 5-minute buckets");
        }
        internal void Retain(IEnumerable<string> active) {
            var present = new HashSet<string>(active, StringComparer.OrdinalIgnoreCase);
            foreach (string id in entries.Keys.Where(id => !present.Contains(id)).ToArray()) {
                Save(id, entries[id]); entries.Remove(id);
            }
        }
        void Save(string id, Entry entry) {
            if (!entry.Dirty || id.StartsWith("session:",StringComparison.Ordinal)) return;
            try {
                Directory.CreateDirectory(folder);
                string path = FileFor(id), temp = path + ".tmp";
                long current = entry.LastSample.Ticks / BucketTicks;
                int count = 0;
                for (int i = 0; i < Slots; i++) if (entry.Bytes[i] > 0 && entry.Epochs[i] > current - MonthBuckets && entry.Epochs[i] <= current) count++;
                using (var writer = new BinaryWriter(File.Create(temp))) {
                    writer.Write(Signature); writer.Write(entry.Started.Ticks); writer.Write(count);
                    for (int i = 0; i < Slots; i++) if (entry.Bytes[i] > 0 && entry.Epochs[i] > current - MonthBuckets && entry.Epochs[i] <= current) {
                        writer.Write(entry.Epochs[i]); writer.Write(entry.Bytes[i]);
                    }
                }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                entry.Dirty = false;
            } catch { /* A locked or read-only history must not interrupt telemetry. */ }
        }
        internal void Checkpoint(DateTime now) {
            if (now < nextSave) return;
            nextSave = now.AddMinutes(5);
            foreach (var entry in entries) Save(entry.Key, entry.Value);
        }
        public void Dispose() { foreach (var entry in entries) Save(entry.Key, entry.Value); }
    }
}
