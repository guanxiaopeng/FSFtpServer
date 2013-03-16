namespace FSFtp

open System
open System.Net
open System.Net.Sockets
open System.Net.NetworkInformation

type FtpServer(endPoint : IPEndPoint) =
    let _localEndPoint = endPoint
    let mutable _listener : TcpListener = null

    new() = FtpServer(new IPEndPoint(Util.LocalIP(), 21))

    static member private HandleClient(client : TcpClient) = 
        let clientConnection = new ClientConnection(client)
        clientConnection.Start()

    static member private HandleAcceptTcpClient(result : IAsyncResult) =
        try
            if result.IsCompleted then
                let listener = result.AsyncState :?> TcpListener
                let client = listener.EndAcceptTcpClient(result)
                FtpServer.HandleClient(client)
                let callback = new AsyncCallback(FtpServer.HandleAcceptTcpClient)
                ignore( listener.BeginAcceptTcpClient(callback, listener) )
        with
        |ex->()
        
    member this.Start() = 
        if _listener = null then
            let listener = new TcpListener(_localEndPoint);
            listener.Start();
            let callback = new AsyncCallback(FtpServer.HandleAcceptTcpClient);
            ignore(listener.BeginAcceptTcpClient(callback, listener));
            ignore(_listener <- listener)

    member this.Stop() = 
        if _listener <> null then
            _listener.Stop();
            _listener <- null
            ClientConnection.CloseAll()
