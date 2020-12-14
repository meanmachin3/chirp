[<RequireQualifiedAccess>]
module JSON

open Chessie.ErrorHandling
open Chiron
open Suave
open Suave.Operators
open System.Text

let private json webpart json =
  json
  |> Json.format
  |> webpart
  >=> Writers.addHeader "content-type" "application/json; charset=utf-8"

let private error webpart msg =
  ["msg", String msg]
  |> Map.ofList
  |> Object
  |> json webpart

let unauthorized =
  error RequestErrors.UNAUTHORIZED "Login required"

let badRequest msg =
  error RequestErrors.BAD_REQUEST msg

let internalServerError =
  error ServerErrors.INTERNAL_ERROR "Something went wrong"

let ok = json (Successful.OK)

let parse request =
  request.rawForm
  |> Encoding.UTF8.GetString
  |> Json.tryParse
  |> ofChoice

let inline deserialise< ^a when (^a or FromJsonDefaults): (static member FromJson: ^a -> ^a Json)>
  req: Result< ^a, string> = trial {
    let! json = parse req
    return! json |> Json.tryDeserialize |> ofChoice
  }
