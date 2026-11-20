using System;
using System.Collections.Generic;
using System.Text;
using NAudio.Wave;
using NAudio.CoreAudioApi;

class Program
{
    // ====== Basic settings ======
    private const int SampleRate = 48000;          // Default sample rate (used if device has no MixFormat)
    private const int DefaultDurationSeconds = 5;   // Default playback duration
    private const float Frequency = 1000f;          // Test tone frequency (1000Hz = 1kHz sine wave)

    // ====== Currently selected audio device and its channel count ======
    private static MMDevice SelectedDevice = null;
    private static int SelectedDeviceChannels = 8;  // Number of channels supported by current device, default 8 (7.1)

    // ====== Channel combination table ======
    // Channels : channel index array for this option (0-based), e.g. Ch1+Ch2 = index 0,1
    // FullName : full display name (used in playback message)
    // ShortName: short name for menu display
    // Using ValueTuple (C# 7+ named tuple) instead of Tuple, so we can use .Channels / .FullName directly
    private static readonly Dictionary<string, (int[] Channels, string FullName, string ShortName)> FactoryMap =
        new Dictionary<string, (int[] Channels, string FullName, string ShortName)>()
    {
        { "1", (new[] { 0, 1 }, "Ch 1 + Ch 2", "Ch1+2") },
        { "2", (new[] { 2, 3 }, "Ch 3 + Ch 4", "Ch3+4") },
        { "3", (new[] { 4, 5 }, "Ch 5 + Ch 6", "Ch5+6") },
        { "4", (new[] { 6, 7 }, "Ch 7 + Ch 8", "Ch7+8") }
    };

    static void Main(string[] args)
    {
        Console.WriteLine("==========================================================================");
        Console.WriteLine(" MULTI-CHANNEL TONE GENERATOR");
        Console.WriteLine("==========================================================================");

        // Let user select output device first
        SelectAudioDevice();
        PrintMenu();

        // Main loop: keep waiting for user input until "Q" is entered
        while (true)
        {
            // Build available options string dynamically, e.g. 1/2/C/Q or 1/2/3/4/C/Q
            string availableOptions = GetAvailableOptionsString();
            Console.Write($"\nSELECT OPTION [{availableOptions}]: ");
            string input = Console.ReadLine();
            if (input == null) continue;

            string cmd = input.Trim().ToLower();

            // User typed Q or exit -> quit program
            if (cmd == "q" || cmd == "exit")
            {
                Console.WriteLine("Program terminated.");
                break;
            }

            // User typed C -> re-select / rescan device
            if (cmd == "c")
            {
                SelectAudioDevice();
                PrintMenu();
                continue;
            }

            // Check if input matches one of the channel options (e.g. "1", "2"...)
            if (FactoryMap.TryGetValue(cmd, out var target))
            {
                // Calculate the minimum channel count required by this option
                // target.Channels[1] is the highest channel index in this pair, +1 = required channel count
                // e.g. option 2 needs Ch3+Ch4 (index 2,3) -> max index=3 -> requires at least 4 channels
                int requiredChannelCount = target.Channels[1] + 1;

                if (requiredChannelCount > SelectedDeviceChannels)
                {
                    Console.WriteLine($"[WARN] Option [{cmd}] requires {requiredChannelCount} channels, but current device only supports {SelectedDeviceChannels} channels!");
                    continue;
                }

                // Ask user for playback duration
                int duration = PromptDuration();

                // Start playing the test tone
                PlayWASAPITone(target.Channels, target.FullName, duration);
            }
            else
            {
                // Input does not match any option -> show error message
                Console.WriteLine($"[WARN] Invalid option! Please input [{availableOptions}].");
            }
        }
    }

    /// <summary>
    /// Build the available options string based on current device's channel count.
    /// e.g. if device only supports 4 channels, only show "1/2/C/Q"
    /// </summary>
    private static string GetAvailableOptionsString()
    {
        List<string> opts = new List<string>();

        // Loop through FactoryMap and check if each option fits the current device's channel count
        foreach (var kv in FactoryMap)
        {
            int requiredChannelCount = kv.Value.Channels[1] + 1;
            if (SelectedDeviceChannels >= requiredChannelCount)
            {
                opts.Add(kv.Key);
            }
        }

        opts.Add("C");
        opts.Add("Q");
        return string.Join("/", opts);
    }

