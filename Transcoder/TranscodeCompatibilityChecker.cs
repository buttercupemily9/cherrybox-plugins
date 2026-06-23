using CherryBox.Encoding;
using CherryBox.Core.Enums;
using CherryBox.Plugins.Abstractions;

namespace CherryBox.Transcoder.Plugin;

internal static class TranscodeCompatibilityChecker
{
    public static bool IsCompatible(MediaProbeResult? probe, TranscodeProfileDto profile, string filePath)
    {
        if (probe is null) return false;
        if (!profile.SkipIfCompatible) return false;

        var ext = Path.GetExtension(filePath);
        if (!ExtensionMatches(profile.Container, ext)) return false;
        if (!CodecMatches(probe.VideoCodec, profile.Video.Codec)) return false;
        if (!AudioCodecMatches(probe.AudioCodec, profile.Audio.Codec)) return false;

        if (profile.Audio.Channels > 0 && probe.AudioChannels is > 0 &&
            probe.AudioChannels != profile.Audio.Channels)
            return false;

        if (profile.Audio.SampleRateHz > 0 && probe.AudioSampleRateHz is > 0 &&
            probe.AudioSampleRateHz != profile.Audio.SampleRateHz)
            return false;

        if (profile.Video.MaxWidth is > 0 && probe.Width is > 0 && probe.Width > profile.Video.MaxWidth)
            return false;

        if (profile.Video.MaxHeight is > 0 && probe.Height is > 0 && probe.Height > profile.Video.MaxHeight)
            return false;

        return true;
    }

    public static TranscodeProfileSpec ToSpec(TranscodeProfileDto profile, double? durationSeconds)
    {
        var videoBitrate = profile.Video.BitrateKbps;
        if (profile.Video.RateControl == TranscodeRateControl.FileSizeTarget && profile.FileSizeTargetMB is > 0)
        {
            var spec = new TranscodeProfileSpec(
                profile.Container,
                profile.Video.Codec,
                profile.Video.RateControl,
                profile.Video.Crf,
                videoBitrate,
                profile.Video.MaxWidth,
                profile.Video.MaxHeight,
                profile.Audio.Codec,
                profile.Audio.Channels,
                profile.Audio.BitrateKbps,
                profile.Audio.SampleRateHz,
                profile.FileSizeTargetMB,
                durationSeconds);
            videoBitrate = FfmpegFileTranscodeBuilder.ComputeTargetVideoBitrateKbps(spec);
        }

        return new TranscodeProfileSpec(
            profile.Container,
            profile.Video.Codec,
            profile.Video.RateControl,
            profile.Video.Crf,
            videoBitrate,
            profile.Video.MaxWidth,
            profile.Video.MaxHeight,
            profile.Audio.Codec,
            profile.Audio.Channels,
            profile.Audio.BitrateKbps,
            profile.Audio.SampleRateHz,
            profile.FileSizeTargetMB,
            durationSeconds);
    }

    public static string TargetExtension(TranscodeContainer container) => container switch
    {
        TranscodeContainer.Mp4 => ".mp4",
        TranscodeContainer.Mkv => ".mkv",
        TranscodeContainer.Avi => ".avi",
        TranscodeContainer.Webm => ".webm",
        _ => ".mp4"
    };

    private static bool ExtensionMatches(TranscodeContainer container, string ext) =>
        string.Equals(ext, TargetExtension(container), StringComparison.OrdinalIgnoreCase);

    private static bool CodecMatches(string? probeCodec, TranscodeVideoCodec codec)
    {
        if (string.IsNullOrWhiteSpace(probeCodec)) return false;
        var normalized = probeCodec.ToLowerInvariant();
        return codec switch
        {
            TranscodeVideoCodec.H264 => normalized is "h264" or "avc1",
            TranscodeVideoCodec.H265 => normalized is "hevc" or "h265",
            TranscodeVideoCodec.H266 => normalized is "vvc" or "h266",
            TranscodeVideoCodec.Av1 => normalized is "av1",
            TranscodeVideoCodec.Vp8 => normalized is "vp8",
            TranscodeVideoCodec.Vp9 => normalized is "vp9",
            TranscodeVideoCodec.Mpeg4 => normalized is "mpeg4",
            TranscodeVideoCodec.Mpeg2 => normalized is "mpeg2video" or "mpeg2",
            _ => false
        };
    }

    private static bool AudioCodecMatches(string? probeCodec, TranscodeAudioCodec codec)
    {
        if (string.IsNullOrWhiteSpace(probeCodec)) return false;
        var normalized = probeCodec.ToLowerInvariant();
        return codec switch
        {
            TranscodeAudioCodec.Aac => normalized is "aac",
            TranscodeAudioCodec.Mp3 => normalized is "mp3" or "mp2",
            TranscodeAudioCodec.Opus => normalized is "opus",
            TranscodeAudioCodec.Vorbis => normalized is "vorbis",
            TranscodeAudioCodec.Ac3 => normalized is "ac3",
            TranscodeAudioCodec.Flac => normalized is "flac",
            _ => false
        };
    }
}

internal sealed class TranscodeAssignmentResolver
{
    public Guid? ResolveProfileId(Guid libraryFolderId, TranscodeAssignmentsDto assignments, TranscodeProfileStore profiles)
    {
        if (!assignments.GlobalEnabled) return null;

        var libraryOverride = assignments.LibraryOverrides
            .FirstOrDefault(o => o.LibraryFolderId == libraryFolderId && o.Enabled);
        if (libraryOverride is not null && profiles.Get(libraryOverride.ProfileId) is not null)
            return libraryOverride.ProfileId;

        foreach (var binding in assignments.ProfileLibraryBindings)
        {
            if (binding.LibraryFolderIds.Contains(libraryFolderId) && profiles.Get(binding.ProfileId) is not null)
                return binding.ProfileId;
        }

        if (assignments.GlobalDefaultProfileId is { } defaultId && profiles.Get(defaultId) is not null)
            return defaultId;

        return profiles.List().FirstOrDefault()?.Id;
    }
}
