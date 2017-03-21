using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using YoutubeExtractor;

namespace Unisonus
{
    class Player
    {
        private class SongQueueItem
        {
            public MessageEventArgs Msg { get; set; }
            public VideoInfo VidInfo { get; set; }
            //public string Filename { get; set; }
        }

        private readonly string vidDir = Path.Combine(".", "vids");
        private readonly DiscordClient client;
        private ProcessStartInfo ffmpegPsi = new ProcessStartInfo
        {
            FileName = Config.Ffmpeg,
            Arguments = "-i $path$ -f s16le -ar 48000 -ac 2 pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        private List<SongQueueItem> fetchedSongs = new List<SongQueueItem>();
        private List<MessageEventArgs> songFetcherQueue = new List<MessageEventArgs>();
        private static bool isActive = false;
        private static bool stop = false;
        private static bool clear = false;
        private static int framesPerSec = 50;
        private static int targetBufferSecs = 5;
        private static int targetBufferFrames = framesPerSec * targetBufferSecs;
        private static int avgBytesPerSec = 48000 * 2 * 2;
        private static int frameSize = avgBytesPerSec / framesPerSec;

        public bool IsPaused => isActive && stop;



        public Player(DiscordClient client)
        {
            if (!Directory.Exists(vidDir))
            {
                Directory.CreateDirectory(vidDir);
            }

            this.client = client;
            Task.Run(() => SongFetcherQueueLoop());
            Task.Run(() => SongPlayerQueueLoop());
        }



        public void Play(MessageEventArgs cmdMsg) => songFetcherQueue.Add(cmdMsg);

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

                //foreach (var item in fetchedSongs)
                //{
                //    File.Delete(item.Filename);
                //}

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
                    MessageEventArgs msg = null;

                    try
                    {
                        msg = songFetcherQueue[0];
                        songFetcherQueue.RemoveAt(0);
                        var kv = FetchSong(msg);
                        fetchedSongs.Add(new SongQueueItem
                        {
                            Msg = msg,
                            VidInfo = kv/*kv.Key,*/
                            //Filename = kv.Value
                        });
                    }
                    catch (Exception)
                    {
                        try
                        {
                            msg.Channel.SendMessage($"Sorry, I can't play `{msg.Message.Text.Remove(0, 5).Trim()}`");
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
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
                    SongQueueItem song = null;

                    try
                    {
                        song = fetchedSongs[0];
                        fetchedSongs.RemoveAt(0);
                        client.SetGame(new Game(song.VidInfo.Title));
                        song.Msg.Channel.SendMessage("Now playing " + song.VidInfo.Title).Wait();
                        PlaySong(song.Msg, song.VidInfo/*, song.Filename*/).Wait();
                        client.SetGame(new Game("idle"));

                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            if (song == null) throw;

                            var innerMostEx = ex;

                            while (innerMostEx.InnerException != null)
                            {
                                innerMostEx = innerMostEx.InnerException;
                            }

                            song.Msg.Channel.SendMessage($"Sorry, I encountered an error while playing {song.VidInfo.Title}: " + innerMostEx.Message);
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        private /*KeyValuePair<*/VideoInfo/*, string>*/ FetchSong(MessageEventArgs msg)
        {
            var link = msg.Message.Text.Remove(0, 5).Trim();
            var vidInfo = DownloadUrlResolver.GetDownloadUrls(link)
                .Where(x => x.VideoType == VideoType.WebM && x.AudioBitrate != 0)
                .OrderByDescending(x => x.AudioBitrate)
                .First();
            if (vidInfo.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(vidInfo);
            }
            //var filename = Path.Combine(vidDir, Guid.NewGuid().ToString());
            //var videoDownloader = new VideoDownloader(vidInfo, filename);
            //videoDownloader.Execute();
            return /*new KeyValuePair<VideoInfo, string>(*/vidInfo;/*, filename);*/
        }

        private async Task PlaySong(MessageEventArgs cmdMsg, VideoInfo vidInfo/*, string filepath*/)
        {
            isActive = true;
            stop = false;
            clear = false;
            
            var audioService = client.GetService<AudioService>();
            var audioClient = await audioService.Join(cmdMsg.User.VoiceChannel);

            var bufferedFrames = new Queue<byte[]>();
            var fileFullyRead = false;
            var ffmpegMre = new ManualResetEvent(false);

#pragma warning disable CS4014
            Task.Run(() =>
            {
                ffmpegPsi.Arguments = ffmpegPsi.Arguments.Replace("$path$", vidInfo.DownloadUrl);
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

                ffmpegProc.CloseMainWindow();
                ffmpegMre.Set();
            });
#pragma warning restore CS4014

            while (bufferedFrames.Count < targetBufferFrames && !clear)
            {
                Thread.Sleep(100);
            }

            while (bufferedFrames.Count > 0 && !clear)
            {
                var b = bufferedFrames.Dequeue();
                audioClient.Send(b, 0, b.Length);

                if (stop)
                {
                    b = new byte[frameSize];
                    audioClient.Send(b, 0, b.Length);

                    while (stop && !clear)
                    {
                        Thread.Sleep(250);
                    }
                }
            }

            ffmpegMre.WaitOne();
            isActive = false;
            clear = false;
            stop = false;
        }
    }
}
