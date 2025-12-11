using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Modulation_Simulation.Models;
    public class QPSKDeModulator(int SampleRate, int SymbolRate,float RrcAlpha = 0.9f, float threshold = 1f, int rrcSpan = 6,float magnitudeScaling = 3.0f,double SymbolSyncBandwith= 0.00004, bool differentialEncoding = true)
    {
    ComplexFIRFilter rrc = new ComplexFIRFilter(RRCFilter.generateCoefficents(rrcSpan, RrcAlpha, SampleRate, SymbolRate).Select(x => new Complex(x, 0)).ToArray());
    FLLBandEdgeFilter fll = new FLLBandEdgeFilter(SampleRate / SymbolRate, RrcAlpha, 40, 0.000000001f);
    // i need to fix the fll, if you have static offset just subtract it at the receiver's lo
    MuellerMuller symbolSync = new MuellerMuller(SampleRate / SymbolRate,
       (1 / Math.Sqrt(2.0))* 4.0 * Math.PI * SymbolSyncBandwith,                  
  Math.Pow(2.0 * Math.PI * SymbolSyncBandwith,2));
    CostasLoopQpsk costas = new CostasLoopQpsk(
    SymbolRate,         
    SymbolRate / 100  
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
       // Samples = fll.Process(Samples);
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
    public Complex[] deModulateConstellation(Complex[] Samples)
    {

        //Samples = fll.Process(Samples);
        Samples = Samples.Select(x=>x * magnitudeScaling).ToArray();
        Samples = symbolSync.Process(rrc.Filter(Samples)).ToArray();
        return Samples.Select(x => costas.Process(x)).ToArray();
    }

}

