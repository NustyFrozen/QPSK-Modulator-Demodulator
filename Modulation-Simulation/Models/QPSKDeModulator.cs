using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
    public class QPSKDeModulator(int SampleRate, int SymbolRate,float RrcAlpha = 0.9f, float threshold = 1f, int rrcSpan = 6,bool differentialEncoding = true)
    {
    ComplexFIRFilter rrc = new ComplexFIRFilter(RRCFilter.generateCoefficents(rrcSpan, RrcAlpha, SampleRate, SymbolRate).Select(x => new Complex(x, 0)).ToArray());
    MuellerMuller symbolSync = new MuellerMuller(SampleRate / SymbolRate, 0.0097,                    // Kp (was 0.013)
   .00001);
    CostasLoopQpsk costas = new CostasLoopQpsk(
    SymbolRate,          // this is the real fs for costas.Process(...)
    SymbolRate / 100.0   // BW ≈ 0.01 * Rs (keep or tweak as you like)
);

    Dictionary<Complex, string> symbolMapping = new()
    {
        [new Complex(-1, -1)] = "00",
        [new Complex(-1, 1)] = "01",
        [new Complex(1, 1)] = "11",
        [new Complex(1, -1)] = "10"
    };
    public string DeModulate(Complex[] Samples)
    {
        var processedSignal = symbolSync.Process(rrc.Filter(Samples));
        var bits = new System.Text.StringBuilder(processedSignal.Count * 2);

        foreach (var sample in processedSignal)
        {
            var rotated = costas.Process(sample);
            var decision = CostasLoopQpsk.GetSign(rotated);

            // reliability metric: distance from decided corner
            Complex diff = rotated - decision;
            double distance2 = diff.MagnitudeSquared();
            bool isReliable = distance2 < threshold;   // tweak this threshold
            if (!isReliable)
                continue;
            bits.Append(symbolMapping[decision]);
        }

        return bits.ToString();
    }

}

