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
    private MMDevice? _device;
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
            try
            {
                _device = string.IsNullOrEmpty(deviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : enumerator.GetDevice(deviceId);

                if (_device.State != DeviceState.Active)
                    throw new InvalidOperationException($"Output device '{_device.FriendlyName}' is {_device.State}. Reconnect it or select an available output in /bgmsep.");

                _output = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, latency: 150);
                _output.Init(_mixer);
                _output.Play();
                _currentDeviceId = deviceId;
                Services.Log.Info($"[BgmSeparator] Audio output started on '{_device.FriendlyName}'.");
            }
            catch
            {
                // A failed Init must not leave an output that EnsureDevice treats as usable.
                // Keep the chosen routing: falling back to desktop audio could leak BGM.
                StopInternal();
                throw;
            }
        }
    }

    public void EnsureDevice(string deviceId)
    {
        if (_output == null || _output.PlaybackState != PlaybackState.Playing || _currentDeviceId != deviceId)
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
        var output = _output;
        _output = null;
        _currentDeviceId = string.Empty;
        try
        {
            if (output != null)
            {
                try { output.Stop(); } catch { }
                output.Dispose();
            }
        }
        finally
        {
            _device?.Dispose();
            _device = null;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { StopInternal(); }
            finally { _mixer.RemoveAllMixerInputs(); }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
