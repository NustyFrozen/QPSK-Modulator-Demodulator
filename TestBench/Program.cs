using Modulation_Simulation.Models;
using NetMQ;
using NetMQ.Sockets;
using System;
using System.Diagnostics;
using System.Numerics;
using TestBench;


const string address = "tcp://*:5555";
using (var pub = new PublisherSocket())
{
    pub.Bind(address);
    Thread.Sleep(500);
   // TestModels.testModels();
    testFullDemodChain.RunTest(pub);
   // testAtDataLevel.RunTest(pub);
}
