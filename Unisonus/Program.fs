namespace Unisonus

open System
open System.Collections.Generic
open System.Threading
open System.Diagnostics
open Discord
open Discord.Audio

module Program =
    open YoutubeExtractor

    let private client = new DiscordClient()
    let private player = Player(client)
    let private notInVCExTxt = "You need to be in a voice channel for me to play music!"
    let private helpCmdTxt = """
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
"""

    let handlePlayCmd (msg : MessageEventArgs) =
        match msg.User.VoiceChannel with
        | null -> msg.Channel.SendMessage(notInVCExTxt) |> ignore
        | _ ->
            if player.IsPaused then
                player.Resume()
            else
                player.Play msg
                msg.Channel.SendMessage("Song added to the queue.") |> ignore

    let private handleCommand (msg : MessageEventArgs) =
        let cmdTxt = msg.Message.Text.Remove(0, 1).ToUpperInvariant()
        match cmdTxt with
        | _ when cmdTxt.StartsWith("HELP") -> 
            msg.Channel.SendMessage(helpCmdTxt.Trim()) |> ignore
        | _ when cmdTxt.StartsWith("PLAY") -> handlePlayCmd msg
        | _ when cmdTxt.StartsWith("STOP") -> player.Stop()
        | _ when cmdTxt.StartsWith("NOWPLAYING") -> ()
        | _ when cmdTxt.StartsWith("LIST") -> ()
        | _ when cmdTxt.StartsWith("REMOVE") -> player.RemoveSong <| cmdTxt.Remove(0, 6).Trim()
        | _ when cmdTxt.StartsWith("REPEAT") -> ()
        | _ when cmdTxt.StartsWith("SHUFFLE") -> ()
        | _ -> ()

    [<EntryPoint>]
    let main argv =
        client.MessageReceived.Add (fun (e : MessageEventArgs) ->
            match e with
            | _ when e.Message.Text.StartsWith(">") -> handleCommand e
            | _ when e.Message.Text = ">VC_TEST" ->
                try
                    let audioService = client.GetService<AudioService>()
                    let ac = audioService.Join(e.User.VoiceChannel) |> Async.AwaitTask |> Async.RunSynchronously
                    e.Channel.SendMessage("Success!") |> ignore
                with
                | _ as ex -> Console.WriteLine ex
            | _ -> ()
        )
        client.UsingAudio(fun x -> x.Mode <- AudioMode.Outgoing) |> ignore
        client.Connect(Config.BotToken, TokenType.Bot)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        client.SetGame(Game("idle"))
        while true do
            Thread.Sleep 1000
        0
