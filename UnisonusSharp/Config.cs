using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnisonusSharp
{
    public static class Config
    {
        private static string[] fileData = File.ReadAllLines("config.txt");

        public static string Ffmpeg = fileData[0].Remove(0, 11).Trim();

        public static string BotToken = fileData[1].Remove(0, 9).Trim();
    }
}
