namespace FSFtp
open System
open System.Collections.Generic
open System.Linq
open System.Text
open System.IO
open System.Xml.Serialization

type UserStore() =
    static let _instance = new UserStore()
    let mutable _users = new List<User>()
    do
        let serializer = new XmlSerializer(_users.GetType(), new XmlRootAttribute("Users"))
        if File.Exists("users.xml") then
            use reader = new StreamReader("users.xml")
            _users <- serializer.Deserialize(reader) :?> List<User>
        else
            let defaultUser = User.AnonymouseUser()
            defaultUser.HomeDir <- "E:\\temp\\"
            _users.Add(defaultUser)
            use writer = new StreamWriter("users.xml")
            serializer.Serialize(writer, _users)

    member private this.FindUser(username : String , password : String) =
        query{
            for u in _users do
            where (u.Username = username && (u.Password = password || u.Username = User.AnonymouseUserName))
            select u
            headOrDefault}

    static member Validate(username : String , password : String) =
        _instance.FindUser(username, password)
        


