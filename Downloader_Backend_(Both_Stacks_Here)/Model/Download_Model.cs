// Models.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Downloader_Backend.Logic;
using System.Text.Json;

namespace Downloader_Backend.Model
{

    public record FormatRequest(string Url, string Session_ID);

    public record DownloadRequest(string Url, string Format, string DownloadId, string Thumbnail = "", string Key = "");

    public record JobActionRequest(string JobId);
    public record Cancel_Pause_Jobs_Request(bool Reload, string Session_ID);

    public class DownloadJob
    {
        [Key]
        public string Id { get; set; } = "";
        public string Url { get; set; } = "";
        public string Format { get; set; } = "";
        public required string Key { get; set; } = "";

        public string Status { get; set; } = "pending";
        public string Method { get; set; } = "unknown";
        public string Title { get; set; } = "media";
        public string Thumbnail { get; set; } = "";


        public long Total { get; set; } = 0;
        public long Downloaded { get; set; } = 0;
        public double Progress { get; set; } = 0.0;
        public string Speed { get; set; } = "0 B/s";

        public string ErrorLog { get; set; } = "nan";

        [JsonIgnore]
        [NotMapped]
        public Process? Process { get; set; } // this can't be serialized or persisted

        [JsonIgnore]
        [NotMapped]
        public CancellationTokenSource? TokenSource { get; set; }

        [JsonIgnore]
        [NotMapped]
        public Task? DownloadTask { get; set; }

        [NotMapped]
        public List<int> ProcessTreePids { get; set; } = [];

        public string OutputPath { get; set; } = "";

        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastProgressAt { get; set; } = DateTimeOffset.UtcNow;

    }

    public class ResumeNewUrlRequest
    {
        public string JobId { get; set; } = "";
        public string NewUrl { get; set; } = "";
    }

    public class DownloadTracker
    {
        public ConcurrentDictionary<string, DownloadJob> Jobs { get; } = new();

        private readonly ConcurrentDictionary<string, List<StreamWriter>> _sseConnections = new();

        // ← ONE PLACE FOR CAMELCASE (used everywhere in SSE)
        public readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public void AddSseConnection(string key, StreamWriter writer)
        {
            var list = _sseConnections.GetOrAdd(key, _ => new List<StreamWriter>());
            lock (list) list.Add(writer);
        }

        public void RemoveSseConnection(string key, StreamWriter writer)
        {
            if (_sseConnections.TryGetValue(key, out var list))
            {
                lock (list) list.Remove(writer);
            }
        }

        public async Task NotifyJobUpdatedAsync(string key)
        {
            if (!_sseConnections.TryGetValue(key, out var connections) || connections.Count == 0)
                return;

            var userJobs = Jobs.Values
                .Where(x => x.Key == key)
                .ToList();

            var json = JsonSerializer.Serialize(userJobs, JsonOptions);   // fetch user connection by its key and store current jobs in that connection. so multiple user can be manage.
            var eventData = $"data: {json}\n\n";

            var copy = connections.ToList();

            foreach (var writer in copy)
            {
                try
                {
                    await writer.WriteAsync(eventData);
                    await writer.FlushAsync();
                }
                catch
                {
                    RemoveSseConnection(key, writer);
                }
            }
        }
    }



    public class File_Saver
    {
        public readonly Dictionary<string, string> fileMap = new()
    {
    { "Strategy_File", Utility.Create_Path(Making_Logs_Path: true) + "/Strategy_File.txt" },
    { "Title_File",    Utility.Create_Path(Making_Logs_Path: true) + "/Title_File.txt" }
    };
    }


    public class GlobalCancellationService
    {
        // Storage for multiple token sources
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _sources = new();

        // Generate a unique key (e.g., GUID string)
        public string GenerateKey()
        {
            return Guid.NewGuid().ToString("N"); // 32-char hex string
        }

        public (string cts_key, CancellationTokenSource cts) Get_Token_With_SC()
        {
            string Cts_Key = GenerateKey();
            var cts = CreateTokenSource(Cts_Key);
            return (Cts_Key, cts);

        }

        // Create a new token source, add to storage, and return it
        public CancellationTokenSource CreateTokenSource(string key)
        {
            var cts = new CancellationTokenSource();
            _sources[key] = cts;
            return cts;
        }

        // Remove a specific token source by key
        public bool RemoveTokenSource(string key)
        {
            if (_sources.TryRemove(key, out var cts))
            {
                if (!cts.IsCancellationRequested)
                {
                    cts?.Cancel();
                }
                cts?.Dispose();
                return true;
            }
            return false;
        }

        // Cancel and dispose all token sources in storage
        public void CancelAndDisposeAll(string Session_ID)
        {
            var Session_CTS = _sources.Where(cts => cts.Key.EndsWith(":" + Session_ID, StringComparison.Ordinal)).ToList();
            foreach (var kvp in Session_CTS)
            {
                RemoveTokenSource(kvp.Key);
            }
        }

        // ❗ New method: remove from storage only (no cancel/dispose)
        public bool DetachTokenSource(string key)
        {
            return _sources.TryRemove(key, out _);
        }

    }


    public class Format
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsAudioOnly { get; set; }
        public string Thumbnail { get; set; } = "";
        public bool IsVideoOnly { get; set; }
        public string Label { get; set; } = "";
    }
}
