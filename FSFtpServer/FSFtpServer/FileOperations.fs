namespace FSFtp

open System
open System.IO
open System.Text
open System.Globalization

type TransferType = 
    |Ascii = 0
    |Ebcdic = 1
    |Image = 2
    |Local = 3

type FileOperations() = 
    let mutable _transferOffset = int64 0
    let mutable _isAbort = false
    let mutable _transferType = TransferType.Ascii

    member this.TransferOffset with set(value) = _transferOffset <- value
    member this.IsTransferAborted 
        with set(value) = _isAbort <- value
        and  get() = _isAbort

    member this.TransferType 
        with set(value) = _transferType <- value
        and get() = _transferType

    member val RenameFrom : String = String.Empty with get, set

    member this.RenameTo(renameTo: String) = 
        let mutable isOK = false
        if File.Exists(this.RenameFrom) then
            File.Move(this.RenameFrom, renameTo)
            isOK <- true
        elif Directory.Exists(this.RenameFrom) then
            Directory.Move(this.RenameFrom, renameTo)
            isOK <- true
        this.RenameFrom <- String.Empty
        isOK

    member this.List(pathName : String, encoding: Encoding, dataStream : Stream ) = 
        let dataWriter = new StreamWriter(dataStream, encoding)
        let myDTFI = (new CultureInfo("en-US", false)).DateTimeFormat
        this.ListSubDir(pathName, myDTFI, dataWriter)
        this.ListFiles(pathName, myDTFI, dataWriter)       
        dataWriter.Flush()

    member private this.ListSubDir(pathName : String, myDTFI : DateTimeFormatInfo, dataWriter : StreamWriter ) = 
        let directories = Directory.EnumerateDirectories(pathName)
        for dir in directories do
            if not _isAbort then
                let d = new DirectoryInfo(dir)
                let date = FileOperations.FormatTime(d.CreationTime, myDTFI)
                let line = String.Format("drwxr-xr-x    2 2012     2012     {0,8} {1} {2}", "4096", date, d.Name)
                dataWriter.WriteLine(line)
        
    member private this.ListFiles(pathName : String, myDTFI : DateTimeFormatInfo, dataWriter : StreamWriter ) = 
        let files = Directory.EnumerateFiles(pathName)
        for file in files do
            if not _isAbort then
                let f = new FileInfo(file)
                let date = FileOperations.FormatTime(f.CreationTime, myDTFI)
                let line = String.Format("-rw-r--r--    2 2012     2012     {0,8} {1} {2}", f.Length, date, f.Name)
                dataWriter.WriteLine(line)

    static member private FormatTime(d : DateTime, myDTFI : System.Globalization.DateTimeFormatInfo ) =
        match d with 
        | d when d < DateTime.Now - TimeSpan.FromDays(180.) -> d.ToString("MMM dd  yyyy", myDTFI)
        | _-> d.ToString("MMM dd HH:mm", myDTFI)

    member private this.CopyStream(input : Stream , output : Stream) = 
        let buffer : byte array = Array.zeroCreate(160 * 1024)
        let mutable count = 0
        let mutable total = int64 0
        let mutable readEnd = false

        _isAbort <- false
        while (not _isAbort && not readEnd) do
            count <- input.Read(buffer, 0, buffer.Length)
            readEnd <- count = 0
            if not readEnd then
                output.Write(buffer, 0, count)
                total <- total + int64 count

    member private this.CopyStreamAscii(input : Stream , output : Stream) = 
        let buffer : char array = Array.zeroCreate(160 * 1024)
        let mutable count = 0
        let mutable total = int64 0
        let mutable readEnd = false

        let encoding = new ASCIIEncoding()
        use reader = new StreamReader(input, encoding)
        use writer = new StreamWriter(output, encoding)
        _isAbort <- false
        while (not _isAbort && not readEnd) do
            count <- reader.Read(buffer, 0, buffer.Length)
            readEnd <- count = 0
            if not readEnd then
                writer.Write(buffer, 0, count)
                total <- total + int64 count

    member this.RetrieveFile(pathname : String, dataStream : Stream) =
        use fs = new FileStream(pathname, FileMode.Open, FileAccess.Read)
        ignore(fs.Seek(_transferOffset, SeekOrigin.Begin))
        _transferOffset <-int64 0

        if _transferType = TransferType.Image then
            this.CopyStream(fs, dataStream)
        elif _transferType = TransferType.Ascii then
            this.CopyStreamAscii(fs, dataStream)

    member this.StoreFile(pathname : String, dataStream : Stream) =
        use fs = new FileStream(pathname, FileMode.OpenOrCreate, FileAccess.Write)
        ignore(fs.Seek(_transferOffset, SeekOrigin.Begin))
        _transferOffset <-int64 0

        if _transferType = TransferType.Image then
            this.CopyStream(dataStream, fs)
        elif _transferType = TransferType.Ascii then
            this.CopyStreamAscii(dataStream, fs)

    static member GetLastWriteTime(path : String) = 
        if path <> null then
            if File.Exists(path) then
                File.GetLastWriteTime(path)
            elif Directory.Exists(path) then
                (new DirectoryInfo(path)).LastWriteTime
            else
                DateTime.MinValue
        else
            DateTime.MinValue
