namespace Tweet

open Chessie.ErrorHandling
open System
open User

type Post = private Post of string with
  static member TryCreate (post: string) =
    match post with
    | null | "" -> fail "Tweet should not be empty"
    | x when x.Length > 140 -> fail "Tweet should not be longer than 140 characters"
    | x -> Post x |> ok
  member this.Value =
    let (Post post) = this
    post

type TweetId = TweetId of Guid

type CreateTweet = UserId -> Post -> AsyncResult<TweetId, Exception>

type Tweet = {
  UserId: UserId
  Username: Username
  Id: TweetId
  Post: Post
}

module Persistence =
  open Database

  let createTweet (getDataContext: GetDataContext) (UserId userId) (post: Post) = asyncTrial {
    use dbContext = getDataContext ()
    let newTweet = {
      Id = Guid.NewGuid ()
      Post = post.Value
      UserId = userId
      TweetedAt = DateTime.UtcNow
    }
    dbContext.Tweets.Add(newTweet) |> ignore
    do! saveChangesAsync dbContext
    printfn "[createTweet] tweet created: %A" newTweet
    return TweetId newTweet.Id
  }
