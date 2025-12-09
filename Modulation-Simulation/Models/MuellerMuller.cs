using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
public class MuellerMuller
{
    private readonly double samplesPerSymbol;
    private readonly double kp;
    private readonly double ki;

    private double timeIndex;
    private double ncoIntegral;
    private Complex prevSample;
    private Complex prevDecision;
    private bool hasPrev;

    private readonly List<Complex> buffer = new List<Complex>();

    public MuellerMuller(double samplesPerSymbol, double kp, double ki)
    {
        this.samplesPerSymbol = samplesPerSymbol;
        this.kp = kp;
        this.ki = ki;
        this.timeIndex = 0.0;
        this.ncoIntegral = 0.0;
        this.hasPrev = false;
    }

    public List<Complex> Process(Complex[] incomingMfSamples)
    {
        // append new matched-filtered samples
        buffer.AddRange(incomingMfSamples);

        var outputSamples = new List<Complex>();

        int maxIndex = buffer.Count - 2;
        if (maxIndex < 0)
            return outputSamples;

        while (timeIndex <= maxIndex)
        {
            int n = (int)Math.Floor(timeIndex);
            double mu = timeIndex - n;

            Complex currSample = Lerp(buffer, n, mu);
            Complex currDecision = CostasLoopQpsk.GetSign(currSample);
            
            if (hasPrev)
            {
                Complex term1 = Complex.Conjugate(prevDecision) * currSample;
                Complex term2 = Complex.Conjugate(currDecision) * prevSample;
                double e = (term1 - term2).Real;

                ncoIntegral += ki * e;
                double correction = kp * e + ncoIntegral;

                timeIndex += samplesPerSymbol + correction;
            }
            else
            {
                timeIndex += samplesPerSymbol;
                hasPrev = true;
            }

            prevSample = currSample;
            prevDecision = currDecision;

            if (currSample.MagnitudeSquared() > 0.5)
                outputSamples.Add(currSample);

            maxIndex = buffer.Count - 2;
        }

        // drop consumed input samples and renormalize timeIndex
        int consumed = (int)Math.Floor(timeIndex) - 1;
        if (consumed > 0 && consumed <= buffer.Count)
        {
            buffer.RemoveRange(0, consumed);
            timeIndex -= consumed;
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


