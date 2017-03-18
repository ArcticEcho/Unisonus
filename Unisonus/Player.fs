namespace Unisonus

open System
open System.Collections.Generic
open System.Threading
open System.Diagnostics
open Discord
open Discord.Audio
open YoutubeExtractor

type Player (client : DiscordClient) =
    let ffmpegStreamPsi =
        let psi = new ProcessStartInfo()
        psi.FileName <- Config.FfmpegPath
        psi.Arguments <- "-i song.mp4 -f s16le -ar 48000 -ac 2 pipe:1"
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi
    let songQueue = Dictionary<MessageEventArgs, string>() 
    let framesPerSec = 50
    let targetBufferSecs = 5
    let targetBufferFrames = framesPerSec * targetBufferSecs
    let avgBytesPerSec = 48000 * 2 * 2
    let frameSize = avgBytesPerSec / framesPerSec
    let mutable currentVC : Channel = null
    let mutable currentAC : IAudioClient = null
    let mutable isActive = false
    let mutable stop = false
    let mutable clear = false

    let fetchSong (cmdMsg : MessageEventArgs) =
        let link = cmdMsg.Message.Text.Remove(0, 5).Trim()
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
        let videoDownloader = new VideoDownloader(vidInfo, "song.mp4")
        videoDownloader.Execute()
        vidInfo.Title


    let playSong (cmdMsg : MessageEventArgs) = async {
        isActive <- true
        currentVC <- cmdMsg.User.VoiceChannel
        let audioService = client.GetService<AudioService>()
        let! ac = audioService.Join(currentVC) |> Async.AwaitTask
        currentAC <- ac
        let bufferedFrames = Queue<byte[]>()
        let mutable fileFullyRead = false
        let ffmpegMre = new ManualResetEvent false
        let ffmpegProc = Process.Start ffmpegStreamPsi
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
        while bufferedFrames.Count > 0 && not clear do
            let b =  bufferedFrames.Dequeue()
            currentAC.Send(b, 0, b.Length)
            if stop then
                currentAC.Send(Array.zeroCreate<byte>(frameSize), 0, frameSize)
                while stop && not clear do
                    Thread.Sleep 250
        currentAC.Wait()
        ffmpegMre.WaitOne() |> ignore
        isActive <- false
        clear <- false
    }

    //let songQueueProcessor() =
    //    while true do
    //        Thread.Sleep 100
    //        if songQueue.Count > 0 then
    //            ()

    member this.Play (cmdMsg : MessageEventArgs) =
        match isActive, stop with
        | true, true ->
            stop <- false
            null
        | false, false ->
            let title = fetchSong cmdMsg
            playSong cmdMsg |> Async.Start
            title
        | _ -> failwith "Unsupport operation."

    member this.Stop() = stop <- true

    member this.RemoveSong (opt : string) =
        match opt with
        | _ as o when opt.ToUpperInvariant() = "ALL" ->
            songQueue.Clear()
            clear <- true
        | _ -> ()
        clear <- true