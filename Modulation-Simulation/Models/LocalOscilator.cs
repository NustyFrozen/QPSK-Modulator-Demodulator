using System;
using System.Numerics;

namespace Modulation_Simulation.Models;

public class LocalOscillator
{
    // Public properties
    public double BaseFrequencyHz { get; set; }        // initial frequency at t = 0
    public double SampleRateHz { get; }                // samples per second
    public double DriftHzPerSecond { get; set; }       // linear drift in Hz/s

    // Current state
    public double CurrentFrequencyHz { get; private set; }
    public double Phase { get; private set; }          // radians, wrapped to [-π, π]

    /// <summary>
    /// Create a local oscillator generating a complex CW tone.
    /// </summary>
    /// <param name="frequencyHz">Initial frequency (Hz) at t = 0.</param>
    /// <param name="sampleRateHz">Sample rate (samples/second).</param>
    /// <param name="driftHzPerSecond">
    /// Frequency drift in Hz/second (positive = drifting up in frequency).
    /// </param>
    /// <param name="initialPhaseRad">Initial phase in radians.</param>
    public LocalOscillator(
        double frequencyHz,
        double sampleRateHz,
        double driftHzPerSecond = 0.0,
        double initialPhaseRad = 0.0)
    {
        if (sampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be > 0.");

        BaseFrequencyHz     = frequencyHz;
        SampleRateHz        = sampleRateHz;
        DriftHzPerSecond    = driftHzPerSecond;
        CurrentFrequencyHz  = BaseFrequencyHz;
        Phase               = initialPhaseRad;
        WrapPhase();
    }

    /// <summary>
    /// Generate the next complex sample (I + jQ).
    /// </summary>
    public Complex NextSample()
    {
        // Update current frequency based on drift (Hz per second)
        if (DriftHzPerSecond != 0.0)
        {
            double freqStepPerSample = DriftHzPerSecond / SampleRateHz;
            CurrentFrequencyHz += freqStepPerSample;
        }

        // Compute phase increment for this sample
        double phaseIncrement = 2.0 * Math.PI * CurrentFrequencyHz / SampleRateHz;
        Phase += phaseIncrement;
        WrapPhase();

        // I = cos(phase), Q = sin(phase)
        return new Complex(Math.Cos(Phase), Math.Sin(Phase));
    }

    /// <summary>
    /// Generate a block of N complex samples into an existing buffer.
    /// </summary>
    public void GenerateBlock(Complex[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

        for (int n = 0; n < count; n++)
        {
            buffer[offset + n] = NextSample();
        }
    }

    /// <summary>
    /// Reset oscillator to t = 0 conditions (phase + frequency).
    /// </summary>
    public void Reset(double? initialPhaseRad = null)
    {
        CurrentFrequencyHz = BaseFrequencyHz;

        if (initialPhaseRad.HasValue)
            Phase = initialPhaseRad.Value;

        WrapPhase();
    }

    private void WrapPhase()
    {
        // Keep phase in [-π, π] using IEEE remainder
        Phase = Math.IEEERemainder(Phase, 2.0 * Math.PI);
    }
}