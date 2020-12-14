namespace Social

module Domain =
  open Chessie.ErrorHandling
  open System
  open User

  type CreateFollowing = User -> UserId -> AsyncResult<unit, Exception>
  type Subscribe = User -> UserId -> AsyncResult<unit, Exception>
  type FollowUser = User -> UserId -> AsyncResult<unit, Exception>
  type IsFollowing = User -> UserId -> AsyncResult<bool, Exception>
  type FindFollowers = UserId -> AsyncResult<User list, Exception>
  type FindFollowingUsers = UserId -> AsyncResult<User list, Exception>

  let followUser
    (subscribe: Subscribe)
    (createFollowing: CreateFollowing)
    user
    userId = asyncTrial {

      do! subscribe user userId
      do! createFollowing user userId      
    }
 
 module Persistence = 
  open Chessie
  open Chessie.ErrorHandling
  open Database
  open Microsoft.EntityFrameworkCore
  open System.Linq
  open User
  open User.Persistence

  let createFollowing (getDataContext: GetDataContext) (user: User) (UserId userId) = asyncTrial {
    use dbContext = getDataContext ()
    let (UserId followerUserId) = user.UserId
    let social = {
      Id = System.Guid.NewGuid ()
      FollowerUserId = followerUserId
      FollowingUserId = userId
    }
    // TODO: refactor into a utility function in the Database module (like saveChangesAsync)
    do! dbContext.Social.AddAsync(social)
      |> Async.AwaitTask
      |> Async.map ignore
      |> AR.catch
    do! saveChangesAsync dbContext
    return ()
  }

  let isFollowing (getDataContext: GetDataContext) (user: User) (UserId userId) = asyncTrial {
    use dbContext = getDataContext ()
    let (UserId followerUserId) = user.UserId
    let query = query {
      for s in dbContext.Social do
        where (s.FollowerUserId = followerUserId && s.FollowingUserId = userId)
    }
    let! maybeConnection =
    // TODO: refactor into a utility function in the Database module (like saveChangesAsync)
      EntityFrameworkQueryableExtensions.ToListAsync(query)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess (List.ofSeq >> List.tryHead)
    return maybeConnection.IsSome
  }

  let findFollowers (getDataContext: GetDataContext) (UserId userId) = asyncTrial {
    use dbContext = getDataContext ()
    let selectFollowersQuery = query {
      for s in dbContext.Social do
        where (s.FollowingUserId = userId)
        select s.FollowerUserId
    }
    // TODO: this differs from the book because otherwise it fails with:
    // System.NotImplementedException: The method or operation is not implemented.
    let! userIds =
      EntityFrameworkQueryableExtensions.ToListAsync(selectFollowersQuery)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess List.ofSeq
    let followersQuery = query {
      for u in dbContext.Users do
        where (userIds.Contains(u.Id))
        select u
    }
    let! followers =
      EntityFrameworkQueryableExtensions.ToListAsync(followersQuery)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess List.ofSeq
    return! mapUserEntities followers    
  }

  let findFollowees (getDataContext: GetDataContext) (UserId userId) = asyncTrial {
    use dbContext = getDataContext ()
    let selectFolloweesQuery = query {
      for s in dbContext.Social do
        where (s.FollowerUserId = userId)
        select s.FollowingUserId
    }
    // TODO: this differs from the book because otherwise it fails with:
    // System.NotImplementedException: The method or operation is not implemented.
    let! userIds =
      EntityFrameworkQueryableExtensions.ToListAsync(selectFolloweesQuery)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess List.ofSeq
    let followeesQuery = query {
      for u in dbContext.Users do
        where (userIds.Contains(u.Id))
        select u
    }
    let! followers =
      EntityFrameworkQueryableExtensions.ToListAsync(followeesQuery)
      |> Async.AwaitTask
      |> AR.catch
      |> AR.mapSuccess List.ofSeq
    return! mapUserEntities followers    
  }

module GetStream =
  open Chessie
  open User

  let subscribe (getStreamClient: GetStream.Client) (user: User) (UserId userId) =
    let (UserId followerUserId) = user.UserId
    let timelineFeed = GetStream.timelineFeed getStreamClient followerUserId
    let userFeed = GetStream.userFeed getStreamClient userId
    timelineFeed.FollowFeed(userFeed)
    |> Async.AwaitTask
    |> AR.catch

module Suave =
  open Auth
  open Chessie
  open Chiron
  open Domain
  open Persistence
  open Suave
  open Suave.Filters
  open Suave.Operators
  open User

  type UserDto = {
    Username: string
  } with
    static member ToJson (u: UserDto) =
      json {
        do! Json.write "username" u.Username
      }

  type UserDtoList = UserDtoList of (UserDto list) with
    static member ToJson (UserDtoList userDtos) =
      let usersJson =
        userDtos
        |> List.map (Json.serializeWith UserDto.ToJson)
      json {
        do! Json.write "users" usersJson
      }

  let private mapUsersToUserDtoList (users: User list) =
    users
    |> List.map (fun user -> { Username = user.Username.Value })
    |> UserDtoList

  type FollowUserRequest = FollowUserRequest of int with
    static member FromJson (_: FollowUserRequest) = json {
      let! userId = Json.read "userId"
      return FollowUserRequest userId
    }

  let private onFollowUserSuccess () =
    Successful.NO_CONTENT

  let private onFollowUserFailure (ex: System.Exception) =
    printfn "[onFollowUserFailure] %A" ex
    JSON.internalServerError

  let private handleFollowUser (followUser: FollowUser) (user: User) ctx = async {
    match JSON.deserialise ctx.request with
    | Success (FollowUserRequest userId) ->
      let! webpart =
        followUser user (UserId userId)
        |> AR.either onFollowUserSuccess onFollowUserFailure
      return! webpart ctx
    | Failure _ ->
      return! JSON.badRequest "Invalid follow user request" ctx
  }

  let private onFindFollowersSuccess (users: User list) =
    mapUsersToUserDtoList users
    |> Json.serialize
    |> JSON.ok

  let private onFindFollowersFailure (ex: System.Exception) =
    printfn "[onFindFollowersFailure] %A" ex
    JSON.internalServerError

  let private handleFindFollowers (findFollowers: FindFollowers) userId ctx = async {
    let! webpart =
      findFollowers (UserId userId)
      |> AR.either onFindFollowersSuccess onFindFollowersFailure
    return! webpart ctx    
  }

  let private onFindFolloweesSuccess (users: User list) =
    mapUsersToUserDtoList users
    |> Json.serialize
    |> JSON.ok

  let private onFindFolloweesFailure (ex: System.Exception) =
    printfn "[onFindFolloweesFailure] %A" ex
    JSON.internalServerError

  let private handleFindFollowees (findFollowers: FindFollowers) userId ctx = async {
    let! webpart =
      findFollowers (UserId userId)
      |> AR.either onFindFolloweesSuccess onFindFolloweesFailure
    return! webpart ctx    
  }

  let webpart getDataContext getStreamClient =
    let createFollowing = createFollowing getDataContext
    let subscribe = GetStream.subscribe getStreamClient
    let followUser = followUser subscribe createFollowing
    let handleFollowUser = handleFollowUser followUser
    let findFollowers = findFollowers getDataContext
    let findFollowees = findFollowees getDataContext
    choose [
      GET >=> pathScan "/%d/followers" (handleFindFollowers findFollowers)
      GET >=> pathScan "/%d/followees" (handleFindFollowees findFollowees)
      POST >=> path "/follow" >=> requiresAuth2 handleFollowUser
    ]
