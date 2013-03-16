// 在 http://fsharp.net 上了解有关 F# 的更多信息
// 请参阅“F# 教程”项目以获取更多帮助。
module Program

open System

[<EntryPoint>]
let main argv = 
    let server = new FSFtp.FtpServer()
    server.Start()
    System.Console.WriteLine("Server running. input <quit> to stop")
    while System.Console.ReadLine() <> "quit" do 
        System.Console.WriteLine("Input <quit> to stop")

    server.Stop()

    0 // 返回整数退出代码
