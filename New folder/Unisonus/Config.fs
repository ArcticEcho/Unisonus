namespace Unisonus

open System.IO

module Config =
    let private fileData = File.ReadAllLines("config.txt")

    let FfmpegPath = fileData.[0].Remove(0, 11).Trim()

    let BotToken = fileData.[1].Remove(0, 9).Trim()