using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;

public class SymbolSync
{
    /// <summary>
    /// maximum likelihood  +polyphase filterbank of rrc derivative upsampler symbol sync method
    /// </summary>
    /// <param name="RRC">the original RRC</param>
    private readonly int sps;
    private readonly int tapsPerPhase;

    private readonly double[][] HPhase;   // [phase][tap]
    private readonly double[][] HdPhase;  // [phase][tap]

    private readonly Complex[] buffer;
    private int bufIndex;

    // Phase accumulator: 32-bit fixed point
    // phaseAcc in [0, 2^32); step ≈ 2^32 / sps per sample
    private uint phaseAcc;
    private uint phaseStep;   // nominal step (≈ 2^32 / sps)

    // Loop gains (tune these!)
    private readonly double gainOmega;
    private readonly double gainPhase;
    private double omegaCorrection; // fine adjust around phaseStep
    public delegate void SymbolHandler(Complex mfOut, Complex derOut, double timingError);
    public SymbolSync(double[] RRC,int sps,
        double gainOmega = 1e-5, double gainPhase = 1e-4)
    {
        this.HPhase = BuildPolyphase(RRC,sps,out tapsPerPhase);
        this.HdPhase = BuildPolyphase(HelperFunctions.Derivative(RRC), sps, out _);

        this.sps = sps;
        
        buffer = new Complex[tapsPerPhase];
        bufIndex = 0;

        // Fixed-point phase: 2^32 corresponds to sps samples
        phaseStep = (uint)((1UL << 32) / (ulong)sps);
        phaseAcc = 0;

        this.gainOmega = gainOmega;
        this.gainPhase = gainPhase;
        this.omegaCorrection = 0.0;
    }
    public void ProcessSample(Complex x, SymbolHandler onSymbol = null)
    {
        // Write to circular buffer
        buffer[bufIndex] = x;
        bufIndex--;
        if (bufIndex < 0)
            bufIndex = tapsPerPhase - 1;

        // NCO advance (fixed-point + fine correction)
        // Convert omegaCorrection to fixed point step
        double step = phaseStep + omegaCorrection;
        phaseAcc += (uint)step;

        // Check symbol event: when phaseAcc wraps around a "symbol boundary"
        // We can look at upper bits: log2(sps) bits to choose phase
        // For simplicity: use integer index based on phaseAcc
        int phaseIndex = (int)((phaseAcc >> 24) % (uint)sps); // 8 bits -> 256 steps; adjust as needed

        // We'll generate a symbol approximately every sps input samples.
        // Simplest: approximate symbol event by "one out of sps samples"
        // using a counter based on phaseStep.
        // More accurate: detect rising edge of a specific MSB; here we just
        // do one symbol per input in this example. You likely gate this in your own way.
        bool symbolEvent = ((phaseAcc & 0xFF000000) < (uint)phaseStep); // crude example

        if (!symbolEvent)
            return;

        // Polyphase filtering (matched filter + derivative)
        var h = HPhase[phaseIndex];
        var hd = HdPhase[phaseIndex];

        Complex y = Complex.Zero;
        Complex yp = Complex.Zero;

        int idx = bufIndex;
        for (int k = 0; k < tapsPerPhase; k++)
        {
            Complex s = buffer[idx];
            y += s * h[k];
            yp += s * hd[k];

            idx++;
            if (idx == tapsPerPhase)
                idx = 0;
        }

        // Decision-directed derivative TED
        Complex sHat = QpskSlice(y);
        double e = sHat.Real * yp.Real + sHat.Imaginary * yp.Imaginary;

        // Loop filter: adjust fine frequency and phase
        omegaCorrection += gainOmega * e;
        double phaseAdj = gainPhase * e;

        // Fold phase adjustment into phaseAcc (fixed-point)
        phaseAcc += (uint)(phaseAdj * (1u << 24)); // scaling – tune in practice

        onSymbol?.Invoke(y, yp, e);
    }

    private static Complex QpskSlice(Complex z)
    {
        double re = z.Real >= 0 ? 1.0 : -1.0;
        double im = z.Imaginary >= 0 ? 1.0 : -1.0;
        double norm = 1.0 / Math.Sqrt(2.0);
        return new Complex(re * norm, im * norm);
    }
    // Returns [phase][tap]
    static double[][] BuildPolyphase(double[] h, int sps, out int tapsPerPhase)
    {
        int N = h.Length;
        tapsPerPhase = (int)Math.Ceiling(N / (double)sps);
        int paddedLen = tapsPerPhase * sps;

        var hPad = new double[paddedLen];
        Array.Copy(h, hPad, N);

        var phases = new double[sps][];
        for (int p = 0; p < sps; p++)
            phases[p] = new double[tapsPerPhase];

        int idx = 0;
        for (int k = 0; k < tapsPerPhase; k++)
        {
            for (int p = 0; p < sps; p++)
            {
                phases[p][k] = hPad[idx++];
            }
        }

        return phases; // phases[phase][tap]
    }

}
