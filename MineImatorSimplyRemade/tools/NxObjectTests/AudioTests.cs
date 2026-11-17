using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MineImatorSimplyRemade;
using MineImatorSimplyRemade.core;
using MineImatorSimplyRemade.core.audio;
using MineImatorSimplyRemade.core.mdl.mineImator;
using MineImatorSimplyRemade.core.project;
using MineImatorSimplyRemade.core.ui.Panels;
using Silk.NET.OpenAL;

internal static partial class Program
{
    private static AudioClip AudioTestClip(Fixture f, params short[] samples)
    {
        byte[] pcm = new byte[samples.Length * 4];
        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4), samples[i]);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4 + 2), (short)-samples[i]);
        }
        string path = f.NewPath(".wav");
        using (var writer = new BinaryWriter(File.Create(path), Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(44100);
            writer.Write(44100 * 4);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);
            writer.Flush();
        }
        return new AudioClip(pcm, 44100, 2, 16, samples.Length / 44100d, path);
    }

    private static void MiImportAudio(Fixture f)
    {
        var clip = AudioTestClip(f, 1000, 2000, 3000, 4000);
        var source = MiDocument("""
            {"resources":[{"id":"sound","type":"sound","filename":""}],
             "timelines":[
               {"id":"audio","type":"audio","name":"Voice","hide":true,
                "default_values":{"SOUND_OBJ":"sound","SOUND_VOLUME":0.8,"SOUND_PITCH":1.5,"SOUND_START":0.1,"SOUND_END":-0.2},
                "keyframes":{"10":{"SOUND_VOLUME":0.5,"SOUND_PITCH":2,"SOUND_START":0.25,"SOUND_END":0.3},
                             "15":{},"20":{"SOUND_OBJ":null},"25":{"SOUND_OBJ":"missing"},"30":{"SOUND_PITCH":-1}}},
               {"id":"static","type":"audio","animated":false,"current_values":{"SOUND_OBJ":"sound","SOUND_VOLUME":0.2},
                "keyframes":{"99":{"SOUND_VOLUME":0.9}}},
               {"id":"empty","type":"audio","default_values":{"SOUND_OBJ":"sound"}}]}
            """);
        source.Resources[0].Filename = Path.GetFileName(clip.SourcePath);
        source.DirectoryPath = f.Root;
        var warnings = new List<string>();
        var root = new MineImatorLoader().CreateSceneFromMiObject(source, warning: warnings.Add);
        Equal(0, root.Children.Count, "Audio belongs to native tracks, not placeholder scene objects");
        var imports = MiObjectFeatureMapper.AudioTracks(source, warnings.Add);
        Equal(3, imports.Count, "Each sound trigger becomes a separate overlapping-capable clip");
        var first = imports[0].Track;
        Equal(10, first.StartFrame, "No invented audio trigger before the first key");
        Equal("Voice", first.DisplayName, "Audio timeline name");
        Near(0.5f, first.Volume, "Trigger gain");
        Near(2, first.Pitch, "Trigger pitch");
        Near(0.5f, first.SourceOffsetSeconds, "Source trim is converted to unpitched source seconds");
        Near(0.3f, first.EndOffsetSeconds, "Loop extension in playback seconds");
        Check(first.Loop && first.Muted, "Extended and hidden source audio flags");
        var second = imports[1].Track;
        Equal(15, second.StartFrame, "Each key retains its own trigger frame");
        Near(0.8f, second.Volume, "Sparse audio keys use defaults, not the previous trigger");
        Near(1.5f, second.Pitch, "Sparse pitch restores default");
        Near(0.15f, second.SourceOffsetSeconds, "Sparse trim restores default");
        Check(!second.Loop, "Negative end trim does not loop");
        Equal(0, imports[2].Track.StartFrame, "Static audio uses current values at the start");
        Near(0.2f, imports[2].Track.Volume, "Static audio ignores retained keys");
        Check(warnings.Count == 2 && warnings.Any(w => w.Contains("missing")) && warnings.Any(w => w.Contains("Invalid volume")),
            "Missing resources and invalid triggers are reported without dropping other sounds");
    }

    private static void MiImportAudioProject(Fixture f)
    {
        var clip = AudioTestClip(f, 1000, 2000, 3000, 4000);
        string first = f.File("one\\sound.wav", ""), second = f.File("two\\sound.wav", "");
        File.Copy(clip.SourcePath, first, true);
        File.Copy(clip.SourcePath, second, true);
        const string audioJson = """
            {"resources":[{"id":"one","type":"sound","filename":"one\\sound.wav"},
                          {"id":"two","type":"sound","filename":"two\\sound.wav"}],
             "timelines":[{"id":"audio","type":"audio","default_values":{"SOUND_OBJ":"one"},
                           "keyframes":{"5":{"SOUND_PITCH":2,"SOUND_START":0.1,"SOUND_END":0.2},
                                        "8":{"SOUND_OBJ":"two"},"12":{}}}]}
            """;
        string objectPath = f.File("audio.miobject", audioJson);
        WithTextureSheetProject(f, manager =>
        {
            string projectObject = manager.GetAssetFullPath(manager.AddAsset(objectPath, ProjectAssetType.Model));
            var timeline = new Timeline();
            var spawn = new SpawnMenu { Viewport = new Viewport() };
            var import = typeof(SpawnMenu).GetMethod("SpawnMineImatorObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var previousTimeline = Timeline.Instance;
            typeof(Timeline).GetProperty(nameof(Timeline.Instance))!.SetValue(null, timeline);
            try
            {
                import.Invoke(spawn, [projectObject, false, false]);
                Equal(0, timeline.AudioTracks.Count, "Object previews do not import project-wide audio");
                import.Invoke(spawn, [projectObject, false, true]);
                Equal(3, timeline.AudioTracks.Count, "All native timeline clips registered");
                Equal(2, manager.Manifest.Assets.Count(a => a.AssetType == ProjectAssetType.Sound),
                    "Multiple triggers reuse the same copied sound asset");
                import.Invoke(spawn, [projectObject, false, false]);
                Equal(3, timeline.AudioTracks.Count, "Restoring scene objects does not duplicate imported audio");
            }
            finally
            {
                typeof(Timeline).GetProperty(nameof(Timeline.Instance))!.SetValue(null, previousTimeline);
            }
            var entries = timeline.AudioTracks.Select(t => t.ManifestEntry).ToArray();
            Check(entries.All(t => !Path.IsPathRooted(t.AssetPath) && File.Exists(manager.ResolveAssetPath(t.AssetPath))),
                "Imported audio assets are project-local and portable");
            Check(entries[0].AssetPath != entries[1].AssetPath && entries[0].AssetDisplayName == entries[1].AssetDisplayName,
                "Same-named sounds retain distinct resource identities");
            ProjectSceneSerializer.WriteSceneToManifest(manager.Manifest, new Viewport(), timeline);
            string json = JsonSerializer.Serialize(manager.Manifest, AppJsonContext.Default.ProjectManifest);
            var saved = JsonSerializer.Deserialize(json, AppJsonContext.Default.ProjectManifest)!;
            Near(2, saved.AudioTracks[0].Pitch, "Pitch survives project serialization");
            Near(0.2f, saved.AudioTracks[0].SourceOffsetSeconds, "Source trim survives project serialization");
            Near(0.2f, saved.AudioTracks[0].EndOffsetSeconds, "Loop extension survives project serialization");
            File.Delete(first);
            File.Delete(second);
            var restored = new Timeline();
            restored.LoadAudioTracksFromManifest(saved.AudioTracks);
            restored.LoadAudioTracksFromManifest(saved.AudioTracks);
            Equal(3, restored.AudioTracks.Count, "Reload replaces audio tracks rather than duplicating them");
            Check(restored.AudioTracks.All(t => File.Exists(manager.ResolveAssetPath(t.ManifestEntry.AssetPath))),
                "Reload no longer depends on the original exported sounds");
            var legacy = JsonSerializer.Deserialize("""{"assetDisplayName":"sound.wav"}""", AppJsonContext.Default.ProjectAudioTrack)!;
            Near(1, legacy.Pitch, "Existing project tracks retain normal playback speed");
        });
    }

    private static void AudioMixTiming(Fixture f)
    {
        var clip = AudioTestClip(f, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000);
        var entry = new ProjectAudioTrack
        {
            StartFrame = 2, SourceOffsetSeconds = 2.25f / 44100, Pitch = 2, Volume = 0.5f
        };
        var track = new TimelineAudioTrack { ManifestEntry = entry, Clip = clip };
        byte[] Render(int start, int count, params TimelineAudioTrack[] tracks)
        {
            string path = f.NewPath(".wav");
            Check(AudioRenderMixer.RenderTimelineAudioToWav(tracks, 44100, start, count, path), "Audio export writes samples");
            byte[] wave = File.ReadAllBytes(path);
            Equal(44 + count * 4, wave.Length, "Exact stereo PCM output length");
            return wave;
        }
        void Sample(byte[] wave, int frame, float expected)
        {
            short left = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + frame * 4));
            short right = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(46 + frame * 4));
            Check(Math.Abs(left - expected) <= 2 && Math.Abs(right + expected) <= 2,
                $"Mixed stereo frame {frame}: expected {expected}, got {left}/{right}");
        }
        var full = Render(0, 9, track);
        Sample(full, 0, 0);
        Sample(full, 1, 0);
        Sample(full, 2, 1625);
        Sample(full, 3, 2625);
        Sample(full, 4, 3625);
        Sample(full, 5, 0);
        var partial = Render(3, 3, track);
        Sample(partial, 0, 2625);
        Sample(partial, 1, 3625);
        Sample(partial, 2, 0);

        track.Clip = AudioTestClip(f, 1000, 2000, 3000, 4000);
        entry.StartFrame = 0;
        entry.SourceOffsetSeconds = 1.25f / 44100;
        entry.Pitch = 0.5f;
        entry.Volume = 1;
        entry.EndOffsetSeconds = 2f / 44100;
        entry.Loop = true;
        var loop = Render(0, 10, track);
        float[] expected = [2250, 2750, 3250, 3750, 3250, 1750, 1250, 1750, 0, 0];
        for (int i = 0; i < expected.Length; i++) Sample(loop, i, expected[i]);

        entry.SourceOffsetSeconds = 0;
        entry.Pitch = 1;
        entry.EndOffsetSeconds = -2.25f / 44100;
        entry.Loop = false;
        var trimmed = Render(0, 5, track);
        Sample(trimmed, 0, 1000);
        Sample(trimmed, 1, 2000);
        Sample(trimmed, 2, 0);
        var overlap = new TimelineAudioTrack { Clip = track.Clip, ManifestEntry = new() { StartFrame = 1 } };
        var mixed = Render(0, 5, track, overlap);
        Sample(mixed, 1, 3000);
    }

    private static void AudioNativePlayback(Fixture f)
    {
        var previous = Services.AudioEngine;
        using var engine = new AudioEngine();
        engine.Initialize();
        Check(engine.IsInitialized, "OpenAL device initialization (use ALSOFT_DRIVERS=null for silent checks)");
        typeof(Services).GetProperty(nameof(Services.AudioEngine))!.SetValue(null, engine);
        try
        {
            var clip = AudioTestClip(f, Enumerable.Repeat((short)1000, 44100).ToArray());
            WithTextureSheetProject(f, manager =>
            {
                var asset = manager.AddAsset(clip.SourcePath, ProjectAssetType.Sound);
                var timeline = new Timeline();
                timeline.SetFrameRate(10);
                var track = timeline.AddAudioTrackFromAsset(asset, new ProjectAudioTrack
                {
                    StartFrame = 10, SourceOffsetSeconds = 0.5f, Pitch = 2, EndOffsetSeconds = 0.3f, Loop = true, Volume = 0
                });
                Check(SpinWait.SpinUntil(() => track.IsLoaded, TimeSpan.FromSeconds(30)), "FFmpeg decodes the native audio asset");
                try
                {
                    typeof(Timeline).GetField("_isPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(timeline, true);
                    timeline.SetCurrentFrame(10);
                    Check(engine.IsSourcePlaying(track.Source), "Native playback starts at the imported frame");
                    var al = (AL)typeof(AudioEngine).GetField("_al", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
                    al.GetSourceProperty(track.Source.SourceId, SourceFloat.Pitch, out float pitch);
                    Near(2, pitch, "Native OpenAL source receives imported pitch");
                    Check(Math.Abs(engine.GetSourceOffsetSeconds(track.Source) - 0.5f) < 0.08f, "Native playback starts at source trim");
                    timeline.SetCurrentFrame(14);
                    Check(Math.Abs(engine.GetSourceOffsetSeconds(track.Source) - 0.3f) < 0.08f, "Seeking wraps pitched source time in loop extension");
                    timeline.SetCurrentFrame(16);
                    Check(!engine.IsSourcePlaying(track.Source), "Native playback stops at the trimmed/extended end");
                }
                finally
                {
                    timeline.RemoveAudioTrack(track);
                }
            });
        }
        finally
        {
            typeof(Services).GetProperty(nameof(Services.AudioEngine))!.SetValue(null, previous);
        }
    }
}
