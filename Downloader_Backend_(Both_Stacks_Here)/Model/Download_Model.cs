// Models.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Downloader_Backend.Model
{

    public record FormatRequest(string Url);

    public record DownloadRequest(string Url, string Format, string DownloadId, string Thumbnail = "", string Key = "");

    public record JobActionRequest(string JobId);

    public class DownloadJob(string id, string url, string format)
    {
        [Key]
        public string Id { get; } = id;
        public string Url { get; set; } = url;
        public string Format { get; set; } = format;
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