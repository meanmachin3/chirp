$(() => {

  const timeAgo = () => (val, render) =>
    moment(render(val) + 'Z').fromNow()

  const template =
    `<div class="tweet_read_view bg-info">
      <span class="text-muted">
        @{{tweet.username}} - {{#timeAgo}}{{tweet.time}}{{/timeAgo}}
      </span>
      <p>{{tweet.tweet}}</p>
    </div>`

  window.renderTweet = ($parent, tweet) => {
    const htmlOutput = Mustache.render(template, {
      tweet,
      timeAgo
    })
    $parent.prepend(htmlOutput)
  }
})
