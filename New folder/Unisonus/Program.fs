namespace Unisonus

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open DSharpPlus

module Program =
    open YoutubeExtractor

    let mutable private client : DiscordClient = null
    let mutable private player = Player(null)
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

    let handlePlayCmd (msg : MessageCreateEventArgs) =
        let isInVc =
            msg.Guild.VoiceStates
            |> Seq.exists (fun vs -> vs.UserID = msg.Author.ID)
        match isInVc with
        | false -> msg.Channel.SendMessage(notInVCExTxt) |> ignore
        | _ ->
            if player.IsPaused then
                player.Resume()
            else
                player.Play msg
                msg.Channel.SendMessage("Song added to the queue.") |> ignore

    let private handleCommand (msg : MessageCreateEventArgs) =
        let cmdTxt = msg.Message.Content.Remove(0, 1).ToUpperInvariant()
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
        let config =
            let c = new DiscordConfig()
            c.AutoReconnect <- true
            c.DiscordBranch <- Branch.Stable
            c.LargeThreshold <- 250
            c.Token <- Config.BotToken
            c.TokenType <- TokenType.Bot
            c.UseInternalLogHandler <- false
            c
        client <- new DiscordClient(config)
        player <- Player(client)
        client.add_MessageCreated (fun (e : MessageCreateEventArgs) ->
            match e with
            | _ when e.Message.Content.StartsWith(">") -> handleCommand e
            | _ -> ()
            Task.Delay 0
        )
        client.Connect()
        |> Async.AwaitTask
        |> Async.RunSynchronously
        client.UpdateStatus("idle")
        |> ignore
        while true do
            Thread.Sleep 1000
        0
