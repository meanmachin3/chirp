module Email

open Chessie.ErrorHandling
open PostmarkDotNet

type TemplatedEmail = {
  To: string
  TemplateId: int64
  PlaceHolders: Map<string, string>
}

type SendEmail = TemplatedEmail -> AsyncResult<unit, System.Exception>

let private mapPostmarkResponse response =
  match response with
  | Choice1Of2 (postmarkResponse: PostmarkResponse) ->
    match postmarkResponse.Status with
    | PostmarkStatus.Success -> ok ()
    | _ -> new System.Exception(postmarkResponse.Message) |> fail
  | Choice2Of2 ex -> fail ex


let consoleSendEmail (templatedEmail: TemplatedEmail) = asyncTrial {
  printfn "[consoleSendEmail] sending email: %A" templatedEmail
}
