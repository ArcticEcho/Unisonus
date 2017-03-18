namespace Unisonus

open System
open System.Collections.Generic
open System.Threading
open System.Diagnostics
open Discord
open Discord.Audio

type Player (client : DiscordClient) =
    let ffmpegPsi =
        let psi = new ProcessStartInfo()
        psi.FileName <- Config.FfmpegPath
        psi.Arguments <- "-i song.mp3 -f s16le -ar 48000 -ac 2 pipe:1"
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi
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

    let playSong (cmdMsg : MessageEventArgs) = async {
        isActive <- true
        currentVC <- cmdMsg.User.VoiceChannel
        let audioService = client.GetService<AudioService>()
        let! ac = audioService.Join(currentVC) |> Async.AwaitTask
        currentAC <- ac
        let bufferedFrames = Queue<byte[]>()
        let mutable fileFullyRead = false
        let ffmpegMre = new ManualResetEvent false
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
        while bufferedFrames.Count > 0 && not clear do
            let b =  bufferedFrames.Dequeue()
            currentAC.Send(b, 0, b.Length)
            if stop then
                currentAC.Send(Array.zeroCreate<byte>(frameSize), 0, frameSize)
                while stop do
                    Thread.Sleep 250
        currentAC.Wait()
        ffmpegMre.WaitOne() |> ignore
        isActive <- false //TODO: wait for ffmpeg to stop
    }

    member this.Play (cmdMsg : MessageEventArgs) = async {
        match isActive, stop with
        | true, true -> stop <- false
        | false, false -> do! playSong cmdMsg
        | _ -> failwith "Unsupport operation."
    }

    member this.Stop() = stop <- true