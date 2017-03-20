using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.VoiceNext;
using YoutubeExtractor;

namespace UnisonusSharp
{
    class Player
    {
        private class SongQueueItem
        {
            public MessageCreateEventArgs Msg { get; set; }
            public VideoInfo VidInfo { get; set; }
            public string Filename { get; set; }
        }

        private readonly DiscordClient client;
        private ProcessStartInfo ffmpegPsi = new ProcessStartInfo
        {
            FileName = Config.Ffmpeg,
            Arguments = "-i $file$ -f s16le -ar 48000 -ac 2 pipe:1",
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        private List<SongQueueItem> fetchedSongs = new List<SongQueueItem>();
        private List<MessageCreateEventArgs> songFetcherQueue = new List<MessageCreateEventArgs>();
        private static bool isActive = false;
        private static bool stop = false;
        private static bool clear = false;
        private static int frameSizeMs = 60;
        private static int targetBufferSecs = 5;
        private static int targetBufferFrames = (1000 / frameSizeMs) * targetBufferSecs;
        private static int avgBytesPerSec = 48000 * 2 * 2;
        private static int frameSize = avgBytesPerSec / 1000 * frameSizeMs;

        public bool IsPaused { get; private set; } = stop;



        public Player(DiscordClient client)
        {
            this.client = client;
            Task.Run(() => SongFetcherQueueLoop());
            Task.Run(() => SongPlayerQueueLoop());
        }



        public void Play(MessageCreateEventArgs cmdMsg) => songFetcherQueue.Add(cmdMsg);

        public void Resume() => stop = false;

        public void Stop() => stop = true;

        public void RemoveSong(string song)
        {
            if (song.ToUpperInvariant() == "ALL")
            {
                songFetcherQueue.Clear();
                clear = true;

                while (isActive)
                {
                    Thread.Sleep(100);
                }

                foreach (var item in fetchedSongs)
                {
                    File.Delete(item.Filename);
                }

                fetchedSongs.Clear();
            }
        }

        private void SongFetcherQueueLoop()
        {
            while (true)
            {
                Thread.Sleep(100);

                if (songFetcherQueue.Count > 0 && fetchedSongs.Count < 3)
                {
                    var msg = songFetcherQueue[0];
                    songFetcherQueue.RemoveAt(0);
                    var kv = FetchSong(msg);
                    fetchedSongs.Add(new SongQueueItem
                    {
                        Msg = msg,
                        VidInfo = kv.Key,
                        Filename = kv.Value
                    });
                }
            }
        }

        private void SongPlayerQueueLoop()
        {
            while (true)
            {
                Thread.Sleep(100);

                if (fetchedSongs.Count > 0)
                {
                    var song = fetchedSongs[0];
                    fetchedSongs.RemoveAt(0);
                    client.UpdateStatus(song.VidInfo.Title).Wait();
                    song.Msg.Channel.SendMessage("Now playing " + song.VidInfo.Title).Wait();
                    PlaySong(song.Msg, song.Filename);
                    client.UpdateStatus("idle").Wait();
                    File.Delete(song.Filename);
                }
            }
        }

        private KeyValuePair<VideoInfo, string> FetchSong(MessageCreateEventArgs msg)
        {
            var link = msg.Message.Content.Remove(0, 5).Trim();
            var vidInfo = DownloadUrlResolver.GetDownloadUrls(link)
                .Where(x => x.VideoType == VideoType.Mp4 && x.AudioBitrate != 0)
                .OrderByDescending(x => x.AudioBitrate)
                .First();
            if (vidInfo.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(vidInfo);
            }
            var filename = Guid.NewGuid().ToString();
            var videoDownloader = new VideoDownloader(vidInfo, filename);
            videoDownloader.Execute();
            return new KeyValuePair<VideoInfo, string>(vidInfo, filename);
        }

        private void PlaySong(MessageCreateEventArgs cmdMsg, string fileGuid)
        {
            isActive = true;

            var vState = cmdMsg.Guild.VoiceStates.Where(vs => vs.UserID == cmdMsg.Author.ID).First();
            var vChannel = client.GetChannel(vState.ChannelID.Value).Result;
            var vnCfg = new VoiceNextConfiguration
            {
                VoiceApplication = DSharpPlus.VoiceNext.Codec.VoiceApplication.LowLatency
            };
            var vClient = client.UseVoiceNext(vnCfg);
            var vConnection = vClient.ConnectAsync(vChannel).Result;

            var bufferedFrames = new Queue<byte[]>();
            var fileFullyRead = false;
            var ffmpegMre = new ManualResetEvent(false);

            Task.Run(() =>
            {
                ffmpegPsi.Arguments = ffmpegPsi.Arguments.Replace("$file$", fileGuid);
                var ffmpegProc = Process.Start(ffmpegPsi);

                while (!fileFullyRead && !clear)
                {
                    while (bufferedFrames.Count > targetBufferFrames && !clear)
                    {
                        Thread.Sleep(5);
                    }
                    var b = new byte[frameSize];
                    var byteCount = ffmpegProc.StandardOutput.BaseStream.Read(b, 0, b.Length);
                    if (byteCount == 0)
                    {
                        fileFullyRead = true;
                    }
                    bufferedFrames.Enqueue(b);
                    Thread.Sleep(15);
                }
                ffmpegProc.Close();
                ffmpegMre.Set();
            });

            while (bufferedFrames.Count < targetBufferFrames)
            {
                Thread.Sleep(100);
            }

            try
            {
                vConnection.SendSpeakingAsync(true).Wait();
                while (bufferedFrames.Count > 0 && !clear)
                {
                    var b = bufferedFrames.Dequeue();
                    vConnection.SendAsync(b, frameSizeMs).Wait();
                    if (stop)
                    {
                        vConnection.SendAsync(new byte[frameSize], frameSizeMs).Wait();

                        while (stop && !clear)
                        {
                            Thread.Sleep(250);
                        }
                    }
                }
                vConnection.SendSpeakingAsync(false).Wait();
            }
            catch (Exception ex)
            {
                File.AppendAllText("log.txt", "\n\n\n" + ex.ToString());
            }

            ffmpegMre.WaitOne();
            isActive = false;
            clear = false;
            stop = false;
        }
    }
}
