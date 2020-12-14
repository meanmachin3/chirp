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

let private sendEmailViaPostmark
  senderEmailAddress
  siteBaseUrl
  (postmarkClient: PostmarkClient)
  templatedEmail =

  let fromAddress = senderEmailAddress
  // TODO: toAddress should be 'templatedEmail.To' but I have not
  //       submitted my Postmark account for approval yet so I can
  //       only send to 'senderEmailAddress'.
  let toAddress = senderEmailAddress
  let placeHolders =
    templatedEmail.PlaceHolders
      .Add("emailAddress", templatedEmail.To)
      .Add("siteBaseUrl", siteBaseUrl)

  printfn
    "[sendEmailViaPostmark] From: %s; To: %s; TemplateId: %d; TemplateModel: %A"
    fromAddress
    toAddress
    templatedEmail.TemplateId
    placeHolders

  let msg =
    new TemplatedPostmarkMessage(
      From = fromAddress,
      To = toAddress,
      TemplateId = templatedEmail.TemplateId,
      TemplateModel = placeHolders
    )

  postmarkClient.SendMessageAsync(msg)
  |> Async.AwaitTask
  |> Async.Catch
  |> Async.map mapPostmarkResponse
  |> AR

let initSendEmail senderEmailAddress siteBaseUrl serverKey =
  let postmarkClient = new PostmarkClient(serverKey)
  sendEmailViaPostmark senderEmailAddress siteBaseUrl postmarkClient

let consoleSendEmail (templatedEmail: TemplatedEmail) = asyncTrial {
  printfn "[consoleSendEmail] sending email: %A" templatedEmail
}
