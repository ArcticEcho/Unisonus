using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Discord;
using Discord.Audio;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Test
{
    class Program
    {

        static void Main(string[] args)
        {
            var client = new DiscordClient();
            client.MessageReceived += async (o, e) =>
            {
                if (e.Message.Text == ">VC_TEST" && e.User.VoiceChannel != null)
                {
                    var ffmpegPsi = new ProcessStartInfo
                    {
                        FileName = Config.Ffmpeg,
                        Arguments = "-i song.mp4 -f s16le -ar 48000 -ac 2 pipe:1",
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    var framesPerSec = 50;
                    var targetBufferSecs = 5;
                    var targetBufferFrames = framesPerSec * targetBufferSecs;
                    var avgBytesPerSec = 48000 * 2 * 2;
                    var frameSize = avgBytesPerSec / framesPerSec;
                    var audioService = client.GetService<AudioService>();
                    Console.WriteLine(1);
                    var audioClient = await audioService.Join(e.User.VoiceChannel);
                    Console.WriteLine(2);

                    var bufferedFrames = new Queue<byte[]>();
                    var fileFullyRead = false;
                    var ffmpegMre = new ManualResetEvent(false);
                    Task.Run(() =>
                    {
                        try
                        {
                            var ffmpegProc = Process.Start(ffmpegPsi);
                            while (!fileFullyRead)
                            {
                                while (bufferedFrames.Count > targetBufferFrames)
                                {
                                    Thread.Sleep(5);
                                }
                                var b = new byte[frameSize];
                                var byteCount = ffmpegProc.StandardOutput.BaseStream.Read(b, 0, b.Length);
                                if (byteCount == 0)
                                    fileFullyRead = true;
                                bufferedFrames.Enqueue(b);
                                Thread.Sleep(15);
                            }
                            ffmpegProc.Close();
                            ffmpegMre.Set();
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.AppendAllText("log", ex.ToString());
                        }
                    });
                    while (bufferedFrames.Count < targetBufferFrames)
                    {
                        Thread.Sleep(100);
                    }
                    System.IO.File.WriteAllText("log", "3");
                    while (bufferedFrames.Count > 0)
                    {
                        try
                        {
                            var b = bufferedFrames.Dequeue();
                            audioClient.Send(b, 0, b.Length);
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.AppendAllText("log", ex.ToString());
                        }
                    }
                }
            };
            client.UsingAudio(x => x.Mode = AudioMode.Outgoing);
            client.Connect(Config.BotToken, TokenType.Bot).Wait();
            while (true)
            {
                Thread.Sleep(1000);
            }
        }
    }
}
