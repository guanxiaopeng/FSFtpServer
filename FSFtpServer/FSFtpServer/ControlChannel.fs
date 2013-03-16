namespace FSFtp

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Collections.Generic
open System.IO
open System.Text

type ControlChannel(tcpClient : TcpClient) = 
    let _tcp = tcpClient
    let mutable _reader = new StreamReader(tcpClient.GetStream(), Encoding.ASCII)
    let mutable _writer = new StreamWriter(tcpClient.GetStream(), Encoding.ASCII)

    member this.ReadLine() = 
        try
            _reader.ReadLine()
        with
        | ex -> null

    member this.Response(str : String) = 
        if not (String.IsNullOrEmpty(str)) then
            _writer.WriteLine(str)
            _writer.Flush()
            System.Diagnostics.Debug.WriteLine("[out]"+str)

    member this.SetUTF8(isOn : bool) = 
        let encoding = 
            match isOn with 
            |true -> (new UTF8Encoding()):>Encoding 
            |false-> (new ASCIIEncoding()) :>Encoding

        _reader <- new StreamReader(_reader.BaseStream, encoding)
        _writer <- new StreamWriter(_writer.BaseStream, encoding)

    member this.Encoding with get() = _reader.CurrentEncoding
