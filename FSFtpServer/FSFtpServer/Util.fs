module FSFtp.Util
    open System.Net
    open System.Net.Sockets
    open System.Net.NetworkInformation

    let private GetIPAddress (adapter : NetworkInterface) = 
        let addresses = adapter.GetIPProperties().UnicastAddresses;
        let ip = query{
            for ad in addresses do
            where (ad.Address.AddressFamily = AddressFamily.InterNetwork)
            select ad
            headOrDefault}
        ip.Address

    let private FindIPv4Adapter(nics : NetworkInterface[]) = 
        query{
            for adp in nics do
            where (adp.Supports NetworkInterfaceComponent.IPv4)
            select adp
            headOrDefault}

    let LocalIP() = 
        let nics = NetworkInterface.GetAllNetworkInterfaces()
        let adapter = FindIPv4Adapter nics

        let address = 
            match adapter with
            |null -> null
            |_-> GetIPAddress adapter

        address

    let makeLock locker =
        System.Threading.Monitor.Enter(locker)
        { 
            new System.IDisposable with  
                member x.Dispose() =System.Threading.Monitor.Exit(locker) 
        }
