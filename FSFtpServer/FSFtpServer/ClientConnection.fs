namespace FSFtp

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Linq
open System.Collections.Generic
open System.IO

type ClientConnection(controlChannel : TcpClient) as this = 
    let mutable _disposed = false
    let _controlClient = controlChannel
    let mutable _dataClient : TcpClient = null
    let mutable _passiveListener : TcpListener = null
    let _controlChannel = new ControlChannel(controlChannel)
    let mutable _currentUser = new User()
    let mutable _quit = false
    let mutable _operation : DataConnectionOperation = null
    let _locker = new Object()
    let _fileOperations = new FileOperations()
    let _eventQuit = new System.Threading.ManualResetEvent(false)

    static let sValidCommands = [| "AUTH"; "USER"; "PASS"; "QUIT"; "HELP"; "NOOP"; "OPTS" |]
    static let sInstances = new List<ClientConnection>()
    do
        sInstances.Add(this)

    member private this.WaitQuit() = ignore(_eventQuit.WaitOne())
    static member CloseAll() = 
        let copy = List<ClientConnection>(sInstances)
        for c in copy do 
            c.Quit()
            c.WaitQuit()

    member private this.DataOperation 
        with set(value) = 
            if _operation <> null then _operation.NotifyComplete()
            _operation <- value

    member private this.CheckUser(cmd : String) = 
        let mutable isOK = true
        if not _currentUser.IsValid then
            _controlChannel.Response("530 Not logged in")
            isOK <-false
        else if not (_currentUser.CanAccess(cmd)) then
            _controlChannel.Response("550 Access denied")
            isOK <- false
        isOK

    member private this.IsValidCmd(cmd : String) = 
        Enumerable.Contains(sValidCommands, cmd) || this.CheckUser(cmd)

    member this.User(param : String) = 
        _currentUser.Username <- param
        _controlChannel.Response("331 Username ok, need password")

    member this.Password(param : String) = 
        let user = UserStore.Validate(_currentUser.Username, param)
        if user.IsValid then 
            _currentUser <- user
            ignore(_currentUser.ChangeWorkDirectory("/"))
            this.DataOperation <- null
            _controlChannel.Response("230 User logged in")
        else
            _controlChannel.Response("530 Not logged in")

    member this.Options(param : String) = 
        let opt = param.ToLower()
        match opt with
        |"utf8 on" -> 
            _controlChannel.Response("200 utf8 is on")
            _controlChannel.SetUTF8(true)
        | "utf8 off"-> 
            _controlChannel.Response("200 utf8 is off")
            _controlChannel.SetUTF8(false)
        | _ -> _controlChannel.Response("501 options not supported : " + param)

    member this.Unsupport(cmd : String) =
        _controlChannel.Response("502 Command not implemented : " + cmd)

    member private this.ChangeWorkDirectory(pathname: String) =
        if _currentUser.ChangeWorkDirectory(pathname) then
            _controlChannel.Response("250 Changed to new directory");
        else
            _controlChannel.Response("501 invalid path");
    
    member private this.ExecuteDataOperation() = 
        try
            if _operation <> null && _dataClient<> null then
                this.Execute()
        with
        |ex-> this.ReportException(ex.Message)

    member private this.Execute() =
        try
            use stream = _dataClient.GetStream()
            _operation.Execute(stream)
            if _fileOperations.IsTransferAborted then
                _controlChannel.Response("426 Connection closed; transfer aborted")
        finally
            _dataClient.Close()
            _dataClient <- null   
            this.DataOperation <- null
            System.Diagnostics.Debug.WriteLine("Data connection closed")

    member private this.DisposeThis() = 
        ignore(sInstances.Remove(this))
        (this:>IDisposable).Dispose()

    member private this.ReportException(message : String) = 
        try
            _controlChannel.Response("550 " + message)
        with
        |ex -> this.DisposeThis()

    member private this.OnPassiveClientConnect(result : IAsyncResult) = 
        try
            use lo = Util.makeLock _locker

            System.Diagnostics.Debug.WriteLine("Passive client connected")
            if _dataClient<> null then _dataClient.Close()
            _dataClient <- _passiveListener.EndAcceptTcpClient(result)
            this.ExecuteDataOperation()
        with
        |ex-> this.ReportException(ex.Message)

    member private this.StartPassiveListener() = 
        if _passiveListener = null then
            _passiveListener <- new TcpListener(Util.LocalIP(), 0)
            _passiveListener.Start()
        ignore(_passiveListener.BeginAcceptTcpClient(new AsyncCallback(this.OnPassiveClientConnect), _passiveListener))

    member private this.Passive() =
        this.StartPassiveListener()
        let passiveListenerEndpoint = _passiveListener.LocalEndpoint :?> IPEndPoint
        let address = passiveListenerEndpoint.Address.GetAddressBytes()
        let port : int16 = int16 passiveListenerEndpoint.Port
        let portArray = BitConverter.GetBytes(port)
        if BitConverter.IsLittleEndian then Array.Reverse(portArray)
        _controlChannel.Response(String.Format("227 Entering Passive Mode ({0},{1},{2},{3},{4},{5})", address.[0], address.[1], address.[2], address.[3], portArray.[0], portArray.[1]))

    member private this.Port(param : String) = 
        let ipAndPort = param.Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
        let ipAddress : byte array = [|for i in 0..3 ->Convert.ToByte(ipAndPort.[i])|]
        let ports : byte array = [|for i in 4..5 ->Convert.ToByte(ipAndPort.[i])|]
        if BitConverter.IsLittleEndian then Array.Reverse(ports)

        let ip = new IPAddress(ipAddress)
        let port = BitConverter.ToInt16(ports, 0)
        this.PrepareActiveDataConnection(new IPEndPoint(ip, int port))

    member private this.PrepareActiveDataConnection(activeEndPoint : IPEndPoint ) = 
        if _dataClient<> null then _dataClient.Close()
        _dataClient <-  new TcpClient(activeEndPoint.AddressFamily)
        ignore(_dataClient.BeginConnect(activeEndPoint.Address, activeEndPoint.Port, new AsyncCallback(this.OnActiveClientConnect), _dataClient))

    member private this.OnActiveClientConnect(result : IAsyncResult) = 
        use lo = Util.makeLock _locker

        System.Diagnostics.Debug.WriteLine("Active client connected")
        let client = result.AsyncState :?> TcpClient
        client.EndConnect(result)
        _controlChannel.Response("200 Data Connection Established")
        this.ExecuteDataOperation()

    member private this.CheckDataOperation() = 
        if _operation <> null then
            _controlChannel.Response("503 Transferring")
        _operation = null

    member private this.List(param : String) = 
        if this.CheckDataOperation() then
            let path = _currentUser.NormalizeFilename(param)
            if path = null then
                _controlChannel.Response("501 Invalid path")
            else
                this.DataOperation <-new DataConnectionOperation(new DataOperation(this.DoList), path)
                this.ExecuteDataOperation()

    member private this.DoList(stream: NetworkStream, path : String) =
        _controlChannel.Response("150 Opening data transfer for LIST")
        _fileOperations.List(path, _controlChannel.Encoding, stream)
        this.TransferComplete()

    member private this.TransferComplete() = 
        _controlChannel.Response("226 Transfer complete")

    member private this.System() = 
        _controlChannel.Response("215 UNIX Type: L8")

    member private this.PrintWorkDirectory() = 
        _controlChannel.Response(String.Format("257 \"{0}\" is current directory.", _currentUser.CurrentDirectory))

    member private this.Type(param : String) = 
        let args = param.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
        if args.Length < 1 then 
            _controlChannel.Response("501 A param required")
        elif args.Length > 1 then
            _controlChannel.Response("504 Command not implemented for that parameter")
        else
            this.SetType((args.[0]).ToUpperInvariant())

    member private this.SetType(transType : String) = 
            match transType with
            |"A" -> 
                _fileOperations.TransferType <- TransferType.Ascii
                _controlChannel.Response(String.Format("200 Type set to {0}", _fileOperations.TransferType.ToString()))
            |"I" ->
                _fileOperations.TransferType <- TransferType.Image
                _controlChannel.Response(String.Format("200 Type set to {0}", _fileOperations.TransferType.ToString()))
            |_ ->
                _controlChannel.Response("504 Command not implemented for that parameter")

    member private this.NoOperation() = 
        _controlChannel.Response("200 OK")

    member private this.Retrieve(param : String) = 
        if this.CheckDataOperation() then
            let path = _currentUser.NormalizeFilename(param)
            if path <> null && File.Exists(path) then
                this.DataOperation <- new DataConnectionOperation(new DataOperation(this.DoRetrieve), path)
                this.ExecuteDataOperation()
            else
                _controlChannel.Response("550 File Not Found")
    
    member private this.DoRetrieve(stream: NetworkStream, fileName: String) = 
        _controlChannel.Response("150 Opening data transfer for RETR")
        _fileOperations.RetrieveFile(fileName, stream)
        this.TransferComplete()     

    member private this.Store(param : String) = 
        if this.CheckDataOperation() then
            let path = _currentUser.NormalizeFilename(param)
            this.DataOperation <-new DataConnectionOperation(new DataOperation(this.DoStore), path)
            this.ExecuteDataOperation()

    member private this.DoStore(stream: NetworkStream, fileName: String) = 
        _controlChannel.Response("150 Opening data transfer for STOR")
        _fileOperations.StoreFile(fileName, stream)
        this.TransferComplete()     

    member private this.Restart(param : String) = 
        _fileOperations.TransferOffset <- Convert.ToInt64(param)
        _controlChannel.Response("200 Command okay.")

    member private this.Delete(param : String) = 
        let path = _currentUser.NormalizeFilename(param)
        if path <> null && File.Exists(path) then
            File.Delete(path)
            _controlChannel.Response("250 Delete file okay, completed")
        else
            _controlChannel.Response("550 File Not Found")

    member private this.FileSize(param : String) = 
        let path = _currentUser.NormalizeFilename(param)
        if path <> null && File.Exists(path) then
            use fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            _controlChannel.Response(String.Format("213 {0}", fs.Length))
        else
            _controlChannel.Response("550 File not found")

    member private this.FileModificationTime(param : String) = 
        let path = _currentUser.NormalizeFilename(param)
        let time = FileOperations.GetLastWriteTime(path)
        if time <> DateTime.MinValue then
            let myDTFI = (new System.Globalization.CultureInfo("en-US", false)).DateTimeFormat
            _controlChannel.Response(String.Format("213 {0}", time.ToString("yyyyMMddHHmmss.fff", myDTFI)))
        else
            _controlChannel.Response("550 File not found")

    member private this.RenameFrom(param : String) = 
        let path = _currentUser.NormalizeFilename(param)
        if String.IsNullOrEmpty(path) then
            _controlChannel.Response( "501 Syntax error in parameters or arguments")
        else
            _fileOperations.RenameFrom <- path
            _controlChannel.Response( "350 Requested file action pending further information")

    member private this.RenameTo(param : String, prevCmd:String) = 
        if prevCmd <> "RNFR" then 
            _controlChannel.Response("503 Bad sequence of commands.")
        else
            let path = _currentUser.NormalizeFilename(param)
            if _fileOperations.RenameTo(path) then
                _controlChannel.Response("250 Rename file okay, completed")
            else
                _controlChannel.Response("450 Rename file action not taken")

    member private this.RemoveDirectory(param : String) = 
        let path = _currentUser.NormalizeFilename(param)
        if path <> null && Directory.Exists(path) then
            Directory.Delete(path)
            _controlChannel.Response( "250 Requested file action okay, completed")
        else
            _controlChannel.Response( "550 Directory not found")

    member private this.CreateDirectory(param : String) = 
        let path = _currentUser.NormalizeFilename(param)
        if path <> null && not (Directory.Exists(path)) then
            ignore (Directory.CreateDirectory(path))
            _controlChannel.Response( "250 Requested file action okay, completed")
        else
            _controlChannel.Response( "550 Directory already exists")

    member this.Abort() = 
        let op = _operation
        if op <> null then 
            _fileOperations.IsTransferAborted <- true
            op.Wait()
        _controlChannel.Response("226 Closing data connection.Requested file action successful")

    member this.Reinitialize() = 
        _currentUser <- new User()           
        _controlChannel.Response("220 Service ready for new user")

    member this.Quit() = 
        _controlChannel.Response("221 Closing control connection")
        if _operation = null then _controlClient.Close()            
        _quit<-true

    member private this.HandleCommand(cmd:String, param:String, prevCmd:String) = 
        use lo = Util.makeLock _locker
        match cmd with
        |"USER" -> this.User(param)
        |"PASS" -> this.Password(param)
        |"OPTS" -> this.Options(param)
        |"CWD"  -> this.ChangeWorkDirectory(param)
        |"CDUP" -> this.ChangeWorkDirectory("..")
        |"PASV" -> this.Passive()
        |"PORT" -> this.Port(param)
        |"LIST" -> this.List(param)
        |"SYST" -> this.System()
        |"PWD"  -> this.PrintWorkDirectory()
        |"TYPE" -> this.Type(param)
        |"NOOP" -> this.NoOperation()
        |"RETR" -> this.Retrieve(param)
        |"STOR" -> this.Store(param)
        |"REST" -> this.Restart(param)
        |"DELE" -> this.Delete(param)
        |"SIZE" -> this.FileSize(param)
        |"MDTM" -> this.FileModificationTime(param)
        |"RNFR" -> this.RenameFrom(param)
        |"RNTO" -> this.RenameTo(param, prevCmd)
        |"RMD"  -> this.RemoveDirectory(param)
        |"MKD"  -> this.CreateDirectory(param)
        |"ABOR" -> this.Abort()
        |"REIN" -> this.Reinitialize()
        |"QUIT" -> this.Quit()
        |_-> this.Unsupport(cmd)

    member private this.HandleClient(o : Object) = 
        try
            _controlChannel.Response("220 F# FTP Server by guanxp. Service Ready.")
            this.CommandLoop()
            let op = _operation
            if op <> null then op.Wait()
            ignore(_eventQuit.Set())
            this.DisposeThis()
        with
        |ex -> ignore(_eventQuit.Set()); this.DisposeThis()
        

    member this.CommandLoop() = 
        let mutable prevCmd = ""
        while not _quit do
            let line = _controlChannel.ReadLine()
            if line = null then 
                _quit<-true
                System.Diagnostics.Debug.WriteLine("Exit command loop")
            else
                System.Diagnostics.Debug.WriteLine("[in ]"+line)
                let command = line.Split(" ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
                let cmd = command.[0].ToUpperInvariant()
                if this.IsValidCmd(cmd) then
                    let param = line.Substring(cmd.Length).Trim()
                    this.HandleCommand(cmd, param, prevCmd)
                    prevCmd <- cmd

    member this.Start() = 
        let callback = new WaitCallback(this.HandleClient)
        ignore(ThreadPool.QueueUserWorkItem(callback))

    abstract member Dispose : bool->unit
    default this.Dispose(disposing : bool) =
        if not _disposed then
            if disposing then
                if _passiveListener <> null then _passiveListener.Stop()
                let toDispose = [|_controlClient:>IDisposable; _dataClient:>IDisposable|]

                for  obj in toDispose do
                    if obj <> null then
                        obj.Dispose()
            _disposed <- true

    interface IDisposable with
        member this.Dispose() = this.Dispose true