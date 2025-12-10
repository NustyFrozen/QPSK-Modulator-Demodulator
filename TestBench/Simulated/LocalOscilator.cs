using System;
using System.Numerics;

namespace TestBench.Simulated;
public class NCO
{
    // Public properties
    public double BaseFrequencyHz { get; set; }        // nominal LO frequency
    public double SampleRateHz { get; }                // samples per second

    /// <summary>
    /// Maximum frequency error in ppm (±ppm).
    /// This is interpreted as the bound on total frequency error over time.
    /// </summary>
    public double ppm { get; set; }

    // Current state
    public double CurrentFrequencyHz { get; private set; }
    public double Phase { get; private set; }          // radians, wrapped to [0, 2π)

    private readonly Random rand = new Random();

    // Frequency error model (in ppm)
    private double maxPpmError;       // |ppm|
    private double staticPpmError;    // fixed per NCO instance (like crystal accuracy)
    private double driftPpm;          // slow wander / aging / temperature drift
    private double currentTotalPpm;   // static + drift

    // Drift timing
    private readonly int driftUpdateIntervalSamples;   // how often to update drift
    private int driftCounter;

    /// <summary>
    /// Create a local oscillator generating a complex CW tone.
    /// </summary>
    /// <param name="frequencyHz">Nominal LO frequency (Hz).</param>
    /// <param name="sampleRateHz">Sample rate (samples/second).</param>
    /// <param name="PpmInstabillity">
    /// Maximum frequency error in ppm (±ppm). Use e.g. 1–20 for realistic crystals.
    /// </param>
    /// <param name="initialPhaseRad">Initial phase in radians.</param>
    public NCO(
        double frequencyHz,
        double sampleRateHz,
        double PpmInstabillity = 0.0,
        double initialPhaseRad = 0.0)
    {
        if (sampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be > 0.");

        BaseFrequencyHz = frequencyHz;
        SampleRateHz = sampleRateHz;
        ppm = PpmInstabillity;
        Phase = initialPhaseRad;

        maxPpmError = Math.Abs(ppm);

        // Update drift every ~1 ms (you can tweak this)
        driftUpdateIntervalSamples = (int)Math.Max(1, sampleRateHz * 1e-3);
        driftCounter = 0;

        InitFrequencyError();
        WrapPhase();
    }

    /// <summary>
    /// Generate the next complex sample (I + jQ).
    /// </summary>
    public Complex NextSample()
    {
        UpdateFrequencyModel();

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
    /// Reset oscillator to t = 0 conditions (phase + drift).
    /// Keeps the same static ppm error (same "physical" LO),
    /// but resets the slow drift.
    /// </summary>
    public void Reset(double? initialPhaseRad = null)
    {
        if (initialPhaseRad.HasValue)
            Phase = initialPhaseRad.Value;
        else
            Phase = 0.0;

        // Keep static error (same LO), reset drift
        driftPpm = 0.0;
        currentTotalPpm = staticPpmError;
        driftCounter = 0;

        UpdateCurrentFrequency();
        WrapPhase();
    }

    // ---------- Internal modeling of LO instability ----------

    /// <summary>
    /// Initialize the static (fixed) ppm error of this LO.
    /// </summary>
    private void InitFrequencyError()
    {
        if (maxPpmError > 0.0)
        {
            // Static error: uniform in [-maxPpmError, +maxPpmError]
            staticPpmError = (rand.NextDouble() * 2.0 - 1.0) * maxPpmError;
        }
        else
        {
            staticPpmError = 0.0;
        }

        driftPpm = 0.0;
        currentTotalPpm = staticPpmError;
        UpdateCurrentFrequency();
    }

    /// <summary>
    /// Update slow drift and apply to CurrentFrequencyHz.
    /// </summary>
    private void UpdateFrequencyModel()
    {
        if (maxPpmError <= 0.0)
        {
            // Ideal LO: no ppm error
            CurrentFrequencyHz = BaseFrequencyHz;
            return;
        }

        driftCounter++;
        if (driftCounter >= driftUpdateIntervalSamples)
        {
            driftCounter = 0;

            // Slow random-walk drift (bounded)
            // Step size is a small fraction of maxPpmError per update.
            double stepStdPpm = maxPpmError * 0.001;  // tweak this for "how fast it drifts"
            double stepPpm = (rand.NextDouble() * 2.0 - 1.0) * stepStdPpm;

            driftPpm += stepPpm;

            // Clamp total error to ±maxPpmError
            currentTotalPpm = staticPpmError + driftPpm;
            if (currentTotalPpm > maxPpmError)
            {
                currentTotalPpm = maxPpmError;
                driftPpm = currentTotalPpm - staticPpmError;
            }
            else if (currentTotalPpm < -maxPpmError)
            {
                currentTotalPpm = -maxPpmError;
                driftPpm = currentTotalPpm - staticPpmError;
            }

            UpdateCurrentFrequency();
        }
        // Between drift updates, CurrentFrequencyHz stays the same.
    }

    private void UpdateCurrentFrequency()
    {
        // ppm -> fractional frequency error
        double fracError = currentTotalPpm * 1e-6;
        CurrentFrequencyHz = BaseFrequencyHz * (1.0 + fracError);
    }

    private void WrapPhase()
    {
        const double twoPi = 2.0 * Math.PI;
        Phase %= twoPi;
        if (Phase < 0) Phase += twoPi;
    }
}
