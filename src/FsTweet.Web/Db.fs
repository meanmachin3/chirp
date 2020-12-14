module Database

open Chessie
open Chessie.ErrorHandling
open Microsoft.EntityFrameworkCore

let makeConnectionString databaseUrl =
  let uri = new System.Uri(databaseUrl)
  let userInfo = uri.UserInfo
  let userInfoBits = userInfo.Split([|':'|])
  let username = userInfoBits.[0]
  let password = userInfoBits.[1]
  sprintf
    "Server=%s;Port=%d;User Id=%s;Password=%s;Database=%s;"
    uri.Host
    uri.Port
    username
    password
    (uri.AbsolutePath.Substring 1)

type [<CLIMutable>] User = {
  Id: int
  Username: string
  PasswordHash: string
  Email: string
  EmailVerificationCode: string
  IsEmailVerified: bool
}

type [<CLIMutable>] Tweet = {
  Id: System.Guid
  Post: string
  UserId: int
  TweetedAt: System.DateTime
}

type [<CLIMutable>] Social = {
  Id: System.Guid
  FollowerUserId: int
  FollowingUserId: int
}

type AppDbContext =
  inherit DbContext
  
  new(options: DbContextOptions<AppDbContext>) = { inherit DbContext(options) }
  [<DefaultValue>] val mutable users: DbSet<User>
  member this.Users
    with get() = this.users
    and set users = this.users <- users

  [<DefaultValue>] val mutable tweets: DbSet<Tweet>
  member this.Tweets
    with get() = this.tweets
    and set tweets = this.tweets <- tweets

  [<DefaultValue>] val mutable social: DbSet<Social>
  member this.Social
    with get() = this.social
    and set social = this.social <- social

type GetDataContext = unit -> AppDbContext

let dataContext (connectionString: string): GetDataContext =
  let optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
  optionsBuilder.UseNpgsql(connectionString) |> ignore
  let options = optionsBuilder.Options
  fun _ -> new AppDbContext(options)

let saveChangesAsync (dbContext: AppDbContext) =
  dbContext.SaveChangesAsync()
  |> Async.AwaitTask
  |> Async.map ignore
  |> AR.catch
