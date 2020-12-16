module Chessie

open Chessie.ErrorHandling

let mapFirstFailure f result =
  let mapFirstItem = function
    | msg :: _ -> msg |> f |> List.singleton
    | [] -> failwith "Chessie.mapFirstFailure called with an empty list"
  mapFailure mapFirstItem result

let private onSuccessAdapter f (x, _) = f x

let private onFailureAdapter f = function
  | x :: _ -> f x
  | [] -> failwith "Chessie.onFailure called with an empty list"

let either onSuccess onFailure =
  either (onSuccessAdapter onSuccess) (onFailureAdapter onFailure)

let (|Success|Failure|) = function
  | Ok (x, _) -> Success x
  | Bad (msgs) -> Failure (List.head msgs)

[<RequireQualifiedAccess>]
module AR =

  let mapFailure f asyncResult =
    asyncResult
    |> Async.ofAsyncResult
    |> Async.map (mapFirstFailure f)
    |> AR
  let mapSuccess f asyncResult =
    asyncResult
    |> Async.ofAsyncResult
    |> Async.map (lift f)
    |> AR

  let catch asyncComputation =
    asyncComputation
    |> Async.Catch
    |> Async.map ofChoice
    |> AR

  let fail msg =
    msg
    |> fail
    |> Async.singleton
    |> AR

  let either onSuccess onFailure asyncResult =
    asyncResult
    |> Async.ofAsyncResult
    |> Async.map (either onSuccess onFailure)