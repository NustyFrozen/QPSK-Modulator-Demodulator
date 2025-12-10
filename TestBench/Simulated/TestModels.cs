using Modulation_Simulation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Modulation_Simulation.Models;
namespace TestBench.Simulated
{
    public static class TestModels
    {
        public static void testModels()
        {
            int sampleRate = 10_000_000, SymbolRate = sampleRate / 32;
            const int samplesPerFrame = 4096;     // must match "items per message" in GRC
            const int floatsPerSample = 2;        // I and Q
            const int bytesPerFloat = 4;
            const float RRCAlpha = .4f;
            QPSKModulator mod = new QPSKModulator(sampleRate, SymbolRate, RRCAlpha);
            mod.getCoeef().Select(x => new Complex(x, 0)).ToArray().SaveAsCs16("RRC Tx.cs16");

            var signal = mod.Modulate("101101100010");
            foreach (var sample in signal)
            {
                if (sample.Imaginary >= 1 || sample.Real >= 1)
                {
                    Console.WriteLine($"out of constellation bounds: {sample }");
                }
            }
            signal.SaveAsCs16("tx.cs16");
        }
    }
}
