using MathNet.Numerics;
using Modulation_Simulation.Models;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TestBench;
public static class testAtDataLevel
{
    public static void RunTest(PublisherSocket pub)
    {
        int sampleRate = 10_000_000, SymbolRate = sampleRate / 20;
        const int samplesPerFrame = 4096;     // must match "items per message" in GRC
        const int floatsPerSample = 2;        // I and Q
        const int bytesPerFloat = 4;
        const float RRCAlpha = .9f;
        QPSKModulator mod = new QPSKModulator(sampleRate, SymbolRate, RRCAlpha);
        QPSKDeModulator demod= new QPSKDeModulator(sampleRate, SymbolRate, RRCAlpha);
        NCO transmitter_unstable_NCO = new NCO(100e6, sampleRate, 1);
        NCO receiver_unstable_NCO = new NCO(100e6, sampleRate, 1);
        var rand = new Random();
        
        var noise = NoiseGenerator.GenerateIqNoise(-90, samplesPerFrame * floatsPerSample);
       
        while (true)
        {
            var data = String.Empty;
            for (int i = 0; i < 32; i++)
                data += rand.Next(0, 2).ToString(); //01100011...
            var modulatedSignal = mod.Modulate(data);

            //simulating over air and back transmission of real life LO
            modulatedSignal = modulatedSignal.Multiply(Enumerable.Range(0, modulatedSignal.Length)
                .Select(x => transmitter_unstable_NCO.NextSample() * receiver_unstable_NCO.NextSample().Conjugate()).ToArray()); //upsample and downsample mixing
            var demodulatedData = demod.DeModulate(modulatedSignal);
            bool passed = data == demodulatedData;
            var results = $"{data} | {demodulatedData}";

            Enumerable.Range(0, Math.Max(0, results.Length / 2 - "EXPECTED | GOT".Length / 2 -2)).All(x =>
            {
                Console.Write(" ");
                return true;
            });
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("EXPECTED | GOT (PRESS a key to re-run test)");
            Console.ForegroundColor = passed ? ConsoleColor.Green:ConsoleColor.Red;
            Console.WriteLine(results);
            
            Console.ReadKey();
            Console.Clear();
        }
    }
    }