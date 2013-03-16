namespace FSFtp

open System
open System.Net
open System.Net.Sockets
open System.Threading

type DataOperation = delegate of (NetworkStream * String)->unit

[<AllowNullLiteralAttribute>]
type DataConnectionOperation(operation : DataOperation, param : String) = 
    let _operation = operation
    let _param = param
    let _eventCompleted = new ManualResetEvent(false)
    member this.Execute(stream : NetworkStream) = 
        _operation.Invoke(stream, _param)

    member this.NotifyComplete() = ignore(_eventCompleted.Set())
    
    member this.Wait() = ignore(_eventCompleted.WaitOne())