    /// <summary>
    /// Print current device info and available menu options
    /// </summary>
    private static void PrintMenu()
    {
        string deviceName = SelectedDevice != null ? SelectedDevice.FriendlyName : "None";
        Console.WriteLine("==========================================================================");
        Console.WriteLine($" Current Device: {deviceName} ({SelectedDeviceChannels} Ch)");

        // If no device is available, only show C / Q, hide options 1~4
        if (SelectedDevice == null || SelectedDeviceChannels == 0)
        {
            Console.WriteLine(" [C] Change / Rescan Device   [Q] Quit");
            Console.WriteLine(" [WARN] No active audio output device detected!");
        }
        else
        {
            StringBuilder menuLine = new StringBuilder(" ");

            // Show available options (1~4) based on device's channel count
            foreach (var kv in FactoryMap)
            {
                int requiredChannelCount = kv.Value.Channels[1] + 1;
                if (SelectedDeviceChannels >= requiredChannelCount)
                {
                    menuLine.Append($"[{kv.Key}] {kv.Value.ShortName}   ");
                }
            }

            menuLine.Append("[C] Change Device   [Q] Quit");
            Console.WriteLine(menuLine.ToString());

            // If device supports less than 8 channels (7.1), notify user that higher options are disabled
            if (SelectedDeviceChannels < 8)
            {
                Console.WriteLine($" [NOTE] Device supports {SelectedDeviceChannels} Ch max. Higher channels are disabled.");
            }
        }

        Console.WriteLine("==========================================================================");
    }

    /// <summary>
    /// Scan all available audio output devices on the system and let user pick one
    /// </summary>
    private static void SelectAudioDevice()
    {
        MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        // If no device found, reset state and return
        if (devices.Count == 0)
        {
            Console.WriteLine("\n[ERROR] No active audio output devices found!");
            SelectedDevice = null;
            SelectedDeviceChannels = 0; // Set channel count to 0, menu will lock all playback options
            return;
        }

        // List all detected devices with their channel count
        Console.WriteLine("\n[Select Output Device]");
        for (int i = 0; i < devices.Count; i++)
        {
            // Get channel count from device's MixFormat, default to 2 (stereo) if not available
            int ch = devices[i].AudioClient.MixFormat != null ? devices[i].AudioClient.MixFormat.Channels : 2;
            Console.WriteLine($" [{i + 1}] {devices[i].FriendlyName} ({ch} Ch)");
        }

        // Wait for user to enter a valid device number
        while (true)
        {
            Console.Write($"Select Device [1-{devices.Count}] (Default: 1): ");
            string input = Console.ReadLine();

            // If user just presses Enter, select first device by default
            if (string.IsNullOrWhiteSpace(input))
            {
                SelectedDevice = devices[0];
                break;
            }

            if (int.TryParse(input.Trim(), out int choice) && choice >= 1 && choice <= devices.Count)
            {
                SelectedDevice = devices[choice - 1];
                break;
            }

            Console.WriteLine("[WARN] Invalid selection. Please try again.");
        }

        // Record channel count of the selected device, used by menu and playback logic later
        WaveFormat mixFormat = SelectedDevice.AudioClient.MixFormat;
        SelectedDeviceChannels = mixFormat != null ? mixFormat.Channels : 2;

        Console.WriteLine($"-> Selected Device: {SelectedDevice.FriendlyName} ({SelectedDeviceChannels} Channels)\n");
    }

    /// <summary>
    /// Ask user for playback duration. Use default value if input is empty or invalid.
    /// </summary>
    private static int PromptDuration()
    {
        Console.Write("Set Duration in Seconds [Default: 5]: ");
        string durationInput = Console.ReadLine();

        // No input -> use default value
        if (string.IsNullOrWhiteSpace(durationInput))
        {
            return DefaultDurationSeconds;
        }

        // Not a valid positive integer -> show warning and use default value
        if (!int.TryParse(durationInput.Trim(), out int duration) || duration <= 0)
        {
            Console.WriteLine("[WARN] Invalid integer! Using default 5s.");
            return DefaultDurationSeconds;
        }

        return duration;
    }

