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


open Suave.Sockets.Control
open Suave.WebSocket
open LiveFeed
open System
open System.IO
open System.Reflection
open Aether


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

 
let (|Message|_|) msg = 
  match msg with
  | (Text, data, true) -> Some(UTF8.toString data)
  | _ -> None

let private logIfError (logger: Logger) ctx =
  readUserState ctx "err"
  |> Option.iter logger.logSimple
  succeed

let private socketHandler (ws : WebSocket) (context: HttpContext) =
      socket {
        let mutable loop = true
        let id : Id = Guid.NewGuid().ToString() /// Pass user's ID
        printfn "[socketHandler] Got a connection"
        while loop do
          let! msg = ws.read()
          match msg with
          | Message(str) -> 
            Stream.onConnect(id, ws, str)
          | (Close, _, _) -> Stream.closeSocket(id)
                             loop <- false
          | _ -> ()
        done
      }

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
    // LiveFeed.Suave.webpart getDataContext getStreamClient
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
