﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using System.IO;

namespace MaxMind.DB.Test
{
    [TestFixture]
    public class ThreadingTest
    {
        [Test]
        public void TestParallelFor()
        {
            var reader = new Reader(Path.Combine("..", "..", "TestData", "GeoLite2-City.mmdb"), FileAccessMode.MemoryMapped);
            var count = 0;
            var ipsAndResults = new Dictionary<IPAddress, string>();
            var rand = new Random();
            while (count < 10000)
            {
                var ip = new IPAddress(rand.Next(int.MaxValue));
                var resp = reader.Find(ip);
                if (resp != null)
                {
                    ipsAndResults.Add(ip, resp.ToString());
                    count++;
                }
            }

            var ips = ipsAndResults.Keys.ToArray();
            var start = DateTime.Now;
            Parallel.For(0, ips.Length, i =>
            {
                var ipAddress = ips[i];
                var result = reader.Find(ipAddress);
                var resultString = result.ToString();
                var expectedString = ipsAndResults[ipAddress];
                if (resultString != expectedString)
                    throw new Exception(string.Format("Non-matching result. Expected {0}, found {1}", expectedString, resultString));
            });
            var stop = DateTime.Now;
            Console.WriteLine("Requests per second: " + count / (stop - start).TotalSeconds);
        }
    }
}