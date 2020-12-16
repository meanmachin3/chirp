open Database
open Email
open Hopac
open Logary
open Logary.Configuration
open Logary.Targets
open Suave
open Suave.DotLiquid
open Suave.Files
open Suave.Filters
open Suave.Operators


open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket

open System
open System.IO
open System.Reflection
open Aether


type Id = String
module Sockets = begin

    let sendText (webSocket : WebSocket) (response:string) = begin
        let byteResponse =
          response
          |> System.Text.Encoding.ASCII.GetBytes
          |> ByteSegment

        // the `send` function sends a message back to the client
        webSocket.send Text byteResponse true
    end   

    type Commands = 
        | ReceiveString of Id * WebSocket * string
        | SendAll of string
        | CloseSocket of Id

    let rec private startMailbox(inbox:MailboxProcessor<Commands>) = begin
        
        let rec doLoop(sockets:Map<Id,WebSocket>) = async {
            let! input = inbox.Receive()
            match input with
            | ReceiveString(id, sock, input) -> 
                return! sockets |> Map.add id sock |> doLoop
            | SendAll(text) ->
                for (id, sock) in sockets |> Map.toSeq do
                    let! res = text |> sendText sock
                    ()
                return! doLoop(sockets)
            | CloseSocket(id) ->
                let ws = sockets.Item id
                let emptyResponse = [||] |> ByteSegment
                ws.send Close emptyResponse true |> ignore
                return! sockets |> Map.remove id |> doLoop
        }

        doLoop(Map.empty)
    end

    let private inbox = MailboxProcessor.Start(startMailbox)

    let onConnect(id, socket, data) = inbox.Post(ReceiveString(id, socket, data))
    let sendAll(text) = inbox.Post(SendAll(text))
    let closeSocket(id) = inbox.Post(CloseSocket(id))

end

let (|Message|_|) msg = 
    match msg with
    | (Text, data, true) -> Some(UTF8.toString data)
    | _ -> None

let private socketHandler (ws : WebSocket) (context: HttpContext) =

  socket {
    let mutable loop = true
    let id : Id = Guid.NewGuid().ToString() /// Pass user's ID
    printfn "[socketHandler] Got a connection"
    while loop do
      let! msg = ws.read()
      match msg with
      | Message(str) -> 
        Sockets.onConnect(id, ws, str)
      | (Close, _, _) -> Sockets.closeSocket(id)
                         loop <- false
      | _ -> ()
    done
  }



let currentPath =
  Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

let initDotLiquid =
  let templatesDir = Path.Combine(currentPath, "views")
  setTemplatesDir templatesDir

let serveAssets =
  pathRegex "/assets/*" >=> browseHome

let serveFavIcon =
  let favIconPath = Path.Combine(currentPath, "assets", "images", "favicon.ico")
  path "/favicon.ico" >=> file favIconPath

let portEnvVar = Environment.GetEnvironmentVariable "PORT"
let port = if String.IsNullOrEmpty portEnvVar then 5000 else int portEnvVar
let databaseUrl = if String.IsNullOrEmpty portEnvVar then "postgres://postgres:test@localhost:5432/fstweet" else Environment.GetEnvironmentVariable "DATABASE_URL"
let connectionString = makeConnectionString databaseUrl
let getDataContext = dataContext connectionString
let postmarkServerKey = Environment.GetEnvironmentVariable "FSTWEET_POSTMARK_SERVER_KEY"
let senderEmailAddress = Environment.GetEnvironmentVariable "FSTWEET_SENDER_EMAIL_ADDRESS"
let siteBaseUrl = Environment.GetEnvironmentVariable "FSTWEET_SITE_BASE_URL"
let env = Environment.GetEnvironmentVariable "FSTWEET_ENVIRONMENT"
let suaveServerKey = "aWZp/TH3jaHMH8N4X0jdZtRinjzpG6AyYQpJwb3mVpI=" |> ServerKey.fromBase64

let streamConfig: GetStream.Config = {
  ApiKey = "3x3gn6v8eyqf"
  ApiSecret = "nsvj924qz6bpykhxmxbucn2sdxzbj2n7zqvauxdt3f98e4ut3zkudnymxpuhrrgu"
  AppId = "103615"
}

let getStreamClient = GetStream.newClient streamConfig

let config =
  { defaultConfig with
      bindings = [ HttpBinding.createSimple HTTP "0.0.0.0" port ]
      homeFolder = Some currentPath
      serverKey = suaveServerKey
  }

let sendEmail =
  match env with
  | "prod" -> initSendEmail senderEmailAddress siteBaseUrl postmarkServerKey
  | _ -> consoleSendEmail

let target = withTarget (Console.create Console.empty "console")

let rule = withRule (Rule.createForTarget "console")

let logaryConf = target >> rule

let private readUserState ctx key: 'value option =
  ctx.userState
  |> Map.tryFind key
  |> Option.map (fun x -> x :?> 'value)

let private logIfError (logger: Logger) ctx =
  readUserState ctx "err"
  |> Option.iter logger.logSimple
  succeed

let app =
  choose [
    serveAssets
    serveFavIcon
    path "/" >=> page "guest/home.liquid" ""
    path "/websocket" >=> handShake socketHandler
    UserSignup.Suave.webPart getDataContext sendEmail
    Auth.Suave.webpart getDataContext
    Wall.Suave.webpart getDataContext getStreamClient
    Social.Suave.webpart getDataContext getStreamClient
    UserProfile.Suave.webpart getDataContext getStreamClient
  ]

[<EntryPoint>]
let main argv =
  let logary = withLogaryManager "FsTweet.Web" logaryConf |> run
  let logger = logary.getLogger (PointName [|"Suave"|])
  let appWithLogger = app >=> context (logIfError logger)  
  initDotLiquid
  setCSharpNamingConvention ()
  startWebServer config appWithLogger
  0
