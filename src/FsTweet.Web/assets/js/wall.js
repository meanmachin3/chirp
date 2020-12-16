$(() => {
  $('#tweetForm').submit(event => {
    const $this = $(this)
    const $tweet = $('#tweet')
    event.preventDefault()
    $this.prop('disabled', true)
    $.ajax({
      url: '/tweets',
      type: 'post',
      data: JSON.stringify({ post: $tweet.val(), retweet: false }),
      contentType: 'application/json'
    }).done(() => {
      $this.prop('disabled', false)
      $tweet.val('')
    }).fail((jqXHR, textStatus, errorThrown) => {
      console.log({ jqXHR, textStatus, errorThrown })
      alert('Something went wrong!')
    })
  })

  $(document).on('click', '.retweet', function(event) {
    const $this = $(this)
    const $tweet = $this.siblings('p').text()
    
    event.preventDefault()
    $this.prop('disabled', true)
    $.ajax({
      url: '/tweets',
      type: 'post',
      data: JSON.stringify({ post: $tweet.val(), retweet: true }),
      contentType: 'application/json'
    }).done(() => {
      $this.prop('disabled', false)
      $tweet.val('')
    }).fail((jqXHR, textStatus, errorThrown) => {
      console.log({ jqXHR, textStatus, errorThrown })
      alert('Something went wrong!')
    })
  })

  const usersTemplate =
  `{{#users}}
    <div class="well user-card">
      <a href="/{{username}}">@{{username}}</a>
      <p>{{post}}</p>
    </div>
  {{/users}}
  `

  const renderUsers = (data, $body) => {
    const htmlOutput = Mustache.render(usersTemplate, data)
    $body.html(htmlOutput)
  }

  const loadTweets = () => {
    const url = `/timeline`
    $.getJSON(url, data => renderUsers(data, $('#wall')))
  }

  const saveLocalStorage = data => {
    localStorage.setItem(fsTweet.user.id, JSON.stringify(data))
  }

  const loadFollowees = () => {
    const url = `/${fsTweet.user.id}/followees`   
    $.getJSON(url, data => saveLocalStorage(data))
  }

  loadTweets()
  loadFollowees()

  var wsUri = "ws://localhost:5000/websocket";

  function init() {
    testWebSocket();
  }
  
  function testWebSocket() {
    websocket = new WebSocket(wsUri);
    websocket.onopen = function (evt) { onOpen(evt) };
    websocket.onclose = function (evt) { onClose(evt) };
    websocket.onmessage = function (evt) { onMessage(evt) };
    websocket.onerror = function (evt) { onError(evt) };
  }

  function onOpen(evt) {
    doSend("Connected");
    console.log("Connected")
  }

  function onClose(evt) {
    writeToScreen("Disconnected.  Will reconnect in 5 seconds.");
    setTimeout(function () {
      init();
    }, 5000);
  }

  function onMessage(evt) {
    console.log(evt)
    writeToScreen(evt)
  }

  function onError(evt) {
    console.log(evt)
  }

  function doSend(message) {
    console.log("SENT: " + message)
    websocket.send(message);
  }

  function writeToScreen(message) {
    debugger
    let element = $("#wall")
    let followers = JSON.parse(localStorage.getItem(fsTweet.user.id))["users"]
    let [username, post] = message.data.split("|")
    for (var i = 0; i < followers.length; i++) {
      if (followers[i].username == username) {
        let html = `<div class="well user-card">
                    <a href="/${username}">${username}</a>
                    <p>${post}</p>
                    </div>`
        element.append(html)
      }
    }
  }

  window.addEventListener("load", init, false);

})
