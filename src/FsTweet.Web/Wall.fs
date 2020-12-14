namespace Wall

module Domain =
  open Chessie
  open Chessie.ErrorHandling
  open System
  open Tweet
  open User

  type NoifyTweet = Tweet -> AsyncResult<unit, Exception>

  type PublishTweetError =
  | CreateTweetError of Exception
  | NotifyTweetError of (TweetId * Exception)

  type PublishTweet = User -> Post -> AsyncResult<TweetId, PublishTweetError>

  let publishTweet createTweet notifyTweet (user: User) post = asyncTrial {
    let! tweetId = createTweet user.UserId post |> AR.mapFailure CreateTweetError
    let tweet = {
      Id = tweetId
      UserId = user.UserId
      Username = user.Username
      Post = post
    }
    do! notifyTweet tweet |> AR.mapFailure (fun ex -> NotifyTweetError (tweet.Id, ex))
    return tweetId
  }

module GetStream =
  open Chessie.ErrorHandling
  open Stream
  open Tweet
  open User

  let private mapStreamResponse = function
  | Choice1Of2 _ -> ok ()
  | Choice2Of2 ex -> fail ex

  let notifyTweet (getStreamClient: GetStream.Client) (tweet: Tweet) = 

    let (UserId userId) = tweet.UserId
    let (TweetId tweetId) = tweet.Id
    let userFeed = GetStream.userFeed getStreamClient userId
    let activity = new Activity(userId.ToString(), "tweet", tweetId.ToString())
    activity.SetData("tweet", tweet.Post.Value)
    activity.SetData("username", tweet.Username.Value)
    userFeed.AddActivity(activity)
    |> Async.AwaitTask
    |> Async.Catch
    |> Async.map mapStreamResponse
    |> AR

module Suave =
  open Auth.Suave
  open Chessie
  open Chiron
  open Domain
  open Logary
  open Suave
  open Suave.DotLiquid
  open Suave.Filters
  open Suave.Operators
  open Suave.Writers
  open Tweet
  open User
  open UserSignup

  type WallViewModel = {
    Username: string
    UserId: int
    ApiKey: string
    AppId: string
    UserFeedToken: string
    TimelineToken: string
  }

  type PostRequest = PostRequest of string with
    static member FromJson (_: PostRequest) = json {
      let! post = Json.read "post"
      return PostRequest post
    }

  let private renderWall (getStreamClient: GetStream.Client) (user: User) ctx = async {
    let (UserId userId) = user.UserId
    let userFeed = GetStream.userFeed getStreamClient userId
    let timelineFeed = GetStream.timelineFeed getStreamClient userId
    let viewModel = {
      Username = user.Username.Value
      UserId = userId
      ApiKey = getStreamClient.Config.ApiKey
      AppId = getStreamClient.Config.AppId
      UserFeedToken = userFeed.ReadOnlyToken
      TimelineToken = timelineFeed.ReadOnlyToken
    }
    return! page "user/wall.liquid" viewModel ctx
  }

  let private onPublishTweetSuccess (TweetId id): WebPart =
    ["id", Json.String (id.ToString())]
    |> Map.ofList
    |> Json.Object
    |> JSON.ok

  let private onPublishTweetFailure (user: User) (err: PublishTweetError) =
    let (UserId userId) = user.UserId
    let msg =
      Message.event LogLevel.Error "Tweet Notification Error"
      |> Message.setField "userId" userId
    match err with
    | NotifyTweetError (tweetId, ex) ->
      let (TweetId guid) = tweetId
      msg
      |> Message.addExn ex
      |> Message.setField "tweetId" guid
      |> setUserData "err"
      >=> onPublishTweetSuccess tweetId
    | CreateTweetError ex ->
      msg
      |> Message.addExn ex
      |> setUserData "err"
      >=> JSON.internalServerError

  let private handleNewTweet (publishTweet: PublishTweet) (user: User) ctx = async {
    match JSON.deserialise ctx.request with
    | Success (PostRequest post) ->
      match Post.TryCreate post with
      | Success post ->
        let! webpart =
          publishTweet user post
          |> AR.either onPublishTweetSuccess (onPublishTweetFailure user)
        return! webpart ctx
      | Failure msg -> return! JSON.badRequest msg ctx
    | Failure msg -> return! JSON.badRequest msg ctx
  }

  let webpart getDataContext getStreamClient =
    let createTweet = Persistence.createTweet getDataContext
    let notifyTweet = GetStream.notifyTweet getStreamClient
    let publishTweet = publishTweet createTweet notifyTweet
    choose [
      GET >=> path "/wall" >=> requiresAuth (renderWall getStreamClient)
      POST >=> path "/tweets" >=> requiresAuth2 (handleNewTweet publishTweet)
    ]
