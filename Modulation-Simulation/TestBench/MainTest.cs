using System;
using System.Linq;
using System.Numerics;
using Modulation_Simulation.Models;

namespace Modulation_Simulation.TestBench;

public class MainTest
{
    public static void RunTests()
    {

        testBaseBandOnly();
        return;
        int sampleRate = 1_000_000, SymbolRate = 512, ppm = 2, fc = 10_000;
        var LO = new LocalOscillator(fc,sampleRate, ppm);
        var LOBuffer = new Complex[5_000_000] ;
        LO.GenerateBlock(LOBuffer,0, 5_000_000);
        LOBuffer.SaveAsCs16($"Oscilator_RATE-{sampleRate}_DRIFT-{ppm}_CARRIER-{fc}.cs16");

        int rrcSpan = 32;
        double rrcBeta = 0.9;
        RRCFilter.generateCoefficents(rrcSpan, rrcBeta, sampleRate, SymbolRate)
            .Select(x=>new Complex(x,0)).ToArray().SaveAsCs16($"RRC_SPAN-{rrcSpan}_Beta-{rrcBeta}.cs16");
        int fllSpan = 33;
        

        QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate);
        var DATA = "0110100001100101011011000110110001101111001000000111011101101111011100100110110001100100";//"hello world"
        var modulatedSignal = modulator.Modulate(DATA);
        modulatedSignal.SaveAsCs16($"QPSKModulated_SAMPLERATE-{sampleRate}_SYMBOLRATE-{SymbolRate}.cs16");

        Complex[] signalOverAir = modulatedSignal.Multiply(LOBuffer); //what goes over the air carrier wave + modulation
        signalOverAir.SaveAsCs16($"QPSKOverAir_SAMPLERATE-{sampleRate}_SYMBOLRATE-{SymbolRate}_CARRIER-{fc}_DRIFT-{ppm}.cs16");

        
    }
    public static void testBaseBandOnly()
    {
        int sampleRate = 1_000_000, SymbolRate = 512;
        QPSKModulator modulator = new QPSKModulator(sampleRate, SymbolRate);
        var DATA = "0110100001100101011011000110110001101111001000000111011101101111011100100110110001100100";//"hello world"
        Console.WriteLine($"Data: {DATA}");
        var modulatedSignal = modulator.Modulate(DATA);
        modulatedSignal.SaveAsCs16($"QPSKModulated_SAMPLERATE-{sampleRate}_SYMBOLRATE-{SymbolRate}.cs16");
        QPSKModulator demodulator = new QPSKModulator(sampleRate, SymbolRate);
        demodulator.deModulate(modulatedSignal);
    }
}