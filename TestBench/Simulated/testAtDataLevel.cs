using MathNet.Numerics;
using QPSK.Models;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using QPSK;
namespace TestBench.Simulated;
public static class testAtDataLevel
{
    public static void RunTest(PublisherSocket pub)
    {
        int sampleRate = 10_000_000, SymbolRate = sampleRate / 2;
        const float RRCAlpha = .4f;

        const string TSC =
            "11001010011101100100100110101100" +
            "01110100111001011010001101101001"; // even length

        QPSKModulator mod = new QPSKModulator(sampleRate, SymbolRate, RRCAlpha,10, tsc: TSC);
        QPSKDeModulator demod = new QPSKDeModulator(sampleRate, SymbolRate, RRCAlpha, 10, tsc: TSC);

        NCO transmitter_unstable_NCO = new NCO(100e6, sampleRate, 1);
        NCO receiver_unstable_NCO = new NCO(100e6, sampleRate, 1);

        var rand = new Random();

        while (true)
        {
            // payload only
            var data = "The Quick Brown fox jump yes yes man good!";

            var modulatedSignal = mod.ModulateTextUtf8(data,"MESSAGE_START", "MESSAGE_STOP").toComplex();

            modulatedSignal = modulatedSignal.Multiply(
                Enumerable.Range(0, modulatedSignal.Length)
                    .Select(_ => transmitter_unstable_NCO.NextSample() * receiver_unstable_NCO.NextSample().Conjugate())
                    .ToArray());

            var demodulatedData = demod.DeModulateTextUtf8(modulatedSignal.toFloatInterleaved(), "MESSAGE_START", "MESSAGE_STOP");

            bool passed = demodulatedData.Contains(data); // TSC already stripped by demod
            var results = $"{data} | {demodulatedData}";

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("EXPECTED | GOT (payload only; TSC baked in)");

            Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(results);

            Console.ReadKey();
            Console.Clear();
        }
    }
}
