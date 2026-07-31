using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BgmSeparator.Audio;

public readonly record struct AudioDeviceInfo(string Id, string Name);

/// <summary>
/// Owns the WASAPI output and a shared mixer. Everything the plugin plays is added as a
/// mixer input at the fixed mix format (48 kHz stereo float); WasapiOut converts to the
/// selected device's own format in shared mode, so any render device / virtual cable works.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // Fixed internal mix format. Tracks are resampled to this before being added.
    public static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private readonly object _lock = new();
    private MixingSampleProvider _mixer;
    private WasapiOut? _output;
    private string _currentDeviceId = string.Empty;

    public AudioEngine()
    {
        _mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
    }

    public static List<AudioDeviceInfo> ListRenderDevices()
    {
        var result = new List<AudioDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            device.Dispose();
        }
        return result;
    }

    /// <summary>Starts (or restarts) output on the given device id. Empty => system default.</summary>
    public void Start(string deviceId)
    {
        lock (_lock)
        {
            StopInternal();

            using var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            if (string.IsNullOrEmpty(deviceId))
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            else
            {
                try
                {
                    device = enumerator.GetDevice(deviceId);
                }
                catch
                {
                    Services.Log.Warning("[BgmSeparator] Saved audio device not found; using default.");
                    device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
            }

            _output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 150);
            _output.Init(_mixer);
            _output.Play();
            _currentDeviceId = deviceId;
            Services.Log.Info($"[BgmSeparator] Audio output started on '{device.FriendlyName}'.");
        }
    }

    public void EnsureDevice(string deviceId)
    {
        if (_output == null || _currentDeviceId != deviceId)
            Start(deviceId);
    }

    public void AddInput(ISampleProvider input)
    {
        lock (_lock)
        {
            _mixer.AddMixerInput(input);
        }
    }

    public void RemoveInput(ISampleProvider input)
    {
        lock (_lock)
        {
            try { _mixer.RemoveMixerInput(input); }
            catch { /* already removed */ }
        }
    }

    public void ClearInputs()
    {
        lock (_lock)
        {
            _mixer.RemoveAllMixerInputs();
        }
    }

    private void StopInternal()
    {
        if (_output != null)
        {
            try { _output.Stop(); } catch { }
            _output.Dispose();
            _output = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            StopInternal();
            _mixer.RemoveAllMixerInputs();
        }
    }
}
