namespace Unisonus

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading
open DSharpPlus
open DSharpPlus.VoiceNext
open YoutubeExtractor

type Player (client : DiscordClient) =
    let ffmpegPsi =
        let psi = new ProcessStartInfo()
        psi.FileName <- Config.FfmpegPath
        psi.Arguments <- "-i $file$ -f s16le -ar 48000 -ac 2 pipe:1"
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi
    let songFetcherQueue = List<MessageCreateEventArgs>()
    let fetchedSongs = List<MessageCreateEventArgs * VideoInfo * string>()
    let frameSizeMs = 60
    let targetBufferSecs = 5
    let targetBufferFrames = (1000 / frameSizeMs) * targetBufferSecs
    let avgBytesPerSec = 48000 * 2 * 2
    let frameSize = avgBytesPerSec / 1000 * frameSizeMs
    let mutable isActive = false
    let mutable stop = false
    let mutable clear = false

    let fetchSong (cmdMsg : MessageCreateEventArgs) =
        let link = cmdMsg.Message.Content.Remove(0, 5).Trim()
        let vidInfo =
            DownloadUrlResolver.GetDownloadUrls(link)
            |> Seq.filter (fun v ->
                v.VideoType = VideoType.Mp4 && v.AudioBitrate <> 0
            )
            |> Array.ofSeq
            |> Array.sortByDescending (fun v -> 
                v.AudioBitrate
            )
            |> Array.head
        if vidInfo.RequiresDecryption then
            DownloadUrlResolver.DecryptDownloadUrl vidInfo
        let filename = Guid.NewGuid().ToString()
        let videoDownloader = new VideoDownloader(vidInfo, filename)
        videoDownloader.Execute()
        vidInfo, filename

    let playSong (cmdMsg : MessageCreateEventArgs) fileGuid = async {
        isActive <- true
        let vState = cmdMsg.Guild.VoiceStates |> Seq.filter (fun vs -> vs.UserID = cmdMsg.Author.ID) |> Seq.head
        let vChannel = client.GetChannel(vState.ChannelID.Value) |> Async.AwaitTask |> Async.RunSynchronously
        let vnCfg =
            let c = new VoiceNextConfiguration()
            c.VoiceApplication <- Codec.VoiceApplication.Music
            c
        let vClient = client.UseVoiceNext(vnCfg)
        let vConnection = vClient.ConnectAsync(vChannel) |> Async.AwaitTask |> Async.RunSynchronously
        let bufferedFrames = Queue<byte[]>()
        let mutable fileFullyRead = false
        let ffmpegMre = new ManualResetEvent false
        ffmpegPsi.Arguments <- ffmpegPsi.Arguments.Replace("$file$", fileGuid)
        let ffmpegProc = Process.Start ffmpegPsi
        async {
            while not fileFullyRead && not clear do
                while bufferedFrames.Count > targetBufferFrames && not clear do
                    Thread.Sleep 5
                let b = Array.zeroCreate<byte>(frameSize)
                let byteCount = ffmpegProc.StandardOutput.BaseStream.Read(b, 0, b.Length)
                if byteCount = 0 then
                    fileFullyRead <- true
                bufferedFrames.Enqueue b
                Thread.Sleep 15
            ffmpegProc.Close()
            ffmpegMre.Set() |> ignore
        } |> Async.Start
        while bufferedFrames.Count < targetBufferFrames do
            Thread.Sleep 100
        try
            vConnection.SendSpeakingAsync(true) |> Async.AwaitTask |> Async.RunSynchronously
            while bufferedFrames.Count > 0 && not clear do
                let b =  bufferedFrames.Dequeue()
                vConnection.SendAsync(b, frameSizeMs)
                |> Async.AwaitTask
                |> Async.RunSynchronously
                if stop then
                    vConnection.SendAsync(Array.zeroCreate<byte>(frameSize), frameSizeMs) |> Async.AwaitTask |> Async.RunSynchronously
                    while stop && not clear do
                        Thread.Sleep 250
            vConnection.SendSpeakingAsync(false) |> Async.AwaitTask |> Async.RunSynchronously
        with
        | _ as ex ->
            File.AppendAllText("log.txt", "\n\n\n" + ex.ToString())
        ffmpegMre.WaitOne() |> ignore
        isActive <- false
        clear <- false
        stop <- false
    }

    let songFetcherQueueLoop() = async {
        while true do
            Thread.Sleep 100
            if songFetcherQueue.Count > 0 && fetchedSongs.Count < 3 then
                let msg = songFetcherQueue.[0]
                songFetcherQueue.RemoveAt(0)
                let vidInfo, filename = fetchSong msg
                fetchedSongs.Add(msg, vidInfo, filename)
    }

    let songPlayerQueueLoop() = async {
        while true do
            Thread.Sleep 100
            if fetchedSongs.Count > 0 then
                let msg, vidInfo, filename = fetchedSongs.[0]
                fetchedSongs.RemoveAt 0
                client.UpdateStatus(vidInfo.Title) |> ignore
                msg.Channel.SendMessage("Now playing " + vidInfo.Title) |> ignore
                do! playSong msg filename
                client.UpdateStatus("idle") |> ignore
                File.Delete filename
    }

    do
        songFetcherQueueLoop() |> Async.Start
        songPlayerQueueLoop() |> Async.Start

    member this.IsPaused
        with get() = stop

    member this.Play (cmdMsg : MessageCreateEventArgs) = songFetcherQueue.Add cmdMsg

    member this.Resume() = stop <- false

    member this.Stop() = stop <- true

    member this.RemoveSong (opt : string) =
        match opt with
        | _ as o when opt.ToUpperInvariant() = "ALL" ->
            songFetcherQueue.Clear()
            clear <- true
            while isActive do
                Thread.Sleep 100
            fetchedSongs |> Seq.iter (fun (m, v, f) ->
                File.Delete f
            )
            fetchedSongs.Clear()
        | _ -> ()