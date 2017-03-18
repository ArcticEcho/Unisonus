open System
open System.Collections.Generic
open System.Threading
open System.Diagnostics
open Discord
open Discord.Audio

module Program =
    let private client = new DiscordClient()
    let private ffmpegPsi =
        let psi = new ProcessStartInfo()
        psi.FileName <- """C:\Program Files\ffmpeg\bin\ffmpeg.exe"""
        psi.Arguments <- "-i song.mp3 -f s16le -ar 48000 -ac 2 pipe:1"
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi
    let mutable private currentVC : Channel = null
    let mutable private currentAC : IAudioClient = null
    let private notInVCExTxt = "You need to be in a voice channel for me to play music!"
    let private helpCmdTxt = """
**play**              - plays a new song/adds a song to the queue/resumes playing if stopped.
**stop**              - stops playing music.
**nowplaying** - posts info on the currently playing song.
**list**                 - lists the next 10 songs in the queue.
**shuffle**          - randomises the song queue order.
**remove** <*all*|*song name*>
    - *all*: removes all songs in the queue.
    - *song name*: removes the song whose title matchs the inputted text.
**repeat** <*all*|*one*>
    - *all*: repeats the entire queue.
    - *one*: repeats the currently playing song."""

    let handlePlayCmd (msg : MessageEventArgs) =
        match msg.User.VoiceChannel with
        | null -> msg.Channel.SendMessage(notInVCExTxt) |> ignore
        | _ ->
            async {
                currentVC <- msg.User.VoiceChannel
                let audioService = client.GetService<AudioService>()
                let! ac = audioService.Join(currentVC) |> Async.AwaitTask
                let ffmpegProc = Process.Start ffmpegPsi
                Thread.Sleep 1000
                let frameFrac = 50
                let targetBuffer = frameFrac * 5
                let avgBytesSec = 48000 * 2 * 2
                let frameSize = avgBytesSec / frameFrac
                let bufferedFrames = Queue<byte[]>()
                let mutable fileFullyRead = false
                async {
                    while bufferedFrames.Count < targetBuffer / 5 * 2 do
                        let b = Array.zeroCreate<byte>(frameSize)
                        bufferedFrames.Enqueue b
                    while not fileFullyRead do
                        while bufferedFrames.Count > targetBuffer do
                            Thread.Sleep 5
                        let b = Array.zeroCreate<byte>(frameSize)
                        let byteCount = ffmpegProc.StandardOutput.BaseStream.Read(b, 0, b.Length)
                        if byteCount = 0 then
                            fileFullyRead <- true
                        bufferedFrames.Enqueue b
                        Thread.Sleep 15
                } |> Async.Start
                while bufferedFrames.Count < targetBuffer do
                    Thread.Sleep 100
                while bufferedFrames.Count > 0 do
                    let b =  bufferedFrames.Dequeue()
                    ac.Send(b, 0, b.Length)
                ac.Wait()
            } |> Async.Start
        ()

    let private handleCommand (msg : MessageEventArgs) =
        let cmdTxt = msg.Message.Text.Remove(0, 1).ToUpperInvariant()
        match cmdTxt with
        | _ when cmdTxt.StartsWith("HELP") -> 
            msg.Channel.SendMessage(helpCmdTxt.Trim()) |> ignore
        | _ when cmdTxt.StartsWith("PLAY") -> handlePlayCmd msg
        | _ when cmdTxt.StartsWith("STOP") -> ()
        | _ when cmdTxt.StartsWith("NOWPLAYING") -> ()
        | _ when cmdTxt.StartsWith("LIST") -> ()
        | _ when cmdTxt.StartsWith("REMOVE") -> ()
        | _ when cmdTxt.StartsWith("REPEAT") -> ()
        | _ when cmdTxt.StartsWith("SHUFFLE") -> ()
        | _ -> ()

    [<EntryPoint>]
    let main argv =
        client.MessageReceived.Add (fun (e : MessageEventArgs) ->
            match e with
            | _ when e.Message.Text.StartsWith(">") -> handleCommand e
            | _ -> ()
        )
        client.UsingAudio(fun x -> x.Mode <- AudioMode.Outgoing) |> ignore
        client.Connect("", TokenType.Bot)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        while true do
            Thread.Sleep 1000
        0
