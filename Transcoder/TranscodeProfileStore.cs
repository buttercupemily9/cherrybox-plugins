using System.Text.Json;
using System.Text.Json.Serialization;
using CherryBox.Core.Enums;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Transcoder.Plugin;

internal sealed class TranscodeProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true
    };

    private readonly string _profilesPath;
    private readonly object _lock = new();
    private List<TranscodeProfileDto> _cache = new();

    public TranscodeProfileStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _profilesPath = Path.Combine(dataDirectory, "profiles.json");
        Load();
    }

    public IReadOnlyList<TranscodeProfileDto> List()
    {
        lock (_lock)
            return _cache.OrderBy(p => p.Name).ToList();
    }

    public TranscodeProfileDto? Get(Guid id)
    {
        lock (_lock)
            return _cache.FirstOrDefault(p => p.Id == id);
    }

    public TranscodeProfileDto Create(UpsertTranscodeProfileRequest request)
    {
        lock (_lock)
        {
            var profile = ToDto(Guid.NewGuid(), request, 1, DateTimeOffset.UtcNow);
            _cache.Add(profile);
            SaveLocked();
            return profile;
        }
    }

    public TranscodeProfileDto? Update(Guid id, UpsertTranscodeProfileRequest request)
    {
        lock (_lock)
        {
            var index = _cache.FindIndex(p => p.Id == id);
            if (index < 0) return null;
            var existing = _cache[index];
            var updated = ToDto(id, request, existing.Version + 1, DateTimeOffset.UtcNow);
            _cache[index] = updated;
            SaveLocked();
            return updated;
        }
    }

    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            var removed = _cache.RemoveAll(p => p.Id == id);
            if (removed > 0) SaveLocked();
            return removed > 0;
        }
    }

    public TranscodeProfileDto Import(string json)
    {
        var imported = JsonSerializer.Deserialize<TranscodeProfileDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Invalid profile JSON.");
        lock (_lock)
        {
            var profile = imported with
            {
                Id = Guid.NewGuid(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _cache.Add(profile);
            SaveLocked();
            return profile;
        }
    }

    public string Export(Guid id)
    {
        var profile = Get(id) ?? throw new InvalidOperationException("Profile not found.");
        return JsonSerializer.Serialize(profile, JsonOptions);
    }

    private void Load()
    {
        if (!File.Exists(_profilesPath))
        {
            _cache = [CreateDefaultProfile()];
            SaveLocked();
            return;
        }

        try
        {
            var json = File.ReadAllText(_profilesPath);
            _cache = JsonSerializer.Deserialize<List<TranscodeProfileDto>>(json, JsonOptions) ?? [];
            if (_cache.Count == 0)
                _cache = [CreateDefaultProfile()];
        }
        catch
        {
            _cache = [CreateDefaultProfile()];
        }
    }

    private void SaveLocked() =>
        File.WriteAllText(_profilesPath, JsonSerializer.Serialize(_cache, JsonOptions));

    private static TranscodeProfileDto CreateDefaultProfile() => ToDto(
        Guid.NewGuid(),
        new UpsertTranscodeProfileRequest(
            "Standard MP4 (H.264 + AAC)",
            TranscodeContainer.Mp4,
            new TranscodeVideoSettingsDto(TranscodeVideoCodec.H264, TranscodeRateControl.Quality, 23, null, 1920, 1080),
            new TranscodeAudioSettingsDto(TranscodeAudioCodec.Aac, 2, 128, 48000),
            null,
            true),
        1,
        DateTimeOffset.UtcNow);

    private static TranscodeProfileDto ToDto(
        Guid id,
        UpsertTranscodeProfileRequest request,
        int version,
        DateTimeOffset updatedAt) =>
        new(
            id,
            request.Name.Trim(),
            version,
            request.Container,
            request.Video,
            request.Audio,
            request.FileSizeTargetMB,
            request.SkipIfCompatible,
            updatedAt);
}

internal sealed class TranscodeAssignmentsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _lock = new();
    private TranscodeAssignmentsDto _assignments;

    public TranscodeAssignmentsStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "assignments.json");
        _assignments = Load();
    }

    public TranscodeAssignmentsDto Get()
    {
        lock (_lock)
            return _assignments;
    }

    public TranscodeAssignmentsDto Update(UpdateTranscodeAssignmentsRequest request)
    {
        lock (_lock)
        {
            _assignments = new TranscodeAssignmentsDto(
                request.GlobalDefaultProfileId,
                request.GlobalEnabled,
                request.BackgroundWorkerEnabled,
                request.AutoEnqueueOnScan,
                request.LibraryOverrides ?? _assignments.LibraryOverrides,
                request.ProfileLibraryBindings ?? _assignments.ProfileLibraryBindings);
            File.WriteAllText(_path, JsonSerializer.Serialize(_assignments, JsonOptions));
            return _assignments;
        }
    }

    public void SetBackgroundWorkerEnabled(bool enabled)
    {
        lock (_lock)
        {
            _assignments = _assignments with { BackgroundWorkerEnabled = enabled };
            File.WriteAllText(_path, JsonSerializer.Serialize(_assignments, JsonOptions));
        }
    }

    private TranscodeAssignmentsDto Load()
    {
        if (!File.Exists(_path))
        {
            var defaults = new TranscodeAssignmentsDto(
                null,
                true,
                true,
                true,
                Array.Empty<TranscodeLibraryOverrideDto>(),
                Array.Empty<TranscodeProfileLibraryBindingDto>());
            File.WriteAllText(_path, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        try
        {
            return JsonSerializer.Deserialize<TranscodeAssignmentsDto>(File.ReadAllText(_path), JsonOptions)
                ?? new TranscodeAssignmentsDto(null, true, true, true, [], []);
        }
        catch
        {
            return new TranscodeAssignmentsDto(null, true, true, true, [], []);
        }
    }
}
