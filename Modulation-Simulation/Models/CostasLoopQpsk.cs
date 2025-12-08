using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;

/// <summary>
/// 
/// </summary>
/// <param name="sampleRate">sample rate</param>
/// <param name="loopBandwith">cutoff LPF of the error</param>
/// <param name="FIRTaps">how strong you want the smoothing Small smoothing 11-31, strong 51-201  must be odd number</param>
    public class CostasLoopQpsk
{
    float sampleRate, cutoff;
    int FIRTaps;
    RealFIRFilter filter;


    //nco
    double theta;        // NCO phase
    double loopOut;      // filtered error (real)
    double loopGain;
    public CostasLoopQpsk(float sampleRate, float cutoff, int FIRTaps,float loopGain = 1.0f)
    {
        this.sampleRate = sampleRate;
        this.cutoff = cutoff;
        this.FIRTaps = FIRTaps;
        this.loopGain = loopGain;
        filter = new RealFIRFilter(generateLPFCoeff());
    }
    //thresholder
    Complex getSign(Complex sample) => new Complex(sample.Real > 0 ? 1.0f : -1.0f, sample.Imaginary > 0 ? 1.0f : -1.0f);
    public Complex process(Complex sample)
    {
        var nco = Complex.FromPolarCoordinates(1.0, -theta);
        var mixed = sample * nco;

        var est = getSign(mixed);
        double phaseError = est.Real * mixed.Imaginary - est.Imaginary * mixed.Real;

        loopOut = filter.Filter(phaseError); // real filter
        theta += loopGain * loopOut;                // K = loop gain
        return mixed;
    }
    double[] generateLPFCoeff()
    {
        double[] Hideal = new double[FIRTaps]; //ideal sinc function
        double Wc = cutoff / sampleRate;
        int M = (FIRTaps - 1) / 2;
        for (int i = 0; i < FIRTaps; i++)
        {
            if(M == i)
            {
                Hideal[i] = 2 * Wc;
            } else
            {
                Hideal[i] = Math.Sin(2 * Math.PI * Wc*(i - M)) / (Math.PI * (i - M));
            }
        }
        var hamming = Window.Hamming(FIRTaps);
        var windowed = Enumerable.Range(0, FIRTaps)
                                 .Select(x => Hideal[x] * hamming[x])
                                 .ToArray();

        var sum = windowed.Sum(); // DC gain normalization

        return windowed
            .Select(v =>(v / sum))
            .ToArray();
    }
}
