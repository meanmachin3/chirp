namespace Tweet

open Chessie.ErrorHandling
open System
open User
open Chiron

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
// type GetAllTweet = UserId -> Re<TweetId, Exception>

type Tweet = {
  UserId: UserId
  Username: Username
  Id: TweetId
  Post: Post
}

type TweetMessage = {
  UserId: UserId
  Post: Post
} with
    static member ToJson (u: TweetMessage) =
      json {
        do! Json.write "userid" u.UserId.Value
        do! Json.write "post" u.Post.Value
      }

module Persistence =
  open Database
  open Microsoft.EntityFrameworkCore
  open Chessie
  open System.Linq
  open User.Persistence

  let mapUserEntityToUser (tweet: Database.Tweet) =
    let userResult = trial {
      // let! username = Username.TryCreate userEntity.Username
      // let! passwordHash = PasswordHash.TryCreate userEntity.PasswordHash
      let! post = Post.TryCreate tweet.Post
      // let userEmailAddress =
      //   match userEntity.IsEmailVerified with
      //   | true -> Verified email
      //   | false -> NotVerified email

      return {
        UserId = UserId tweet.UserId
        Post = post
      }      
    }
    userResult |> mapFirstFailure Exception
    
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

  let mapTweetEntities (tweetEntities: Database.Tweet seq) =
    tweetEntities
    |> Seq.map mapUserEntityToUser
    |> collect
    |> mapFirstFailure (fun ex -> new AggregateException(ex) :> Exception)
    |> Async.singleton
    |> AR

  let GetTimeline (getDataContext: GetDataContext) (UserId userid) = asyncTrial {
    use dbContext = getDataContext ()
    let selectFolloweesQuery = query {
      for s in dbContext.Social do
        where (s.FollowerUserId = userid)
        select s.FollowingUserId
    }

    let! userIds =
      EntityFrameworkQueryableExtensions.ToListAsync(selectFolloweesQuery)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess List.ofSeq

    let tweetsQuery =
      query {
        for tweet in dbContext.Tweets do
          where (userIds.Contains(tweet.UserId))
          select tweet
      }
    
    let! tweets =
      EntityFrameworkQueryableExtensions.ToListAsync(tweetsQuery)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess List.ofSeq

    printfn "[GetTimeline] Got %A" tweets
    return! mapTweetEntities tweets    
  }

  let GetAllTweet (getDataContext: GetDataContext) (UserId userid) = asyncTrial {
    use dbContext = getDataContext ()
    let tweet = dbContext.Tweets.ToList()
    let tweetsQuery =
      query {
        for tweet in dbContext.Tweets do
          select tweet
      }
    
    let! tweets =
      EntityFrameworkQueryableExtensions.ToListAsync(tweetsQuery)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess List.ofSeq
    
    printfn "[GetAllTweet] Got tweet as %A" tweet
    printfn "[GetAllTweet] Got %A" tweets
    return! mapTweetEntities tweets    
  }
