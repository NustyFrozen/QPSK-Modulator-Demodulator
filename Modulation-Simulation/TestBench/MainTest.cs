using System.Linq;
using System.Numerics;
using Modulation_Simulation.Models;

namespace Modulation_Simulation.TestBench;

public class MainTest
{
    public static void RunTests()
    {


        int sampleRate = 1_000_000, SymbolRate = 512, DriftPerSec = 32, fc = 100_000_000;


        var LO = new LocalOscillator(fc,sampleRate,DriftPerSec);
        var LOBuffer = new Complex[16_000_000] ;
        LO.GenerateBlock(LOBuffer,0,16_000_000);
        LOBuffer.SaveAsCs16($"Oscilator_RATE-{sampleRate}_DRIFT-{DriftPerSec}_CARRIER-{fc}.cs16");

        int rrcSpan = 4;
        double rrcBeta = 0.7;
        RRCFilter.generateCoefficents(rrcSpan, 0.7, sampleRate, SymbolRate)
            .Select(x=>new Complex(x,0)).ToArray().SaveAsCs16($"RRC_SPAN-{rrcSpan}_Beta-{rrcBeta}.cs16");


        QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate);
        var DATA = "0110100001100101011011000110110001101111001000000111011101101111011100100110110001100100";//"hello world"
        var modulatedSignal = modulator.Modulate(DATA);
        modulatedSignal.SaveAsCs16($"QPSKModulated_SAMPLERATE-{sampleRate}_SYMBOLRATE-{SymbolRate}.cs16");

        
    }
}