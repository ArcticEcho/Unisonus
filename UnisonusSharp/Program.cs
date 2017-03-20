using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;

namespace UnisonusSharp
{
    public class Program
    {
        private static DiscordClient client;
        private static Player player;
        private static string notInVCExTxt = "You need to be in a voice channel for me to play music!";
        private static string helpCmdTxt = @"
**play**              - plays a new song/adds a song to the queue/resumes playing if stopped.
**stop**              - stops playing music.
**skip**               - skips the current song.
**nowplaying** - posts info on the current song.
**list**                 - lists the next 10 songs in the queue.
**shuffle**          - randomises the song queue order.
**remove** <*all*|*song name*>
    - *all*: removes all songs in the queue.
    - *song name*: removes the song whose title matchs the inputted text.
**repeat** <*all*|*one*>
    - *all*: repeats the entire queue.
    - *one*: repeats the current song.
";

        public static void Main(string[] args)
        {
            var config = new DiscordConfig
            {
                AutoReconnect = true,
                DiscordBranch = Branch.Stable,
                LargeThreshold = 250,
                Token = Config.BotToken,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = false
            };

            client = new DiscordClient(config);
            player = new Player(client);

            client.MessageCreated += async e =>
            {
                if (e.Message.Content.StartsWith(">"))
                {
                    HandleCommand(e);
                }
                await Task.Delay(0);
            };
            client.Connect().Wait();
            client.UpdateStatus("idle").Wait();

            while (true)
            {
                Thread.Sleep(1000);
            }
        }

        private static void HandleCommand(MessageCreateEventArgs msg)
        {
            var cmdTxt = msg.Message.Content.Remove(0, 1).ToUpperInvariant();

            if (cmdTxt == "HELP")
            {
                msg.Channel.SendMessage(helpCmdTxt.Trim()).Wait();
            }
            else if (cmdTxt.StartsWith("PLAY"))
            {
                HandlePlayCmd(msg);
            }
            else if (cmdTxt.StartsWith("STOP"))
            {
                player.Stop();
            }
            else if (cmdTxt.StartsWith("REMOVE"))
            {
                var song = cmdTxt.Remove(0, 6).Trim();
                player.RemoveSong(song);
            }
        }

        private static void HandlePlayCmd(MessageCreateEventArgs msg)
        {
            var isInVc = msg.Guild.VoiceStates.Exists(vs => vs.UserID == msg.Author.ID);

            if (!isInVc)
            {
                msg.Channel.SendMessage(notInVCExTxt).Wait();
                return;
            }

            if (player.IsPaused)
            {
                player.Resume();
            }
            else
            {
                player.Play(msg);
                msg.Channel.SendMessage("Song added to the queue.").Wait();
            }
        }
    }
}
