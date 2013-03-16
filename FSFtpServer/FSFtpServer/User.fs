namespace FSFtp

open System
open System.Linq
open System.Xml.Serialization
open System.IO

[<Serializable>]
type User() =
    let mutable _currentDir = String.Empty

    member this.CurrentDirectory 
        with get() = 
            let dir = _currentDir.Replace(this.HomeDir, String.Empty).Replace('\\', '/')
            match dir with
            | dir when dir.Length > 0 ->dir
            | _ -> "/"

    [<XmlAttribute("username")>]
    member val Username : String = String.Empty with get, set
 
    [<XmlAttribute("password")>]
    member val Password : String = String.Empty with get, set
 
    [<XmlAttribute("homedir")>]
    member val HomeDir : String = String.Empty with get, set

    [<XmlArrayItem(ElementName= "grant", Type = typeof<String> )>]
    [<XmlArray>]
    member val AccessGrant : String array = Array.empty with get, set

    member this.CanAccess(cmd : String) =
        let cmdUpper = cmd.ToUpper()
        this.AccessGrant <> null && Enumerable.Contains(this.AccessGrant, cmdUpper)

    member this.IsValid = this.HomeDir <> String.Empty 

    member private this.NewDir(pathname: String) = 
        let mutable newDir = ""
        if pathname.StartsWith("/") then
            let sub = pathname.Substring(1).Replace('/', '\\')
            newDir <- Path.Combine(this.HomeDir, sub)
        else
            let sub = pathname.Replace('/', '\\')
            newDir <- Path.Combine(_currentDir, sub)
        (new DirectoryInfo(newDir)).FullName

    member this.ChangeWorkDirectory(pathname: String) =
        if pathname = "/" then 
            _currentDir <- this.HomeDir
            true
        else
            let  newDir = this.NewDir(pathname)
            if newDir.StartsWith(this.HomeDir) && Directory.Exists(newDir) then
                _currentDir <- newDir
                true
            else
                false

    member this.NormalizeFilename(param : String) =
        let mutable path = param.Trim([|'\"'|])
        if path = null then
            path <- String.Empty
        if path = "/" then
            path <- this.HomeDir
        else
            if path.StartsWith("/") then
                path <- (new FileInfo(Path.Combine(this.HomeDir, path.Substring(1)))).FullName
            else
                path <- (new FileInfo(Path.Combine(_currentDir, path))).FullName

        if path.StartsWith(this.HomeDir) then
            path
        else
            null

    static member AnonymouseUser() =
            let user = new User()
            user.AccessGrant <- [|"CWD";"CDUP";"PASV";"PORT";"LIST";"SYST";"PWD";"TYPE";"NOOP";"RETR";"REST";"SIZE";"MDTM";"ABOR";"REIN";"QUIT"|]
            user.Username <- "anonymous"
            user.Password <- ""
            user

    static member AnonymouseUserName with get() = "anonymous"
