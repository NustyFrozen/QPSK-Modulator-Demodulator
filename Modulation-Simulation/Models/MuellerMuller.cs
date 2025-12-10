using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Runtime.Intrinsics.X86;

public class MuellerMuller
{
    private readonly double samplesPerSymbol;   // e.g. 8.0
    private readonly double kp;
    private readonly double ki;

    // timing state: time = baseIndex + mu, where 0 <= mu < 1
    private int baseIndex;          // integer sample index into buffer
    private double mu;              // fractional position between baseIndex and baseIndex+1
    private double ncoIntegral;     // integrator in timing loop

    private Complex prevSample;
    private Complex prevDecision;
    private bool hasPrev;

    private readonly List<Complex> buffer = new List<Complex>();

    public MuellerMuller(double samplesPerSymbol, double kp, double ki)
    {
        this.samplesPerSymbol = samplesPerSymbol;
        this.kp = kp;
        this.ki = ki;

        baseIndex = 0;
        mu = 0.0;
        ncoIntegral = 0.0;
        hasPrev = false;
    }

    public List<Complex> Process(Complex[] incomingMfSamples)
    {
        // append new matched-filter outputs
        buffer.AddRange(incomingMfSamples);

        var outputSamples = new List<Complex>();

        // need at least 2 samples for linear interpolation
        while (baseIndex + 1 < buffer.Count)
        {
            // interpolate at current timing phase
            Complex currSample = Lerp(buffer, baseIndex, mu);
            Complex currDecision = CostasLoopQpsk.GetSign(currSample);

            double advance; 

            if (hasPrev)
            {
                // Mueller–Muller TED: e = { d_{k-1}* x_k - d_k* x_{k-1} }
                Complex term1 = Complex.Conjugate(prevDecision) * currSample;
                Complex term2 = Complex.Conjugate(currDecision) * prevSample;
                double e = (term1 - term2).Real;

                // PI loop filter
                ncoIntegral += ki * e;
                double correction = kp * e + ncoIntegral;

                // clamp correction to avoid symbol slips
                const double maxStep = 0.25; // max +/- step in *samples*
                if (correction > maxStep) correction = maxStep;
                if (correction < -maxStep) correction = -maxStep;

                advance = samplesPerSymbol + correction;
            }
            else
            {
                // first symbol: no timing error yet
                hasPrev = true;
                advance = samplesPerSymbol;
            }

            // output *one* symbol per timing update
            outputSamples.Add(currSample);

            // update previous symbol/decision
            prevSample = currSample;
            prevDecision = currDecision;

            // advance timing: time = baseIndex + mu + advance
            double newTime = baseIndex + mu + advance;
            baseIndex = (int)Math.Floor(newTime);
            mu = newTime - baseIndex;

            // If next baseIndex is already too close to the end,
            // stop and wait for more input next call.
            if (baseIndex + 1 >= buffer.Count)
                break;
        }

        // Drop consumed samples from the buffer, keep last few for next interpolation
        int consumed = Math.Min(baseIndex, Math.Max(0, buffer.Count - 2));
        if (consumed > 0)
        {
            buffer.RemoveRange(0, consumed);
            baseIndex -= consumed;   // renormalize index into the shortened buffer
        }

        return outputSamples;
    }

    private static Complex Lerp(List<Complex> x, int n, double mu)
    {
        Complex x0 = x[n];
        Complex x1 = x[n + 1];
        double a = 1.0 - mu;
        return new Complex(
            a * x0.Real + mu * x1.Real,
            a * x0.Imaginary + mu * x1.Imaginary
        );
    }
}