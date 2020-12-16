namespace LiveFeed
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket    
open System

type Id = String

module Stream = begin

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

module Suave = 
  open Suave
  open Suave.Filters
  open Suave.Operators
  
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
            Stream.onConnect(id, ws, str)
          | (Close, _, _) -> Stream.closeSocket(id)
                             loop <- false
          | _ -> ()
        done
      }
  
  let webpart getDataContext getStreamClient =
    choose [
      path "/websocket" >=> handShake socketHandler
    ]