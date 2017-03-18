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

    member this.Play (cmdMsg : MessageEventArgs) = async {
        currentVC <- cmdMsg.User.VoiceChannel
        let audioService = client.GetService<AudioService>()
        let! ac = audioService.Join(currentVC) |> Async.AwaitTask
        currentAC <- ac
        let ffmpegProc = Process.Start ffmpegPsi
        Thread.Sleep 1000
        let bufferedFrames = Queue<byte[]>()
        let mutable fileFullyRead = false
        async {
            while not fileFullyRead do
                while bufferedFrames.Count > targetBufferFrames do
                    Thread.Sleep 5
                let b = Array.zeroCreate<byte>(frameSize)
                let byteCount = ffmpegProc.StandardOutput.BaseStream.Read(b, 0, b.Length)
                if byteCount = 0 then
                    fileFullyRead <- true
                bufferedFrames.Enqueue b
                Thread.Sleep 15
        } |> Async.Start
        while bufferedFrames.Count < targetBufferFrames do
            Thread.Sleep 100
        while bufferedFrames.Count > 0 do
            let b =  bufferedFrames.Dequeue()
            currentAC.Send(b, 0, b.Length)
        currentAC.Wait()
    }