    /// <summary>
    /// Play test tone on selected channels using WASAPI
    /// </summary>
    /// <param name="targetChannels">Channel index array to generate sound on, e.g. [0,1] means Ch1+Ch2</param>
    /// <param name="channelName">Display name for the channel combination</param>
    /// <param name="durationSeconds">Playback duration in seconds</param>
    private static void PlayWASAPITone(int[] targetChannels, string channelName, int durationSeconds)
    {
        try
        {
            // If no device selected (should not happen, but kept as safety check), use system default output device
            MMDevice device = SelectedDevice;
            if (device == null)
            {
                MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            // Get actual sample rate from device, use default 48000Hz if not available
            WaveFormat mixFormat = device.AudioClient.MixFormat;
            int sampleRate = mixFormat != null ? mixFormat.SampleRate : SampleRate;

            Console.WriteLine($"[PLAY] Output: {channelName} | {sampleRate} Hz | {durationSeconds}s (Press any key to stop)");

            // Create playback format: 32-bit float, multi-channel (based on device's actual channel count)
            WaveFormatExtensible format = new WaveFormatExtensible(
                sampleRate,
                32,
                SelectedDeviceChannels
            );

            // Create custom audio data provider (generates sine wave on target channels)
            MultiChannelToneProvider waveProvider = new MultiChannelToneProvider(format, Frequency, targetChannels, durationSeconds);

            WasapiOut wasapiOut;
            try
            {
                // Try Shared Mode first (shares device with other apps, more stable)
                wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
                wasapiOut.Init(waveProvider);
            }
            catch
            {
                // If Shared Mode fails (e.g. device does not support it), fall back to Exclusive Mode
                wasapiOut = new WasapiOut(device, AudioClientShareMode.Exclusive, false, 50);
                wasapiOut.Init(waveProvider);
            }

            // "using" block will automatically call Dispose to release audio resources when done
            using (wasapiOut)
            {
                wasapiOut.Play();

                // Wait until playback finishes, or user presses a key to stop early
                // Check for key press every 100ms
                int elapsedMs = 0;
                int totalMs = durationSeconds * 1000;
                bool stoppedByUser = false;

                while (elapsedMs < totalMs)
                {
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey(intercept: true); // intercept: true -> key press will not be shown on screen
                        stoppedByUser = true;
                        break;
                    }

                    System.Threading.Thread.Sleep(100);
                    elapsedMs += 100;
                }

                wasapiOut.Stop();

                if (stoppedByUser)
                {
                    Console.WriteLine("[STOP] Playback interrupted by user.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] WASAPI output failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Custom audio data provider (IWaveProvider).
/// Generates audio data for a fixed duration.
/// Only target channels get a sine wave (test tone), other channels stay silent (0).
/// </summary>
public class MultiChannelToneProvider : IWaveProvider
{
    // Bytes used by one float sample (32-bit float = 4 bytes)
    private const int BytesPerFloatSample = 4;

    private readonly WaveFormat _waveFormat;       // Audio format (sample rate, channel count, etc.)
    private readonly float _frequency;             // Test tone frequency (Hz)
    private readonly HashSet<int> _targetChannels; // Set of target channel indexes (for fast lookup)
    private readonly long _totalSamples;           // Total number of samples to generate (all channels combined)
    private long _currentSample;                   // Current sample position (playback progress)

    public WaveFormat WaveFormat => _waveFormat;

    /// <param name="waveFormat">Output audio format</param>
    /// <param name="frequency">Test tone frequency</param>
    /// <param name="targetChannels">Channel index array to generate sound on</param>
    /// <param name="durationSeconds">Total playback duration in seconds</param>
    public MultiChannelToneProvider(WaveFormat waveFormat, float frequency, int[] targetChannels, int durationSeconds)
    {
        _waveFormat = waveFormat;
        _frequency = frequency;
        _targetChannels = new HashSet<int>(targetChannels);

        // Total samples = sample rate * channel count * duration
        // e.g. 48000Hz, 8 channels, 5 seconds -> 48000*8*5 samples
        _totalSamples = (long)waveFormat.SampleRate * waveFormat.Channels * durationSeconds;
        _currentSample = 0;
    }

    /// <summary>
    /// NAudio playback engine calls this method repeatedly to get audio data to play
    /// </summary>
    /// <param name="buffer">Buffer to write audio data into</param>
    /// <param name="offset">Starting write position in buffer (bytes)</param>
    /// <param name="count">Amount of data requested (bytes)</param>
    /// <returns>Actual bytes written; returns 0 when playback is finished</returns>
    public int Read(byte[] buffer, int offset, int count)
    {
        // Max number of samples that fit in this call
        int totalRequestedSamples = count / BytesPerFloatSample;

        // Remaining samples left to play, take the smaller value (avoid exceeding total length)
        int samplesToRead = (int)Math.Min(totalRequestedSamples, _totalSamples - _currentSample);

        // Playback already finished, return 0 to signal end
        if (samplesToRead <= 0)
            return 0;

        // WaveBuffer lets us write directly as float array into the byte buffer
        WaveBuffer waveBuffer = new WaveBuffer(buffer) { ByteBufferCount = offset + count };

        for (int i = 0; i < samplesToRead; i++)
        {
            // Determine which channel this sample belongs to (interleaved: L R L R... cycling by channel count)
            int channelIndex = (int)((_currentSample + i) % _waveFormat.Channels);

            // Determine which audio frame this sample belongs to, used to calculate actual time
            long frameIndex = (_currentSample + i) / _waveFormat.Channels;

            float sampleValue = 0.0f; // Default is silence

            // Only generate sine wave on target channels, other channels stay silent
            if (_targetChannels.Contains(channelIndex))
            {
                double time = (double)frameIndex / _waveFormat.SampleRate;
                // Standard sine wave formula: A * sin(2π * f * t), amplitude 0.5 to avoid clipping
                sampleValue = 0.5f * (float)Math.Sin(2 * Math.PI * _frequency * time);
            }

            waveBuffer.FloatBuffer[(offset / BytesPerFloatSample) + i] = sampleValue;
        }

        _currentSample += samplesToRead;

        // Return actual bytes written
        return samplesToRead * BytesPerFloatSample;
    }
}
