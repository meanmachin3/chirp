$(() => {
  const tweetTemplate =
  `{{#users}}
    <div class="tweet-1">
          <div class="tweet-txt">
            <div class="tweet-name-date">
            <a href="/{{username}}"><span class="twitter-account"> @{{username}}</span></a>
            </div>
            <div class="message">
              {{post}}
            </div>
            <div class="tweet-icons">
              <i class="fas fa-image"></i>
              <i class="fas fa-gift"></i>
              <i class="fas fa-retweet"></i>
              <i class="fas fa-heart"></i>
            </div>
          </div>
        </div>
  {{/users}}
  `
  const renderTweets = (data, $body) => {
    data.users = data.users.filter((user) => {return user.username == fsTweet.user.id})
    const htmlOutput = Mustache.render(tweetTemplate, data)
    $body.html(htmlOutput)
  }

  const loadAllTweets = () => {
    const url = `/all`
    $.getJSON(url, data => renderTweets(data, $('#tweet')))
  }

  loadAllTweets()
})
